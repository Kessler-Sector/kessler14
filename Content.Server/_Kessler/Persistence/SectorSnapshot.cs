using System.Linq;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Aggregate values describing a set of entities, used to compare a sector before and after a save/load round trip.
/// Entities get new uids when loaded, so the comparison works on totals keyed by prototype rather than on individual
/// entities.
/// </summary>
public sealed class SectorSnapshot
{
    public const string NoPrototype = "<none>";

    public int EntityCount;
    public int MapCount;
    public int GridCount;
    public int NullspaceCount;
    public int ContainedCount;
    public long TileCount;

    /// <summary>
    /// Entity count per prototype id.
    /// </summary>
    public Dictionary<string, double> PrototypeCounts = new();

    /// <summary>
    /// Battery charge per battery prototype.
    /// </summary>
    public Dictionary<string, double> BatteryCharge = new();

    public Dictionary<string, double> BatteryMaxCharge = new();

    /// <summary>
    /// Solution volume, keyed by the prototype of the entity that owns the solution.
    /// </summary>
    public Dictionary<string, double> SolutionVolume = new();

    /// <summary>
    /// Total quantity per reagent across all solutions.
    /// </summary>
    public Dictionary<string, double> ReagentQuantity = new();

    /// <summary>
    /// Total stack count per stack type. Cash is the "Credit" stack type.
    /// </summary>
    public Dictionary<string, double> StackCounts = new();

    /// <summary>
    /// Total damage per prototype, for damaged entities only.
    /// </summary>
    public Dictionary<string, double> Damage = new();

    public int PaperCount;

    /// <summary>
    /// Multiset of paper hashes (content, stamps, stamp state), so papers can be compared without matching uids.
    /// </summary>
    public Dictionary<string, int> PaperHashes = new();

    /// <summary>
    /// The metrics that are compared by <see cref="Compare"/>, by name.
    /// </summary>
    public IEnumerable<(string Name, Dictionary<string, double> Values)> Metrics()
    {
        yield return ("entities", PrototypeCounts);
        yield return ("battery-charge", BatteryCharge);
        yield return ("battery-max-charge", BatteryMaxCharge);
        yield return ("solution-volume", SolutionVolume);
        yield return ("reagent-quantity", ReagentQuantity);
        yield return ("stack-count", StackCounts);
        yield return ("damage", Damage);
    }

    public Dictionary<string, double> Totals()
    {
        var totals = new Dictionary<string, double>
        {
            ["entities"] = EntityCount,
            ["maps"] = MapCount,
            ["grids"] = GridCount,
            ["nullspace"] = NullspaceCount,
            ["contained"] = ContainedCount,
            ["tiles"] = TileCount,
            ["papers"] = PaperCount,
        };

        foreach (var (name, values) in Metrics())
        {
            if (name != "entities")
                totals[name] = values.Values.Sum();
        }

        totals["cash"] = StackCounts.GetValueOrDefault("Credit");
        return totals;
    }

    /// <summary>
    /// Lists every key whose value differs between two snapshots, per metric.
    /// </summary>
    public static List<MetricDiff> Compare(SectorSnapshot before, SectorSnapshot after)
    {
        var diffs = new List<MetricDiff>();

        foreach (var ((name, a), (_, b)) in before.Metrics().Zip(after.Metrics()))
        {
            foreach (var key in a.Keys.Union(b.Keys))
            {
                var va = a.GetValueOrDefault(key);
                var vb = b.GetValueOrDefault(key);
                if (!NearlyEqual(va, vb))
                    diffs.Add(new MetricDiff(name, key, va, vb));
            }
        }

        return diffs
            .OrderBy(d => d.Metric, StringComparer.Ordinal)
            .ThenByDescending(d => Math.Abs(d.Delta))
            .ToList();
    }

    /// <summary>
    /// Number of papers in <paramref name="before"/> that have no identical paper in <paramref name="after"/>.
    /// </summary>
    public static int ChangedPapers(SectorSnapshot before, SectorSnapshot after)
    {
        var changed = 0;
        foreach (var (hash, count) in before.PaperHashes)
        {
            changed += Math.Max(0, count - after.PaperHashes.GetValueOrDefault(hash));
        }

        return changed;
    }

    public static bool NearlyEqual(double a, double b)
    {
        var diff = Math.Abs(a - b);
        return diff <= 0.01 || diff <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-6;
    }
}

public readonly record struct MetricDiff(string Metric, string Key, double Before, double After)
{
    public double Delta => After - Before;
}
