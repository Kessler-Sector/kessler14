using Content.Server.Administration;
using Content.Server.Station.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Lists the ships in storage.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class StoredShipsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ShipStorageSystem _storage = default!;

    public override string Command => "storedships";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var ships = _storage.GetStoredShips();
        foreach (var ship in ships)
        {
            var storedAt = ship.StoredAt;
            shell.WriteLine(Loc.GetString("cmd-storedships-line",
                ("id", ship.Id.ToString()),
                ("ship", ship.ShipName),
                ("owner", ship.OwnerName),
                ("reason", ship.Reason.ToString()),
                ("debt", ship.Debt),
                ("date", storedAt.ToString("yyyy-MM-dd HH:mm"))));
        }

        shell.WriteLine(Loc.GetString("cmd-storedships-total", ("count", ships.Count)));
    }
}

/// <summary>
/// Moves an owned ship into storage now.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ShipStoreCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ShipStorageSystem _storage = default!;

    public override string Command => "shipstore";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !NetEntity.TryParse(args[0], out var net) || !EntityManager.TryGetEntity(net, out var ship))
        {
            shell.WriteError(Loc.GetString("cmd-shipstore-help", ("command", Command)));
            return;
        }

        if (_storage.TryStoreShip(ship.Value, ShipStorageReason.Admin, DateTime.UtcNow, out var problem))
            shell.WriteLine(Loc.GetString("cmd-shipstore-done"));
        else
            shell.WriteError(Loc.GetString("cmd-shipstore-failed", ("problem", problem)));
    }
}

/// <summary>
/// Brings a stored ship back, docked at the station the admin is on (or left on the shipyard map).
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ShipRecallCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ShipStorageSystem _storage = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override string Command => "shiprecall";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !Guid.TryParse(args[0], out var id))
        {
            shell.WriteError(Loc.GetString("cmd-shiprecall-help", ("command", Command)));
            return;
        }

        var station = shell.Player?.AttachedEntity is { } admin ? _station.GetOwningStation(admin) : null;
        if (_storage.TryRecallShip(id, station, out var ship, out var problem))
            shell.WriteLine(Loc.GetString("cmd-shiprecall-done", ("ship", ship.Value.ToString())));
        else
            shell.WriteError(Loc.GetString("cmd-shiprecall-failed", ("problem", problem)));
    }
}
