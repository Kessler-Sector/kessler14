using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Lists every owned ship with its owner, price and whether a voucher paid for it.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ShipOwnersCommand : LocalizedEntityCommands
{
    public override string Command => "shipowners";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<ShipOwnershipComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var ownership, out var meta))
        {
            count++;
            var purchasedAt = ownership.PurchasedAt;
            var nextUpkeep = ownership.NextUpkeep;
            shell.WriteLine(Loc.GetString("cmd-shipowners-line",
                ("ship", meta.EntityName),
                ("uid", uid.ToString()),
                ("owner", ownership.OwnerName),
                ("profile", ownership.OwnerProfileId?.ToString() ?? "?"),
                ("price", ownership.PurchasePrice),
                ("voucher", ownership.PurchasedWithVoucher ? "yes" : "no"),
                ("date", purchasedAt.ToString("yyyy-MM-dd HH:mm")),
                ("next", nextUpkeep.ToString("yyyy-MM-dd HH:mm")),
                ("debt", ownership.UpkeepDebt),
                ("impound", ownership.ImpoundDue ? Loc.GetString("cmd-shipowners-impound") : "")));
        }

        shell.WriteLine(Loc.GetString("cmd-shipowners-total", ("count", count)));
    }
}
