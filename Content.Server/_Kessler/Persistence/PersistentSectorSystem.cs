using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Content.Server._NF.RoundNotifications.Events;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared._NF.CCVar;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Shared._Kessler.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Keeps the sector persistent across rounds and server restarts: saves the sector map at round end, at shutdown and
/// optionally on a timer, and loads it at round start in place of generating a new sector.
/// </summary>
public sealed class PersistentSectorSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly PersistenceLoadSystem _persistenceLoad = default!;
    [Dependency] private readonly PersistenceSaveSystem _persistenceSave = default!;
    [Dependency] private readonly SectorSnapshotSystem _snapshot = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    /// <summary>
    /// Version of the save layout. Saves with a newer version are not loaded.
    /// </summary>
    public const int SaveFormatVersion = 1;

    private const string SaveFile = "sector.yml";
    private const string MetaFile = "sector.meta.json";
    private const string SnapshotFile = "sector.snapshot.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>
    /// Whether this round's sector was loaded from the save rather than generated.
    /// </summary>
    public bool SectorLoaded { get; private set; }

    /// <summary>
    /// Differences found by comparing the loaded sector with its save snapshot, or null if no check ran.
    /// </summary>
    public int? LastLoadDifferences { get; private set; }

    /// <summary>
    /// Minds that came from the save this round.
    /// </summary>
    private readonly HashSet<EntityUid> _loadedMinds = new();

    private TimeSpan _nextAutosave;

    public bool Enabled => _cfg.GetCVar(KesslerCVars.PersistenceEnabled);

    private ResPath Folder => new ResPath(_cfg.GetCVar(KesslerCVars.PersistencePath)).ToRootedPath();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnPlayerBeforeSpawn);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var minutes = _cfg.GetCVar(KesslerCVars.PersistenceAutosaveMinutes);
        if (!Enabled || minutes <= 0 || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (_nextAutosave == TimeSpan.Zero)
            _nextAutosave = _timing.RealTime + TimeSpan.FromMinutes(minutes);

        if (_timing.RealTime < _nextAutosave)
            return;

        _nextAutosave = _timing.RealTime + TimeSpan.FromMinutes(minutes);
        TrySaveSector("autosave");
    }

    #region Loading

    /// <summary>
    /// Loads the saved sector, if persistence is enabled and a save exists. Called by the game ticker while it loads
    /// the round's maps.
    /// </summary>
    public bool TryLoadSector(out MapId mapId)
    {
        mapId = MapId.Nullspace;
        SectorLoaded = false;
        LastLoadDifferences = null;
        _loadedMinds.Clear();

        if (!Enabled)
            return false;

        var savePath = Folder / SaveFile;
        if (!_res.UserData.Exists(savePath))
        {
            Log.Info($"No persistent sector save at {savePath}; generating a new sector.");
            return false;
        }

        if (!TryReadMeta(out var meta) || meta.Format > SaveFormatVersion)
        {
            FailLoad($"save format {meta?.Format.ToString() ?? "unknown"} is not supported (this build: {SaveFormatVersion})");
            return false;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        if (!_persistenceLoad.TryValidateSave(savePath, out var problem))
        {
            FailLoad($"the save is broken: {problem}");
            return false;
        }

        var options = new MapLoadOptions { ExpectedCategory = FileCategory.Map };
        // Everything that exists before the load, so a failed load can be cleaned up completely.
        var existing = EntityManager.GetEntities().ToHashSet();
        LoadResult? result;
        try
        {
            if (!_persistenceLoad.TryLoadSave(savePath, options, out result) || result.Maps.Count != 1)
            {
                DeleteLoadedEntities(existing);
                FailLoad("the save file could not be loaded");
                return false;
            }
        }
        catch (Exception e)
        {
            DeleteLoadedEntities(existing);
            FailLoad($"loading threw: {e}");
            return false;
        }

        mapId = result.Maps.Single().Comp.MapId;
        SectorLoaded = true;
        foreach (var uid in result.Entities)
        {
            if (HasComp<MindComponent>(uid))
                _loadedMinds.Add(uid);
        }

        Log.Info($"Loaded persistent sector ({result.Entities.Count} entities, saved {meta.SavedUtc:u}, " +
                 $"{_persistenceLoad.SkippedMigrations} migrations skipped) in {stopwatch.Elapsed.TotalSeconds:F2} s.");
        VerifyLoadedSector(result.Maps.Single().Owner);

        // Selling needs the shipyard map, which a generated round only creates on the first purchase.
        if (_cfg.GetCVar(NFCCVars.Shipyard))
            _shipyard.SetupShipyardIfNeeded();

        return true;
    }

    /// <summary>
    /// Compares the loaded sector with the snapshot taken when it was saved and logs every difference.
    /// </summary>
    private void VerifyLoadedSector(EntityUid map)
    {
        var path = Folder / SnapshotFile;
        if (!_res.UserData.Exists(path))
            return;

        SectorSnapshot? saved;
        try
        {
            saved = JsonSerializer.Deserialize<SectorSnapshot>(_res.UserData.ReadAllText(path), JsonOptions);
        }
        catch (JsonException e)
        {
            Log.Warning($"Could not read {path}: {e.Message}");
            return;
        }

        if (saved == null)
            return;

        // Entities the save leaves out (sounds started during the load, ...) cannot have come from it.
        var loaded = _snapshot.Snapshot(_snapshot.CollectTree([map]).Where(IsSaved));
        var diffs = SectorSnapshot.Compare(saved, loaded);
        var papers = SectorSnapshot.ChangedPapers(saved, loaded);
        LastLoadDifferences = diffs.Count + papers;
        if (diffs.Count == 0 && papers == 0)
        {
            Log.Info($"Verified loaded sector against its save snapshot: {loaded.EntityCount} entities, no differences.");
            return;
        }

        var lines = diffs.Take(50).Select(d => $"  {d.Metric} {d.Key}: {d.Before} -> {d.After}");
        Log.Warning($"Loaded sector differs from its save snapshot ({diffs.Count} differences, {papers} changed papers):"
                    + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// Deletes every entity a failed load left behind. The map loader cleans up after itself, but its cleanup can
    /// throw halfway, which left a half-loaded sector (stations, minds, ships) running next to the generated one.
    /// </summary>
    private void DeleteLoadedEntities(HashSet<EntityUid> existing)
    {
        var leftovers = EntityManager.GetEntities().Where(uid => !existing.Contains(uid)).ToList();
        if (leftovers.Count == 0)
            return;

        // Maps first: deleting a map deletes everything on it. Then whatever is left, such as minds and stations.
        var deleted = 0;
        foreach (var uid in leftovers.OrderByDescending(uid => HasComp<MapComponent>(uid)))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            try
            {
                Del(uid);
                deleted++;
            }
            catch (Exception e)
            {
                Log.Error($"Could not delete {ToPrettyString(uid)} left by the failed load: {e}");
            }
        }

        var remaining = leftovers.Count(uid => !TerminatingOrDeleted(uid));
        Log.Warning($"Deleted {deleted} entities left by the failed load ({leftovers.Count} found, {remaining} remain).");
    }

    /// <summary>
    /// A save that cannot be loaded is moved aside, so the next save does not rotate it out of the backups.
    /// </summary>
    private void FailLoad(string reason)
    {
        var failed = Folder / "failed" / DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        _res.UserData.CreateDir(failed);
        MoveIfExists(Folder / SaveFile, failed / SaveFile);
        MoveIfExists(Folder / MetaFile, failed / MetaFile);
        MoveIfExists(Folder / SnapshotFile, failed / SnapshotFile);

        var message = $"Persistent sector could not be loaded ({reason}). It was moved to {failed}; a new sector is generated.";
        Log.Error(message);
        _chat.SendAdminAnnouncement(message);
    }

    private bool TryReadMeta([NotNullWhen(true)] out SaveMeta? meta)
    {
        meta = null;
        var path = Folder / MetaFile;
        if (!_res.UserData.Exists(path))
            return false;

        try
        {
            meta = JsonSerializer.Deserialize<SaveMeta>(_res.UserData.ReadAllText(path));
        }
        catch (JsonException e)
        {
            Log.Error($"Could not read {path}: {e.Message}");
        }

        return meta != null;
    }

    private void OnPlayerBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        // A player whose character came from the save gets their own body back instead of a new character.
        if (ev.Handled
            || !_mind.TryGetMind(ev.Player.UserId, out var mindId, out var mind)
            || !_loadedMinds.Contains(mindId.Value)
            || mind.CurrentEntity is not { } body
            || TerminatingOrDeleted(body))
        {
            return;
        }

        _players.SetAttachedEntity(ev.Player, body);
        ev.Handled = true;
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        if (!SectorLoaded)
            return;

        // Players who stayed connected through a round restart were attached to their bodies when the save loaded,
        // but are still in the lobby.
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame
                || session.AttachedEntity == null
                || _ticker.UserHasJoinedGame(session)
                || !_mind.TryGetMind(session.UserId, out var mindId, out _)
                || !_loadedMinds.Contains(mindId.Value))
            {
                continue;
            }

            _ticker.PlayerJoinGame(session);
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        SectorLoaded = false;
        _loadedMinds.Clear();
        _nextAutosave = TimeSpan.Zero;
    }

    #endregion

    #region Saving

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        // Raised right before the round restart cleanup, while the sector still exists.
        if (ev.New == GameRunLevel.PreRoundLobby && ev.Old != GameRunLevel.PreRoundLobby)
            TrySaveSector("round end");
    }

    /// <summary>
    /// Saves the sector before the server shuts down. Called from the server entry point.
    /// </summary>
    public void SaveOnShutdown()
    {
        if (!Enabled)
            return;

        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby)
        {
            Log.Info("Server shutting down in the lobby; the sector was already saved at round end.");
            return;
        }

        TrySaveSector("shutdown");
    }

    /// <summary>
    /// Saves the sector map, keeping the previous save as a backup.
    /// </summary>
    public bool TrySaveSector(string reason)
    {
        if (!Enabled)
            return false;

        if (!_map.TryGetMap(_ticker.DefaultMap, out var sectorMap))
        {
            Log.Warning($"Not saving the persistent sector ({reason}): no sector map ({_ticker.DefaultMap}).");
            return false;
        }

        try
        {
            SaveSector(sectorMap.Value, reason);
            return true;
        }
        catch (Exception e)
        {
            var message = $"Saving the persistent sector ({reason}) failed: {e}";
            Log.Error(message);
            _chat.SendAdminAnnouncement($"Saving the persistent sector ({reason}) failed. See the server log.");
            return false;
        }
    }

    private void SaveSector(EntityUid sectorMap, string reason)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Ships mid-jump are on a separate hyperspace map that is not saved; land them first.
        var settled = _shuttle.CompleteFtlForSave();

        var options = SerializationOptions.Default with
        {
            Category = FileCategory.Map,
            // One broken entity must not cost the whole sector; it is logged and left out.
            EntityExceptionBehaviour = EntityExceptionBehaviour.IgnoreEntityAndChildren,
            LogAutoInclude = null,
        };
        var snapshot = _snapshot.Snapshot(_snapshot.CollectTree([sectorMap]).Where(IsSaved));
        var (data, category) = _persistenceSave.Serialize([sectorMap], options, applyPolicy: true, new List<string>());
        if (category != FileCategory.Map)
            throw new InvalidOperationException($"Serialized category was {category}, expected {FileCategory.Map}");
        var serializeTime = stopwatch.Elapsed;

        var folder = Folder;
        _res.UserData.CreateDir(folder);
        _persistenceSave.Write(Temp(folder, SaveFile), data);
        var writeTime = stopwatch.Elapsed - serializeTime;

        var entities = data.TryGet("meta", out Robust.Shared.Serialization.Markdown.Mapping.MappingDataNode? metaNode)
                       && metaNode.TryGet("entityCount", out Robust.Shared.Serialization.Markdown.Value.ValueDataNode? countNode)
            ? int.Parse(countNode.Value)
            : 0;
        var meta = new SaveMeta
        {
            Format = SaveFormatVersion,
            SavedUtc = DateTime.UtcNow,
            Reason = reason,
            BuildVersion = _cfg.GetCVar(CVars.BuildVersion),
            EngineVersion = _cfg.GetCVar(CVars.BuildEngineVersion),
            Entities = entities,
        };
        _res.UserData.WriteAllText(Temp(folder, MetaFile), JsonSerializer.Serialize(meta, JsonOptions));
        _res.UserData.WriteAllText(Temp(folder, SnapshotFile), JsonSerializer.Serialize(snapshot, JsonOptions));

        // Everything is written before anything is replaced, so a crash leaves either the old save or the new one.
        RotateBackups(folder);
        foreach (var file in new[] { SaveFile, MetaFile, SnapshotFile })
        {
            _res.UserData.Rename(Temp(folder, file), folder / file);
        }

        Log.Info($"Saved persistent sector ({reason}): {entities} entities, {settled} ships landed from FTL, " +
                 $"serialize {serializeTime.TotalSeconds:F2} s, write {writeTime.TotalSeconds:F2} s, " +
                 $"total {stopwatch.Elapsed.TotalSeconds:F2} s.");

        var leftOut = _persistenceSave.LastLeftOut;
        if (leftOut.Count > 0)
        {
            _chat.SendAdminAnnouncement($"The persistent sector was saved without {leftOut.Count} entities that failed to " +
                                        "save (see the server log): " +
                                        string.Join(", ", leftOut.Take(5).Select(uid => ToPrettyString(uid).ToString())));
        }
    }

    /// <summary>
    /// Moves the current save into a timestamped backup folder and deletes the oldest backups beyond the limit.
    /// </summary>
    private void RotateBackups(ResPath folder)
    {
        var backups = folder / "backups";
        _res.UserData.CreateDir(backups);
        if (_res.UserData.Exists(folder / SaveFile)
            || _res.UserData.Exists(folder / MetaFile)
            || _res.UserData.Exists(folder / SnapshotFile))
        {
            var target = backups / DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            _res.UserData.CreateDir(target);
            MoveIfExists(folder / SaveFile, target / SaveFile);
            MoveIfExists(folder / MetaFile, target / MetaFile);
            MoveIfExists(folder / SnapshotFile, target / SnapshotFile);
        }

        var keep = Math.Max(0, _cfg.GetCVar(KesslerCVars.PersistenceBackups));
        var old = _res.UserData.DirectoryEntries(backups)
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();
        foreach (var name in old)
        {
            _res.UserData.Delete(backups / name);
        }
    }

    /// <summary>
    /// Whether the save policy keeps an entity's prototype, so the snapshot only counts what is actually saved.
    /// </summary>
    private bool IsSaved(EntityUid uid)
    {
        var proto = MetaData(uid).EntityPrototype;
        return proto == null || _persistenceSave.IsPersisted(proto);
    }

    private static ResPath Temp(ResPath folder, string file)
    {
        return folder / (file + ".tmp");
    }

    private void MoveIfExists(ResPath from, ResPath to)
    {
        if (_res.UserData.Exists(from))
            _res.UserData.Rename(from, to);
    }

    #endregion

    /// <summary>
    /// Stored next to the save so its version can be checked without parsing the save itself.
    /// </summary>
    public sealed class SaveMeta
    {
        public int Format { get; set; }
        public DateTime SavedUtc { get; set; }
        public string Reason { get; set; } = "";
        public string BuildVersion { get; set; } = "";
        public string EngineVersion { get; set; } = "";
        public int Entities { get; set; }
    }
}
