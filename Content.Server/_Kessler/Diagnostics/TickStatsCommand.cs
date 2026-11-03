using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._Kessler.Diagnostics;

/// <summary>
/// Prints entity system tick time statistics over the last few seconds.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class TickStatsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TickTimeSystem _tickTime = default!;

    public override string Command => "tickstats";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var seconds = 60;
        if (args.Length > 0 && !int.TryParse(args[0], out seconds))
        {
            shell.WriteError(Help);
            return;
        }

        shell.WriteLine(TickTimeSystem.Format(_tickTime.GetStats(seconds * _timing.TickRate)));
    }
}
