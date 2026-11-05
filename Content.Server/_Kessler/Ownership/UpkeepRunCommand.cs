using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Bills ship upkeep now instead of waiting for the next check. An optional number of hours pretends that much time has
/// passed, so billing and debt can be tried without waiting days.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed class UpkeepRunCommand : LocalizedEntityCommands
{
    [Dependency] private readonly UpkeepSystem _upkeep = default!;

    public override string Command => "upkeeprun";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var hours = 0.0;
        if (args.Length > 0 && !double.TryParse(args[0], out hours))
        {
            shell.WriteError(Loc.GetString("cmd-upkeeprun-bad-hours"));
            return;
        }

        var now = DateTime.UtcNow + TimeSpan.FromHours(hours);
        _ = _upkeep.RunBilling(now);
        shell.WriteLine(Loc.GetString("cmd-upkeeprun-done", ("time", now.ToString("yyyy-MM-dd HH:mm"))));
    }
}
