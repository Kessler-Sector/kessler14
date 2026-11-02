using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Result of a <c>persistenceroundtrip</c> run. Written as readable text and as JSON.
/// </summary>
public sealed class RoundTripReport
{
    private const int MaxListedRows = 200;

    public DateTime StartedUtc;
    public string SourceMap = "";
    public bool ForceSave;
    public List<string> ForcedPrototypes = new();
    public string SavePath = "";
    public string? Failure;

    public double CollectSeconds;
    public double SerializeSeconds;
    public double WriteSeconds;
    public double LoadSeconds;
    public double SnapshotSeconds;
    public long FileBytes;

    /// <summary>
    /// Entities per prototype as written to the save file.
    /// </summary>
    public Dictionary<string, double> FileCounts = new();

    /// <summary>
    /// References written as "invalid" in the save file, per prototype of the entity holding the reference.
    /// </summary>
    public Dictionary<string, int> InvalidReferences = new();

    /// <summary>
    /// Entities that were not serialized because their own prototype has <c>save: false</c>, per prototype.
    /// </summary>
    public Dictionary<string, int> UnsaveableRoots = new();

    /// <summary>
    /// Entities that were not serialized because they are inside an unsaveable entity, per prototype.
    /// </summary>
    public Dictionary<string, int> DroppedChildren = new();

    /// <summary>
    /// Auto-included nullspace entities (stations, minds, ...) per prototype.
    /// </summary>
    public Dictionary<string, int> IncludedNullspace = new();

    /// <summary>
    /// Maps that were not part of the save, with their entity counts.
    /// </summary>
    public Dictionary<string, int> OtherMaps = new();

    /// <summary>
    /// Nullspace entities that were not part of the save, per prototype.
    /// </summary>
    public Dictionary<string, int> OtherNullspace = new();

    public SectorSnapshot? Before;
    public SectorSnapshot? After;
    public List<MetricDiff> Diffs = new();
    public int ChangedPapers;

