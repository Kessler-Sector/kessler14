using System.Diagnostics;
using System.IO;
using System.Linq;
using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Stage A persistence check: saves a map, loads it into a new paused map in the same server, and reports what changed.
/// Loading a copy next to the live sector duplicates stations and minds, so only run this on a dev server and restart
/// afterwards.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed class PersistenceRoundTripCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly SectorSnapshotSystem _snapshot = default!;

    private const string KeepFlag = "keep";
    private const string ForceSaveFlag = "forcesave";

    public override string Command => "persistenceroundtrip";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var options = CompletionHelper.MapIds(EntityManager)
            .Append(new CompletionOption(KeepFlag))
            .Append(new CompletionOption(ForceSaveFlag));
        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-persistenceroundtrip-hint"));
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var mapId = _ticker.DefaultMap;
        var keep = false;
        var forceSave = false;

        foreach (var arg in args)
        {
            if (arg == KeepFlag)
                keep = true;
            else if (arg == ForceSaveFlag)
                forceSave = true;
            else if (int.TryParse(arg, out var id))
                mapId = new MapId(id);
            else
            {
                shell.WriteError(Loc.GetString("cmd-persistenceroundtrip-bad-arg", ("arg", arg)));
                return;
            }
        }

        if (!_map.TryGetMap(mapId, out var mapUid))
        {
            shell.WriteError(Loc.GetString("cmd-persistenceroundtrip-no-map", ("map", mapId.ToString())));
            return;
        }

        var started = DateTime.UtcNow;
        var dir = new ResPath($"/kessler/roundtrip/{started:yyyyMMdd-HHmmss}");
        var savePath = dir / "sector.yml";
        var report = new RoundTripReport
        {
            StartedUtc = started,
            SourceMap = $"{EntityManager.ToPrettyString(mapUid.Value)} (map {mapId})",
            ForceSave = forceSave,
            SavePath = savePath.ToString(),
        };

        shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-start", ("map", report.SourceMap)));
        _res.UserData.CreateDir(dir);

        LoadResult? loaded = null;
        try
        {
            loaded = Run(mapUid.Value, savePath, report);
        }
        catch (Exception e)
        {
            report.Failure ??= e.ToString();
        }

        if (loaded != null && !keep)
            DeleteLoaded(loaded);

        _res.UserData.WriteAllText(dir / "report.txt", report.ToText());
        _res.UserData.WriteAllText(dir / "report.json", report.ToJson());

        if (report.Failure != null)
            shell.WriteError(Loc.GetString("cmd-persistenceroundtrip-failed", ("reason", report.Failure.Split('\n')[0])));

        WriteSummary(shell, report, loaded, keep);
        shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-report", ("path", (dir / "report.txt").ToString())));
    }

    /// <summary>
    /// Runs the save, load and both snapshots, filling in the report. Returns the load result so the caller can
    /// delete the copy, or null if the load did not happen.
    /// </summary>
    private LoadResult? Run(EntityUid mapUid, ResPath savePath, RoundTripReport report)
    {
        var stopwatch = Stopwatch.StartNew();
        var sourceTree = _snapshot.CollectTree([mapUid]);
        report.CollectSeconds = stopwatch.Elapsed.TotalSeconds;

        // Everything the engine reports as serializable passes through this hook, including auto-included nullspace
        // entities. Prototypes with save: false are rejected before the hook is invoked.
        var serializable = new HashSet<EntityUid>();
        void OnIsSerializable(Entity<MetaDataComponent> ent, ref bool result) => serializable.Add(ent.Owner);

        var forced = new List<EntityPrototype>();
        if (report.ForceSave)
        {
            foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.MapSavable)
                    continue;

                proto.MapSavable = true;
                forced.Add(proto);
                report.ForcedPrototypes.Add(proto.ID);
            }
            report.ForcedPrototypes.Sort(StringComparer.Ordinal);
        }

        var options = SerializationOptions.Default with
        {
            Category = FileCategory.Map,
            // One broken entity should not abort the whole save; it is logged and shows up in the diff.
            EntityExceptionBehaviour = EntityExceptionBehaviour.IgnoreEntityAndChildren,
            LogAutoInclude = null,
        };

        MappingDataNode data;
        using (var capture = new LogCapture(_logManager.RootSawmill))
        {
            _mapLoader.OnIsSerializable += OnIsSerializable;
            try
            {
                stopwatch.Restart();
                (data, var category) = _mapLoader.SerializeEntitiesRecursive([mapUid], options);
                report.SerializeSeconds = stopwatch.Elapsed.TotalSeconds;

                if (category != FileCategory.Map)
                    report.Failure = $"Serialized category was {category}, expected {FileCategory.Map}";
            }
            finally
            {
                _mapLoader.OnIsSerializable -= OnIsSerializable;
                foreach (var proto in forced)
                {
                    proto.MapSavable = false;
                }

                report.SaveWarnings = capture.Warnings;
                report.SaveErrors = capture.Errors;
                report.SaveLogSawmills = new(capture.PerSawmill);
                report.SaveMessages = new(capture.Messages);
            }
        }

        // The live snapshot covers the map plus the nullspace entities the save pulled in (stations, minds, ...).
        var nullspaceRoots = serializable
            .Where(uid => !EntityManager.HasComponent<MapComponent>(uid)
                          && !EntityManager.GetComponent<TransformComponent>(uid).ParentUid.IsValid())
            .ToList();
        var sourceSet = new HashSet<EntityUid>(sourceTree);
        sourceSet.UnionWith(_snapshot.CollectTree(nullspaceRoots));

        foreach (var uid in nullspaceRoots)
        {
            Increment(report.IncludedNullspace, _snapshot.PrototypeKey(uid));
        }

        foreach (var uid in sourceSet)
        {
            if (serializable.Contains(uid))
                continue;

            var meta = EntityManager.GetComponent<MetaDataComponent>(uid);
            var key = _snapshot.PrototypeKey(uid);
            if (meta.EntityPrototype is { MapSavable: false })
                Increment(report.UnsaveableRoots, key);
            else
                Increment(report.DroppedChildren, key);
        }

        CountUncovered(report, sourceSet, mapUid);
        ReadFileContents(data, report);

        stopwatch.Restart();
        report.Before = _snapshot.Snapshot(sourceSet);
        report.SnapshotSeconds = stopwatch.Elapsed.TotalSeconds;

        if (report.Failure != null)
            return null;

        stopwatch.Restart();
        WriteYaml(savePath, data);
        report.WriteSeconds = stopwatch.Elapsed.TotalSeconds;

        using (var stream = _res.UserData.Open(savePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            report.FileBytes = stream.Length;
        }

        // Load into a new, paused map. Post-init entities are flagged in the file, so map-init (fills, spawners)
        // does not run again.
        var loadOptions = new MapLoadOptions
        {
            ExpectedCategory = FileCategory.Map,
            DeserializationOptions = DeserializationOptions.Default with { PauseMaps = true },
        };

        LoadResult? result;
        using (var capture = new LogCapture(_logManager.RootSawmill))
        {
            stopwatch.Restart();
            var ok = _mapLoader.TryLoadGeneric(savePath, out result, loadOptions);
            report.LoadSeconds = stopwatch.Elapsed.TotalSeconds;

            report.LoadWarnings = capture.Warnings;
            report.LoadErrors = capture.Errors;
            report.LoadLogSawmills = new(capture.PerSawmill);
            report.LoadMessages = new(capture.Messages);

            if (!ok || result == null)
            {
                report.Failure = "Loading the save file failed";
                return null;
            }
        }

        var loadedRoots = result.Maps.Select(m => m.Owner).Concat(result.NullspaceEntities);

        stopwatch.Restart();
        report.After = _snapshot.Snapshot(_snapshot.CollectTree(loadedRoots));
        report.SnapshotSeconds += stopwatch.Elapsed.TotalSeconds;

        report.Diffs = SectorSnapshot.Compare(report.Before, report.After);
        report.ChangedPapers = SectorSnapshot.ChangedPapers(report.Before, report.After);

        if (result.Maps.Count != 1)
            report.Failure = $"Save file contained {result.Maps.Count} maps, expected 1";

        return result;
    }

    /// <summary>
    /// Lists maps and nullspace entities that this save does not cover, so the report shows what a whole-sector save
    /// still has to include.
    /// </summary>
    private void CountUncovered(RoundTripReport report, HashSet<EntityUid> covered, EntityUid sourceMap)
    {
        var maps = EntityManager.EntityQueryEnumerator<MapComponent>();
        while (maps.MoveNext(out var uid, out _))
        {
            if (uid == sourceMap)
                continue;

            var name = $"{EntityManager.ToPrettyString(uid)}";
            report.OtherMaps[name] = _snapshot.CollectTree([uid]).Count;
        }

        var xforms = EntityManager.EntityQueryEnumerator<TransformComponent>();
        while (xforms.MoveNext(out var uid, out var xform))
        {
            if (xform.ParentUid.IsValid() || covered.Contains(uid) || EntityManager.HasComponent<MapComponent>(uid))
                continue;

            Increment(report.OtherNullspace, _snapshot.PrototypeKey(uid));
        }
    }

    /// <summary>
    /// Counts entities per prototype in the serialized data, and references that were written as "invalid".
    /// </summary>
    private static void ReadFileContents(MappingDataNode data, RoundTripReport report)
    {
        if (!data.TryGet("entities", out SequenceDataNode? protos))
            return;

        foreach (var node in protos)
        {
            if (node is not MappingDataNode protoNode || !protoNode.TryGet("entities", out SequenceDataNode? entities))
                continue;

            var proto = protoNode.TryGet("proto", out ValueDataNode? protoValue) && protoValue.Value != ""
                ? protoValue.Value
                : SectorSnapshot.NoPrototype;

            report.FileCounts[proto] = report.FileCounts.GetValueOrDefault(proto) + entities.Count;

            foreach (var entity in entities)
            {
                var invalid = CountInvalid(entity);
                if (invalid > 0)
                    report.InvalidReferences[proto] = report.InvalidReferences.GetValueOrDefault(proto) + invalid;
            }
        }
    }

    private static int CountInvalid(DataNode node)
    {
        switch (node)
        {
            case ValueDataNode value:
                return value.Value == "invalid" ? 1 : 0;
            case SequenceDataNode sequence:
                return sequence.Sum(CountInvalid);
            case MappingDataNode mapping:
                var count = 0;
                foreach (var (_, child) in mapping)
                {
                    count += CountInvalid(child);
                }
                return count;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Writes the data the same way <see cref="MapLoaderSystem"/> does, so serialization and file writing can be
    /// timed separately.
    /// </summary>
    private void WriteYaml(ResPath path, MappingDataNode data)
    {
        var document = new YamlDocument(data.ToYaml());
        using var writer = _res.UserData.OpenWriteText(path);
        var stream = new YamlStream { document };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);
    }

    private void DeleteLoaded(LoadResult result)
    {
        foreach (var map in result.Maps)
        {
            EntityManager.DeleteEntity(map.Owner);
        }

        foreach (var uid in result.NullspaceEntities)
        {
            if (!EntityManager.Deleted(uid))
                EntityManager.DeleteEntity(uid);
        }
    }

    private void WriteSummary(IConsoleShell shell, RoundTripReport report, LoadResult? loaded, bool keep)
    {
        var before = report.Before?.EntityCount ?? 0;
        var file = (int) report.FileCounts.Values.Sum();
        var after = report.After?.EntityCount ?? 0;

        shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-summary-entities",
            ("live", before), ("file", file), ("loaded", after)));
        shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-summary-timings",
            ("serialize", report.SerializeSeconds.ToString("F2")),
            ("write", report.WriteSeconds.ToString("F2")),
            ("load", report.LoadSeconds.ToString("F2")),
            ("size", (report.FileBytes / 1024.0 / 1024.0).ToString("F1"))));
        shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-summary-diffs",
            ("values", report.Diffs.Count(d => d.Metric != "entities")),
            ("papers", report.ChangedPapers),
            ("unsaveable", report.UnsaveableRoots.Values.Sum()),
            ("dropped", report.DroppedChildren.Values.Sum())));
        shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-summary-log",
            ("saveErrors", report.SaveErrors), ("loadErrors", report.LoadErrors)));

        if (loaded != null && keep)
        {
            var map = loaded.Maps.FirstOrDefault();
            shell.WriteLine(Loc.GetString("cmd-persistenceroundtrip-kept",
                ("map", map.Comp?.MapId.ToString() ?? "?")));
        }
    }

    private static void Increment(Dictionary<string, int> dict, string key)
    {
        dict[key] = dict.GetValueOrDefault(key) + 1;
    }
}
