using System.Diagnostics;
using System.Linq;
using Content.Shared._Kessler.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Server._Kessler.Diagnostics;

/// <summary>
/// Measures how long the entity system update takes each tick: <see cref="TickTimeStartSystem"/> updates before every
/// other system and <see cref="TickTimeSystem"/> after every other system. This covers game logic (atmos, power,
/// physics, NPCs, ...) but not networking or sending game state, which run outside the entity systems.
/// </summary>
public sealed class TickTimeSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntitySystemManager _systems = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>
    /// How many recent ticks are kept for statistics.
    /// </summary>
    private const int Capacity = 9000;

    private readonly double[] _durations = new double[Capacity];
    private readonly double[] _periods = new double[Capacity];
    private int _count;
    private int _next;

    private long _startTimestamp;
    private long _previousStart;
    private TimeSpan _nextLog;

    public override void Initialize()
    {
        base.Initialize();

        foreach (var type in _systems.GetEntitySystemTypes())
        {
            if (type != typeof(TickTimeSystem) && type != typeof(TickTimeStartSystem))
                UpdatesAfter.Add(type);
        }
    }

    /// <summary>
    /// Called by <see cref="TickTimeStartSystem"/> at the start of the entity system update.
    /// </summary>
    public void TickStarted()
    {
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_startTimestamp == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        _durations[_next] = Stopwatch.GetElapsedTime(_startTimestamp, now).TotalMilliseconds;
        _periods[_next] = _previousStart == 0 ? 0 : Stopwatch.GetElapsedTime(_previousStart, _startTimestamp).TotalMilliseconds;
        _previousStart = _startTimestamp;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);

        var seconds = _cfg.GetCVar(KesslerCVars.TickLogSeconds);
        if (seconds <= 0)
            return;

        if (_timing.RealTime < _nextLog)
            return;

        if (_nextLog != TimeSpan.Zero)
            Log.Info(Format(GetStats((int) (seconds * _timing.TickRate))));
        _nextLog = _timing.RealTime + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Statistics over the most recent <paramref name="ticks"/> ticks.
    /// </summary>
    public TickStats GetStats(int ticks)
    {
        var n = Math.Clamp(ticks, 1, Math.Max(1, _count));
        var durations = new double[n];
        var periods = new double[n];
        for (var i = 0; i < n; i++)
        {
            var index = (_next - 1 - i + Capacity) % Capacity;
            durations[i] = _durations[index];
            periods[i] = _periods[index];
        }

        Array.Sort(durations);
        var budget = 1000.0 / _timing.TickRate;
        using var process = Process.GetCurrentProcess();
        return new TickStats(
            n,
            durations.Average(),
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            Percentile(durations, 0.99),
            durations[^1],
            periods.Where(p => p > 0).DefaultIfEmpty(0).Average(),
            durations.Count(d => d > budget),
            process.WorkingSet64 / 1e6,
            GC.GetTotalMemory(false) / 1e6,
            GC.CollectionCount(2));
    }

    public static string Format(TickStats s)
    {
        return $"Tick stats over {s.Ticks} ticks: entity systems mean {s.Mean:F2} ms, p50 {s.P50:F2}, p95 {s.P95:F2}, " +
               $"p99 {s.P99:F2}, max {s.Max:F2}; tick period {s.Period:F2} ms; {s.OverBudget} over budget; " +
               $"working set {s.WorkingSetMb:F0} MB, managed heap {s.ManagedMb:F0} MB, gen2 GCs {s.Gen2Collections}";
    }

    private static double Percentile(double[] sorted, double p)
    {
        return sorted[(int) Math.Min(sorted.Length - 1, Math.Floor(p * sorted.Length))];
    }

    public readonly record struct TickStats(
        int Ticks,
        double Mean,
        double P50,
        double P95,
        double P99,
        double Max,
        double Period,
        int OverBudget,
        double WorkingSetMb,
        double ManagedMb,
        int Gen2Collections);
}

/// <summary>
/// Updates before every other entity system and marks the start of the tick for <see cref="TickTimeSystem"/>.
/// </summary>
public sealed class TickTimeStartSystem : EntitySystem
{
    [Dependency] private readonly IEntitySystemManager _systems = default!;
    [Dependency] private readonly TickTimeSystem _tickTime = default!;

    public override void Initialize()
    {
        base.Initialize();

        foreach (var type in _systems.GetEntitySystemTypes())
        {
            if (type != typeof(TickTimeStartSystem))
                UpdatesBefore.Add(type);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _tickTime.TickStarted();
    }
}
