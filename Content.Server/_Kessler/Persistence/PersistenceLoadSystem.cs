using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._NF.PublicTransit;
using Content.Server.Holiday;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.Maps;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Loads saves of a running sector.
/// </summary>
/// <remarks>
/// Map migrations (<c>migration.yml</c>, <c>nf_migration.yml</c>) and holiday replacements rename or delete
/// prototypes whenever a file is read. They are written for map files, and many of them target prototypes that still
/// exist (Frontier replacing upstream items, holiday variants). Applied to a save of live entities, they would replace
/// or delete those entities on every restart. While loading a save, only entries whose source prototype no longer
/// exists are kept, so renamed and removed prototypes are still migrated.
/// </remarks>
public sealed class PersistenceLoadSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly PublicTransitSystem _publicTransit = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    private bool _loadingSave;

    /// <summary>
    /// Migration entries skipped during the last save load, because their source prototype still exists.
    /// </summary>
    public int SkippedMigrations { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BeforeEntityReadEvent>(OnBeforeEntityRead,
            after: new[] { typeof(MapMigrationSystem), typeof(HolidaySystem) });
    }

    private static readonly Regex EntityLine = new(@"^  - uid: (\d+)$", RegexOptions.Compiled);
    private static readonly Regex ComponentLine = new(@"^    - type: (\w+)$", RegexOptions.Compiled);
    private static readonly Regex ParentLine = new(@"^      parent: (\d+)$", RegexOptions.Compiled);
    private static readonly Regex ContainedLine = new(@"^\s+(?:ent: |- )(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Checks that every parent and container entry in a save refers to an entity the save contains. Loading a save
    /// that fails this check creates the broken entities first and then fails while starting them, and the map
    /// loader's cleanup of such a load can itself fail and leave half the save running.
    /// </summary>
    public bool TryValidateSave(ResPath path, [NotNullWhen(false)] out string? problem)
    {
        var defined = new HashSet<int>();
        var references = new List<(int Entity, string Component, int Target)>();
        var entity = 0;
        string? component = null;
        using (var reader = _res.UserData.OpenText(path))
        {
            while (reader.ReadLine() is { } line)
            {
                if (EntityLine.Match(line) is { Success: true } uid)
                {
                    entity = int.Parse(uid.Groups[1].Value);
                    defined.Add(entity);
                    component = null;
                }
                else if (ComponentLine.Match(line) is { Success: true } type)
                {
                    component = type.Groups[1].Value;
                }
                else if (component == "Transform" && ParentLine.Match(line) is { Success: true } parent)
                {
                    references.Add((entity, component, int.Parse(parent.Groups[1].Value)));
                }
                else if (component == "ContainerContainer" && ContainedLine.Match(line) is { Success: true } contained)
                {
                    references.Add((entity, component, int.Parse(contained.Groups[1].Value)));
                }
            }
        }

        var missing = references.Where(r => !defined.Contains(r.Target)).ToList();
        if (missing.Count == 0)
        {
            problem = null;
            return true;
        }

        var targets = missing.Select(r => r.Target).Distinct().ToList();
        problem = $"{missing.Count} references to {targets.Count} entities that are not in the save (" +
                  string.Join(", ", missing.Take(5).Select(r => $"{r.Component} of {r.Entity} -> {r.Target}")) + ")";
        return false;
    }

    /// <summary>
    /// Loads a save file without applying migrations to prototypes that still exist.
    /// </summary>
    public bool TryLoadSave(ResPath path, MapLoadOptions options, [NotNullWhen(true)] out LoadResult? result)
    {
        _loadingSave = true;
        SkippedMigrations = 0;
        try
        {
            if (!_mapLoader.TryLoadGeneric(path, out result, options))
                return false;
        }
        finally
        {
            _loadingSave = false;
        }

        // Data that is derived rather than saved, rebuilt once every loaded entity has started.
        _publicTransit.RegisterLoadedStops(result);
        RestoreNpcOwners(result);
        RestorePlayerMindLinks(result);
        return true;
    }

    /// <summary>
    /// Loads a stored ship onto <paramref name="map"/> the same way as the sector save: no migrations for prototypes
    /// that still exist, and the same fix-ups afterwards. Fails, deleting whatever loaded, unless the file holds exactly
    /// one grid.
    /// </summary>
    public bool TryLoadStoredGrid(ResPath path, MapId map, Vector2 offset, [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        grid = null;
        var options = new MapLoadOptions
        {
            MergeMap = map,
            Offset = offset,
            ExpectedCategory = FileCategory.Grid,
        };

        if (!TryLoadSave(path, options, out var result))
            return false;

        if (result.Grids.Count != 1)
        {
            Log.Error($"Stored ship {path} held {result.Grids.Count} grids instead of one; not loaded");
            _mapLoader.Delete(result);
            return false;
        }

        grid = result.Grids.Single();
        return true;
    }

    /// <summary>
    /// Points the player data of loaded minds' users at those minds.
    /// </summary>
    /// <remarks>
    /// A loaded mind registers its user with the mind system on startup, but the user's player data still points at
    /// their previous mind (or none). Players who connected earlier in this process would otherwise be out of sync with
    /// their mind; after a process restart there is no player data yet and this does nothing. A player who is
    /// connected right now is put into the mind's body.
    /// </remarks>
    private void RestorePlayerMindLinks(LoadResult result)
    {
        var query = EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var uid, out var mind))
        {
            if (!result.Entities.Contains(uid) || mind.UserId is not { } userId)
                continue;

            if (!_players.TryGetPlayerData(userId, out var data) || data.ContentData()?.Mind == uid)
                continue;

            // SetUserId only updates the player data when the user changes, so clear it and set it again.
            _mind.SetUserId(uid, null, mind);
            _mind.SetUserId(uid, userId, mind);
        }
    }

    /// <summary>
    /// NPC blackboards are not saved (their serializer can only read), and the blackboard's owner is set at map-init,
    /// which does not run on load. Without it, every loaded NPC fails to plan.
    /// </summary>
    private void RestoreNpcOwners(LoadResult result)
    {
        var query = EntityQueryEnumerator<HTNComponent>();
        while (query.MoveNext(out var uid, out var htn))
        {
            if (result.Entities.Contains(uid))
                htn.Blackboard.SetValue(NPCBlackboard.Owner, uid);
        }
    }

    private void OnBeforeEntityRead(BeforeEntityReadEvent ev)
    {
        if (!_loadingSave)
            return;

        foreach (var id in ev.RenamedPrototypes.Keys.ToArray())
        {
            if (!_proto.HasIndex<EntityPrototype>(id))
                continue;

            ev.RenamedPrototypes.Remove(id);
            SkippedMigrations++;
        }

        SkippedMigrations += ev.DeletedPrototypes.RemoveWhere(id => _proto.HasIndex<EntityPrototype>(id));
    }
}
