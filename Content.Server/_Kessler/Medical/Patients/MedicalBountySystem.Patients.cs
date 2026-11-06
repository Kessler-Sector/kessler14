using Content.Server._Kessler.Medical.Patients;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Medical;
using Content.Shared.Database;

namespace Content.Server._NF.Medical;

/// <summary>
/// Kessler: living NPC patients are discharged at the medical bounty redemption machine (docs/kessler/design/
/// medical-science.md, M1). Kept apart from the Frontier file so the upstream edit stays small.
/// </summary>
public sealed partial class MedicalBountySystem
{
    [Dependency] private readonly PatientSystem _patients = default!;

    private void RedeemPatient(EntityUid uid, MedicalBountyRedemptionComponent component, RedeemMedicalBountyMessage ev,
        Entity<KesslerPatientComponent> patient)
    {
        if (_patients.DischargeProblem(patient) is { } problem)
        {
            _popup.PopupEntity(Loc.GetString(problem), uid);
            _audio.PlayPvs(component.DenySound, uid);
            return;
        }

        var payout = _patients.Payout(patient);
        var successString = "medical-bounty-redemption-success";
        if (TryComp<MedicalBountyBankPaymentComponent>(ev.Actor, out var bankPayment))
        {
            successString = "medical-bounty-redemption-success-to-station";
            _bank.TrySectorDeposit(bankPayment.Account, payout, LedgerEntryType.MedicalBountyTax);
        }
        else if (payout > 0)
        {
            var stackUid = _stack.Spawn(payout, "Credit", Transform(uid).Coordinates);
            if (!_hands.TryPickupAnyHand(ev.Actor, stackUid))
                _transform.SetLocalRotation(stackUid, Angle.Zero);
        }

        foreach (var (account, taxCoeff) in component.TaxAccounts)
        {
            _bank.TrySectorDeposit(account, (int) (payout * taxCoeff), LedgerEntryType.MedicalBountyTax);
        }

        _adminLog.Add(LogType.MedicalBountyRedeemed, LogImpact.Low,
            $"{ToPrettyString(ev.Actor):actor} discharged the patient {ToPrettyString(patient):subject} for {payout}.");

        _patients.Discharged(patient, ev.Actor);
        QueueDel(patient);

        _popup.PopupEntity(Loc.GetString(successString), uid);
        _audio.PlayPvs(component.RedeemSound, uid);
        UpdateUserInterface(uid, component);
    }

    private MedicalBountyRedemptionUIState GetPatientState(Entity<KesslerPatientComponent> patient, bool paidToStation)
    {
        var status = _patients.DischargeProblem(patient) switch
        {
            null => MedicalBountyRedemptionStatus.Valid,
            "kessler-patient-discharge-not-cured" => MedicalBountyRedemptionStatus.NotCured,
            "medical-bounty-redemption-fail-not-alive" => MedicalBountyRedemptionStatus.NotAlive,
            _ => MedicalBountyRedemptionStatus.TooDamaged,
        };

        var payout = status == MedicalBountyRedemptionStatus.Valid ? _patients.Payout(patient) : 0;
        return new MedicalBountyRedemptionUIState(status, payout, paidToStation);
    }
}
