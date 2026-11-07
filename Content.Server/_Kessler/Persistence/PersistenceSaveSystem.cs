using System.Linq;
using System.Runtime.ExceptionServices;
using Content.Shared.Station.Components;
using Content.Server._NF.PublicTransit;
using Content.Shared.Mind;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Events;
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
    [Dependency] private readonly IDependencyCollection _dependency = default!;
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
    /// Prototypes with any of these components are left out of persistent saves even if map files may hold them. They
    /// are momentary and only valid while the entity that made them points at them (PrototypeSweepTest found these).
    /// </summary>
    private static readonly string[] MomentaryComponents =
    {
        // Beams and lightning in flight, and their invisible controllers.
        "Beam",
        // Invisible power loads that exist while someone is being shocked.
        "Electrocution",
        // Placeholders in hands while pulling or holding something two-handed. A saved one blocked the hand forever.
        "VirtualItem",
        // A cast fishing lure.
        "FishingLure",
        // Spray and splatter in flight; saving one threw.
        "Vapor",
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
        foreach (var component in MomentaryComponents)
        {
            if (proto.Components.ContainsKey(component))
                return false;
        }

        return proto.MapSavable ? !UnsavedPrototypes.Contains(proto.ID) : !IsTransient(proto);
    }

    /// <summary>
    /// How many times <see cref="Serialize"/> re-serializes to leave out entities that threw.
    /// </summary>
    private const int MaxExclusionPasses = 3;

    /// <summary>
    /// Entities the last <see cref="Serialize"/> left out because serializing them threw, with everything inside them.
    /// </summary>
    public IReadOnlyCollection<EntityUid> LastLeftOut { get; private set; } = Array.Empty<EntityUid>();

    /// <summary>
    /// Serializes the given entities and their children. If <paramref name="applyPolicy"/> is true, prototypes marked
    /// <c>save: false</c> are saved too unless they are transient; their ids are added to <paramref name="forced"/>. Prototypes
    /// in <see cref="UnsavedPrototypes"/> are left out.
    /// </summary>
    /// <remarks>
    /// When a component throws, the engine drops its entity but can still write the entity's children, which then
    /// point at a parent that is not in the save and make the whole save fail to load. So when the options ignore
    /// throwing entities, the save is serialized again with those entities and everything inside them left out from
    /// the start; references to them are written as invalid. They are listed in <see cref="LastLeftOut"/>.
    /// </remarks>
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
        var excluded = new HashSet<EntityUid>();
        LastLeftOut = Array.Empty<EntityUid>();

        // Bus routes cannot be serialized; they are rebuilt after a load (PersistenceLoadSystem).
        _publicTransit.DetachRoutesForSave();
        try
        {
            SerializedPass? result = null;
            Exception? error = null;
            try
            {
                result = SerializeOnce(roots, options, included, excluded);
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
                result = SerializeOnce(roots, options, included, excluded);
            }
            else if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            for (var pass = 0; pass < MaxExclusionPasses && result!.Erroring.Count > 0; pass++)
            {
                foreach (var uid in result.Erroring)
                {
                    AddWithDescendants(uid, excluded);
                }

                Log.Error($"Leaving {excluded.Count} entities out of the save because serializing them threw (see the " +
                          "errors above), together with everything inside them: " +
                          string.Join(", ", result.Erroring.Select(uid => ToPrettyString(uid))));
                result = SerializeOnce(roots, options, included, excluded);
            }

            LastLeftOut = excluded;
            StripRuntimeComponents(result!.Node);
            return (result.Node, result.Category);
        }
        finally
        {
            _publicTransit.RestoreRoutesAfterSave();
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

    private sealed record SerializedPass(MappingDataNode Node, FileCategory Category, HashSet<EntityUid> Erroring);

    /// <summary>
    /// One serialization pass, like <see cref="MapLoaderSystem.SerializeEntitiesRecursive"/>, but it leaves out
    /// <paramref name="excluded"/> and reports the entities whose components threw.
    /// </summary>
    private SerializedPass SerializeOnce(
        HashSet<EntityUid> roots,
        SerializationOptions options,
        HashSet<EntityUid> included,
        HashSet<EntityUid> excluded)
    {
        var maps = roots.Select(uid => Transform(uid).MapID).ToHashSet();
        RaiseLocalEvent(new BeforeSerializationEvent(roots, maps));

        var serializer = new EntitySerializer(_dependency, options);
        // References to entities that are left out are written as invalid instead of dangling ids.
        serializer.ErroringEntities.UnionWith(excluded);
        serializer.OnIsSerializeable += (Entity<MetaDataComponent> ent, ref bool serializable) =>
        {
            included.Add(ent.Owner);
            if (excluded.Contains(ent.Owner))
                serializable = false;
        };
        serializer.SerializeEntityRecursive(roots);
        var data = serializer.Write();
        var category = serializer.GetCategory();
        RaiseLocalEvent(new AfterSerializationEvent(roots, data, category));

        var erroring = serializer.ErroringEntities.Where(uid => !excluded.Contains(uid)).ToHashSet();
        return new SerializedPass(data, category, erroring);
    }

    private void AddWithDescendants(EntityUid uid, HashSet<EntityUid> set)
    {
        if (!set.Add(uid) || !TryComp(uid, out TransformComponent? xform))
            return;

        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            AddWithDescendants(child, set);
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
        // Saving a whole map takes the minds and stations of everything on it. Saving single grids (a stored ship) only
        // takes those on the grids themselves, not on the rest of their map.
        var maps = new HashSet<EntityUid>();
        var grids = new HashSet<EntityUid>();
        foreach (var uid in entities)
        {
            if (HasComp<MapComponent>(uid))
                maps.Add(uid);
            else if (Transform(uid).GridUid is { } gridUid)
                grids.Add(gridUid);
            else if (Transform(uid).MapUid is { } mapUid)
                maps.Add(mapUid);
        }

        bool Saved(EntityUid? uid) => OnMaps(uid, maps) || OnGrids(uid, grids);

        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var uid, out var mind))
        {
            if (IsNullspaceRoot(uid) && (Saved(mind.OwnedEntity) || Saved(mind.VisitingEntity)))
                roots.Add(uid);
        }

        var stations = EntityQueryEnumerator<StationDataComponent>();
        while (stations.MoveNext(out var uid, out var station))
        {
            if (IsNullspaceRoot(uid) && station.Grids.Any(grid => Saved(grid)))
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

    private bool OnGrids(EntityUid? uid, HashSet<EntityUid> grids)
    {
        return uid != null && grids.Count > 0 && TryComp(uid, out TransformComponent? xform) && xform.GridUid is { } gridUid
               && grids.Contains(gridUid);
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
    /// Saves one grid (a ship) with the Kessler save policy to a file in the server data folder, for ship storage.
    /// </summary>
    public bool TrySaveGrid(EntityUid grid, ResPath path)
    {
        var options = SerializationOptions.Default with { Category = FileCategory.Grid };
        var (data, category) = Serialize([grid], options, applyPolicy: true, new List<string>());
        if (category != FileCategory.Grid)
        {
            Log.Error($"Failed to save {ToPrettyString(grid)} as a grid. Output: {category}");
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
