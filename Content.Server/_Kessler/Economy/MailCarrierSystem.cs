using Content.Server._NF.Bank;
using Content.Server._DV.Mail.Components;
using Content.Server.Popups;
using Content.Shared._Kessler.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Hands;
using Robust.Shared.Configuration;

namespace Content.Server._Kessler.Economy;

/// <summary>
/// Pays mail carriers. Frontier paid each opened parcel's value into the Frontier sector account only, so carrying
/// mail earned nothing unless a station representative paid staff by hand.
/// </summary>
public sealed class MailCarrierSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MailComponent, GotEquippedHandEvent>(OnPickedUp);
    }

    private void OnPickedUp(Entity<MailComponent> ent, ref GotEquippedHandEvent args)
    {
        // Only someone who can be paid, and not the recipient picking up their own mail.
        if (!ent.Comp.IsProfitable
            || !HasComp<BankAccountComponent>(args.User)
            || Name(args.User) == ent.Comp.Recipient)
        {
            return;
        }

        EnsureComp<MailCarrierComponent>(ent).Carrier = args.User;
    }

    /// <summary>
    /// Pays the parcel's carrier their share of <paramref name="bounty"/>, if they can be paid, and returns how much
    /// was paid. The rest is for the caller to deposit as before.
    /// </summary>
    public int PayCarrier(EntityUid mail, int bounty, EntityUid opener)
    {
        if (!TryComp<MailCarrierComponent>(mail, out var comp)
            || comp.Carrier is not { } carrier
            || carrier == opener
            || TerminatingOrDeleted(carrier))
        {
            return 0;
        }

        var share = (int) MathF.Round(bounty * _cfg.GetCVar(KesslerCVars.MailCarrierShare));
        if (share <= 0 || share > bounty)
            return 0;

        // Offline carriers can't be paid through the bank here; their share stays with the station.
        if (!_bank.TryBankDeposit(carrier, share))
            return 0;

        _popup.PopupEntity(Loc.GetString("kessler-mail-carrier-paid",
                ("amount", share),
                ("recipient", Name(opener))),
            carrier,
            carrier);
        return share;
    }
}
