using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Saves the persistent sector now, the same way round end and shutdown do.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed class SectorSaveCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PersistentSectorSystem _persistentSector = default!;

    public override string Command => "sectorsave";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_persistentSector.Enabled)
        {
            shell.WriteError(Loc.GetString("cmd-sectorsave-disabled"));
            return;
        }

        if (_persistentSector.TrySaveSector("admin command"))
            shell.WriteLine(Loc.GetString("cmd-sectorsave-success"));
        else
            shell.WriteError(Loc.GetString("cmd-sectorsave-failed"));
    }
}
