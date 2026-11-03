using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Mapping;
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
    /// Serializes the given entities and their children. If <paramref name="applyPolicy"/> is true, prototypes marked
    /// <c>save: false</c> are saved too unless they are transient; their ids are added to <paramref name="forced"/>.
    /// </summary>
    public (MappingDataNode Node, FileCategory Category) Serialize(
        HashSet<EntityUid> entities,
        SerializationOptions options,
        bool applyPolicy,
        List<string> forced)
    {
        var flipped = new List<EntityPrototype>();
        if (applyPolicy)
        {
            foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.MapSavable || proto.Abstract || IsTransient(proto))
                    continue;

                proto.MapSavable = true;
                flipped.Add(proto);
                forced.Add(proto.ID);
            }
        }

        try
        {
            return _mapLoader.SerializeEntitiesRecursive(entities, options);
        }
        finally
        {
            foreach (var proto in flipped)
            {
                proto.MapSavable = false;
            }
        }
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
