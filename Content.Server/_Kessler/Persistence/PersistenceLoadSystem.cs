using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.PublicTransit;
using Content.Server.Holiday;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.Maps;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Events;
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
