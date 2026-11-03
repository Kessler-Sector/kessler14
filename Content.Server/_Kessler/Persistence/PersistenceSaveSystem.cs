using System.Linq;
using System.Runtime.ExceptionServices;
using Content.Shared.Station.Components;
using Content.Server._NF.PublicTransit;
using Content.Shared.Mind;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Serializes a running sector for a persistent save.
/// </summary>
/// <remarks>
/// Prototypes with <c>save: false</c> are skipped by the engine together with everything inside them. Upstream uses
/// it so mappers cannot save mobs into map files, which covers every mob, player body, borg, mech and vehicle. For a
/// persistent save those must be kept, so they are switched to savable for the duration of the save. Things that are
/// transient by nature (ghosts, sounds, effects, AI eyes) stay unsaved.
/// </remarks>
public sealed class PersistenceSaveSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly PublicTransitSystem _publicTransit = default!;

    /// <summary>
    /// Prototypes with any of these components stay unsaved even in a persistent save.
    /// </summary>
    private static readonly string[] TransientComponents =
    {
        "Ghost",
        "TimedDespawn",
        "PointingArrow",
        "InterviewHologram",
    };

    /// <summary>
    /// Prototypes that stay unsaved even in a persistent save.
    /// </summary>
    private static readonly HashSet<string> TransientPrototypes = new()
    {
        "Audio",
        "Exclamation",
        // Polymorph forms: the original body lives on a separate paused map.
        "EtherealJaunt",
        "DesynchronizedPocket",
        // Remote eyes and holograms owned by an AI core.
        "ShipAiEye",
        "ShipAiProjection",
        "StationAiHolo",
    };

    /// <summary>
    /// Prototypes that map files may contain but persistent saves leave out.
    /// </summary>
    private static readonly HashSet<string> UnsavedPrototypes = new()
    {
        // FTL arrival effect. It points at a ship in FTL, and ships are landed before a persistent save.
        "FtlVisualizerEntity",
    };

    /// <summary>
    /// Components removed from persistent saves. They hold runtime-only links that the engine does not mark as
    /// unsaved because map files never contain them.
    /// </summary>
    private static readonly HashSet<string> RuntimeComponents = new()
    {
        // Link from a body to the connected player. A loaded body would claim a player with no session.
        "Actor",
    };

    /// <summary>
    /// Whether a prototype marked <c>save: false</c> is still transient for persistence purposes.
    /// </summary>
    public bool IsTransient(EntityPrototype proto)
    {
        if (TransientPrototypes.Contains(proto.ID))
            return true;

        foreach (var component in TransientComponents)
        {
            if (proto.Components.ContainsKey(component))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether entities of a prototype are written to a persistent save.
    /// </summary>
    public bool IsPersisted(EntityPrototype proto)
    {
        return proto.MapSavable ? !UnsavedPrototypes.Contains(proto.ID) : !IsTransient(proto);
    }

    /// <summary>
    /// Serializes the given entities and their children. If <paramref name="applyPolicy"/> is true, prototypes marked
    /// <c>save: false</c> are saved too unless they are transient; their ids are added to <paramref name="forced"/>. Prototypes
    /// in <see cref="UnsavedPrototypes"/> are left out.
    /// </summary>
    public (MappingDataNode Node, FileCategory Category) Serialize(
        HashSet<EntityUid> entities,
        SerializationOptions options,
        bool applyPolicy,
        List<string> forced)
    {
        var flipped = new List<EntityPrototype>();
        var unsaved = new List<EntityPrototype>();
        if (applyPolicy)
        {
            foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || proto.MapSavable == IsPersisted(proto))
                    continue;

                if (proto.MapSavable)
                {
                    proto.MapSavable = false;
                    unsaved.Add(proto);
                    continue;
                }

                proto.MapSavable = true;
                flipped.Add(proto);
                forced.Add(proto.ID);
            }
        }

        var roots = WithNullspaceRoots(entities);
        var included = new HashSet<EntityUid>();
        void OnIsSerializable(Entity<MetaDataComponent> ent, ref bool serializable) => included.Add(ent.Owner);

        _mapLoader.OnIsSerializable += OnIsSerializable;
        // Bus routes cannot be serialized; they are rebuilt after a load (PersistenceLoadSystem).
        _publicTransit.DetachRoutesForSave();
        try
        {
            (MappingDataNode Node, FileCategory Category)? result = null;
            Exception? error = null;
            try
            {
                result = _mapLoader.SerializeEntitiesRecursive(roots, options);
            }
            catch (Exception e)
            {
                error = e;
            }

            // Safety net for nullspace entities with children that WithNullspaceRoots did not anticipate. Their broken
            // references may also be what made the first pass throw.
            var missed = included.Where(uid => !roots.Contains(uid) && IsNullspaceRoot(uid) && Transform(uid).ChildCount > 0)
                .ToList();
            if (missed.Count > 0)
            {
                Log.Warning($"Re-serializing with {missed.Count} auto-included nullspace entities as roots: "
                            + string.Join(", ", missed.Select(uid => ToPrettyString(uid))));
                roots.UnionWith(missed);
                result = _mapLoader.SerializeEntitiesRecursive(roots, options);
            }
            else if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            StripRuntimeComponents(result!.Value.Node);
            return result.Value;
        }
        finally
        {
            _publicTransit.RestoreRoutesAfterSave();
            _mapLoader.OnIsSerializable -= OnIsSerializable;
            foreach (var proto in flipped)
            {
                proto.MapSavable = false;
            }

            foreach (var proto in unsaved)
            {
                proto.MapSavable = true;
            }
        }
    }

    /// <summary>
    /// Adds the nullspace entities that belong with the given entities: the minds of bodies on their maps and the
    /// stations that own grids there.
    /// </summary>
    /// <remarks>
    /// The engine includes referenced nullspace entities on its own, but it writes them and their children without
    /// reserving their YAML ids first (<c>EntitySerializer.ProcessAutoInclude</c>). A mind written before its job role
    /// child then records the role as a missing entity and its role container fails to serialize. Passing these
    /// entities as roots serializes them like everything else.
    /// </remarks>
    private HashSet<EntityUid> WithNullspaceRoots(HashSet<EntityUid> entities)
    {
        var roots = new HashSet<EntityUid>(entities);
        var maps = new HashSet<EntityUid>();
        foreach (var uid in entities)
        {
            if (Transform(uid).MapUid is { } mapUid)
                maps.Add(mapUid);
        }

        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var uid, out var mind))
        {
            if (IsNullspaceRoot(uid) && (OnMaps(mind.OwnedEntity, maps) || OnMaps(mind.VisitingEntity, maps)))
                roots.Add(uid);
        }

        var stations = EntityQueryEnumerator<StationDataComponent>();
        while (stations.MoveNext(out var uid, out var station))
        {
            if (IsNullspaceRoot(uid) && station.Grids.Any(grid => OnMaps(grid, maps)))
                roots.Add(uid);
        }

        return roots;
    }

    private static void StripRuntimeComponents(MappingDataNode data)
    {
        if (!data.TryGet("entities", out SequenceDataNode? protos))
            return;

        foreach (var proto in protos)
        {
            if (proto is not MappingDataNode protoNode || !protoNode.TryGet("entities", out SequenceDataNode? entities))
                continue;

            foreach (var entity in entities)
            {
                if (entity is not MappingDataNode entityNode
                    || !entityNode.TryGet("components", out SequenceDataNode? components))
                {
                    continue;
                }

                for (var i = components.Count - 1; i >= 0; i--)
                {
                    if (components[i] is MappingDataNode component
                        && component.TryGet("type", out ValueDataNode? type)
                        && RuntimeComponents.Contains(type.Value))
                    {
                        components.RemoveAt(i);
                    }
                }
            }
        }
    }

    private bool IsNullspaceRoot(EntityUid uid)
    {
        return !Transform(uid).ParentUid.IsValid() && !HasComp<MapComponent>(uid);
    }

    private bool OnMaps(EntityUid? uid, HashSet<EntityUid> maps)
    {
        return uid != null && TryComp(uid, out TransformComponent? xform) && xform.MapUid is { } mapUid && maps.Contains(mapUid);
    }

    /// <summary>
    /// Saves a map with the Kessler save policy to a file in the server data folder.
    /// </summary>
    public bool TrySaveMap(EntityUid map, ResPath path)
    {
        var options = SerializationOptions.Default with { Category = FileCategory.Map };
        var (data, category) = Serialize([map], options, applyPolicy: true, new List<string>());
        if (category != FileCategory.Map)
        {
            Log.Error($"Failed to save {ToPrettyString(map)} as a map. Output: {category}");
            return false;
        }

        Write(path, data);
        return true;
    }

    /// <summary>
    /// Writes serialized data to a YAML file in the server data folder, the same way <see cref="MapLoaderSystem"/>
    /// does.
    /// </summary>
    public void Write(ResPath path, MappingDataNode data)
    {
        path = path.ToRootedPath();
        _res.UserData.CreateDir(path.Directory);
        var document = new YamlDocument(data.ToYaml());
        using var writer = _res.UserData.OpenWriteText(path);
        var stream = new YamlStream { document };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);
    }
}