    public int SaveWarnings;
    public int SaveErrors;
    public int LoadWarnings;
    public int LoadErrors;
    public Dictionary<string, int> SaveLogSawmills = new();
    public Dictionary<string, int> LoadLogSawmills = new();
    public List<string> SaveMessages = new();
    public List<string> LoadMessages = new();

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
        });
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kessler persistence round trip");
        sb.AppendLine($"Started:      {StartedUtc:u}");
        sb.AppendLine($"Source map:   {SourceMap}");
        sb.AppendLine($"Save file:    {SavePath} ({FileBytes / 1024.0 / 1024.0:F1} MiB)");
        sb.AppendLine($"Force save:   {ForceSave}{(ForceSave ? $" ({ForcedPrototypes.Count} prototypes switched to save: true)" : "")}");
        if (Failure != null)
            sb.AppendLine($"FAILED:       {Failure}");
        sb.AppendLine();

        sb.AppendLine("== Timings (seconds)");
        sb.AppendLine($"collect tree  {CollectSeconds,8:F2}");
        sb.AppendLine($"serialize     {SerializeSeconds,8:F2}");
        sb.AppendLine($"write yaml    {WriteSeconds,8:F2}");
        sb.AppendLine($"load          {LoadSeconds,8:F2}");
        sb.AppendLine($"snapshots     {SnapshotSeconds,8:F2}");
        sb.AppendLine();

        if (Before != null)
        {
            var before = Before.Totals();
            var after = After?.Totals() ?? new Dictionary<string, double>();
            sb.AppendLine("== Totals            live -> file -> loaded");
            foreach (var (key, value) in before)
            {
                var file = key == "entities" ? FileCounts.Values.Sum().ToString("0", CultureInfo.InvariantCulture) : "-";
                var loaded = after.TryGetValue(key, out var v) ? Format(v) : "-";
                var mark = after.TryGetValue(key, out var v2) && !SectorSnapshot.NearlyEqual(value, v2) ? "  <-- changed" : "";
                sb.AppendLine($"{key,-20} {Format(value)} -> {file} -> {loaded}{mark}");
            }

            if (After != null)
                sb.AppendLine($"{"papers changed",-20} {ChangedPapers}");
            sb.AppendLine();
        }

        if (Before != null)
        {
            sb.AppendLine("== Entity counts that differ, per prototype (live -> file -> loaded)");
            var rows = Before.PrototypeCounts.Keys
                .Union(FileCounts.Keys)
                .Union(After?.PrototypeCounts.Keys ?? Enumerable.Empty<string>())
                .Select(p => (Proto: p,
                    Live: Before.PrototypeCounts.GetValueOrDefault(p),
                    File: FileCounts.GetValueOrDefault(p),
                    Loaded: After?.PrototypeCounts.GetValueOrDefault(p) ?? 0))
                .Where(r => r.Live != r.File || r.File != r.Loaded)
                .OrderByDescending(r => Math.Abs(r.Live - r.Loaded))
                .ThenBy(r => r.Proto, StringComparer.Ordinal)
                .ToList();

            sb.AppendLine($"{rows.Count} prototypes differ");
            foreach (var r in rows.Take(MaxListedRows))
            {
                sb.AppendLine($"  {r.Proto,-50} {r.Live,7} -> {r.File,7} -> {r.Loaded,7}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("== Value differences (live -> loaded), excluding entity counts");
        var valueDiffs = Diffs.Where(d => d.Metric != "entities").ToList();
        sb.AppendLine($"{valueDiffs.Count} differences");
        foreach (var d in valueDiffs.Take(MaxListedRows))
        {
            sb.AppendLine($"  {d.Metric,-20} {d.Key,-45} {Format(d.Before)} -> {Format(d.After)}");
        }
        sb.AppendLine();

        AppendCounts(sb, "Not saved: prototype has save: false (root entities)", UnsaveableRoots);
        AppendCounts(sb, "Not saved: inside an unsaveable entity", DroppedChildren);
        AppendCounts(sb, "Holders of references written as 'invalid'", InvalidReferences);
        AppendCounts(sb, "Nullspace entities auto-included in the save", IncludedNullspace);
        AppendCounts(sb, "Maps NOT covered by this save (entity counts)", OtherMaps);
        AppendCounts(sb, "Nullspace entities NOT covered by this save", OtherNullspace);

        AppendLog(sb, "Save", SaveWarnings, SaveErrors, SaveLogSawmills, SaveMessages);
        AppendLog(sb, "Load", LoadWarnings, LoadErrors, LoadLogSawmills, LoadMessages);

        if (ForcedPrototypes.Count > 0)
        {
            sb.AppendLine("== Prototypes temporarily switched to save: true");
            foreach (var proto in ForcedPrototypes)
            {
                sb.AppendLine($"  {proto}");
            }
        }

        return sb.ToString();
    }

    private static void AppendCounts(StringBuilder sb, string title, Dictionary<string, int> counts)
    {
        sb.AppendLine($"== {title}: {counts.Values.Sum()} total");
        foreach (var (key, count) in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(MaxListedRows))
        {
            sb.AppendLine($"  {key,-50} {count,7}");
        }
        sb.AppendLine();
    }

    private static void AppendLog(StringBuilder sb, string phase, int warnings, int errors, Dictionary<string, int> sawmills, List<string> messages)
    {
        sb.AppendLine($"== {phase} log: {warnings} warnings, {errors} errors");
        foreach (var (sawmill, count) in sawmills.OrderByDescending(x => x.Value))
        {
            sb.AppendLine($"  {sawmill,-40} {count,7}");
        }
        sb.AppendLine($"  First {messages.Count} messages:");
        foreach (var message in messages)
        {
            var text = message.Length > 2000 ? message[..2000] + " [...]" : message;
            sb.AppendLine($"    {text.ReplaceLineEndings("\n      ")}");
        }
        sb.AppendLine();
    }

    private static string Format(double value)
    {
        return value.ToString(Math.Abs(value % 1) < 1e-9 ? "0" : "0.##", CultureInfo.InvariantCulture);
    }
}
