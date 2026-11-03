using Serilog.Events;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Temporarily records warnings and errors logged on a sawmill and its children, so a report can list what the
/// engine complained about during a save or load.
/// </summary>
public sealed class LogCapture : ILogHandler, IDisposable
{
    private const int MaxStoredMessages = 50;

    private readonly ISawmill _sawmill;
    private readonly object _lock = new();

    public int Warnings { get; private set; }
    public int Errors { get; private set; }

    /// <summary>
    /// Message counts per sawmill, for warnings and above.
    /// </summary>
    public readonly Dictionary<string, int> PerSawmill = new();

    public readonly List<string> Messages = new();

    public LogCapture(ISawmill sawmill)
    {
        _sawmill = sawmill;
        _sawmill.AddHandler(this);
    }

    public void Log(string sawmillName, LogEvent message)
    {
        if (message.Level < LogEventLevel.Warning)
            return;

        lock (_lock)
        {
            if (message.Level == LogEventLevel.Warning)
                Warnings++;
            else
                Errors++;

            PerSawmill[sawmillName] = PerSawmill.GetValueOrDefault(sawmillName) + 1;

            if (Messages.Count < MaxStoredMessages)
                Messages.Add($"[{message.Level}] {sawmillName}: {message.RenderMessage()}");
        }
    }

    public void Dispose()
    {
        _sawmill.RemoveHandler(this);
    }
}
