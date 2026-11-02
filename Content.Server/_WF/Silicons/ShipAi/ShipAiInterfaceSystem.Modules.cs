using Content.Server.Medical;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed partial class ShipAiInterfaceSystem
{
    [Dependency] private readonly HealthAnalyzerSystem _healthAnalyzer = default!;

    public const string ModuleSlot = "ship_ai_module";

    private void InitializeModules()
    {
        SubscribeLocalEvent<ShipAiInterfaceComponent, ItemSlotInsertAttemptEvent>(OnSlotInsertAttempt);
        SubscribeLocalEvent<ShipAiInterfaceComponent, ItemSlotEjectAttemptEvent>(OnModuleEjectAttempt);
        SubscribeLocalEvent<ShipAiComponent, ShipAiCameraScanEvent>(OnCameraScan);

        Subs.BuiEvents<ShipAiInterfaceComponent>(HealthAnalyzerUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnScanClosed);
        });
    }

    private void OnSlotInsertAttempt(Entity<ShipAiInterfaceComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (ModuleLocked(ent, args.Slot) ||
            args.Slot.ID == StationAiHolderComponent.Container && WrongFaction(ent, args.Item))
            args.Cancelled = true;
    }

    private void OnModuleEjectAttempt(Entity<ShipAiInterfaceComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (ModuleLocked(ent, args.Slot))
            args.Cancelled = true;
    }

    private bool ModuleLocked(EntityUid core, ItemSlot slot)
    {
        return slot.ID == ModuleSlot && !_wires.IsPanelOpen(core);
    }

    private void SetModules(EntityUid core, EntityUid brain, bool apply)
    {
        if (!_containers.TryGetContainer(core, ModuleSlot, out var slot))
            return;

        foreach (var module in slot.ContainedEntities)
        {
            if (apply)
                ApplyModule(module, brain);
            else
                RemoveModule(module, brain);
        }
    }

    private void ApplyModule(EntityUid module, EntityUid brain)
    {
        if (!TryComp<ShipAiModuleComponent>(module, out var comp) || !comp.Enabled)
            return;

        EntityManager.AddComponents(brain, comp.Components, removeExisting: false);
        comp.GrantedActions.Clear();
        foreach (var action in comp.Actions)
        {
            if (_actions.AddAction(brain, action) is { } granted)
                comp.GrantedActions.Add(granted);
        }
    }

    private void RemoveModule(EntityUid module, EntityUid brain)
    {
        if (!TryComp<ShipAiModuleComponent>(module, out var comp) || !comp.Enabled)
            return;

        EntityManager.RemoveComponents(brain, comp.Components);
        foreach (var action in comp.GrantedActions)
        {
            _actions.RemoveAction(brain, action);
            QueueDel(action);
        }

        comp.GrantedActions.Clear();
    }

    private void OnCameraScan(Entity<ShipAiComponent> ent, ref ShipAiCameraScanEvent args)
    {
        if (args.Handled ||
            !HasComp<MobStateComponent>(args.Target) ||
            !TryGetInterface(ent, out var iface) ||
            !_shipAi.OnOwnShip(ent, args.Target))
            return;

        args.Handled = true;
        var scan = EnsureComp<ShipAiScanComponent>(iface);
        scan.Patient = args.Target;
        scan.NextUpdate = _timing.CurTime;
        _ui.OpenUi(iface.Owner, HealthAnalyzerUiKey.Key, ent.Owner);
    }

    private void OnScanClosed(Entity<ShipAiInterfaceComponent> ent, ref BoundUIClosedEvent args)
    {
        RemCompDeferred<ShipAiScanComponent>(ent);
    }

    private void StopScan(EntityUid core)
    {
        if (!HasComp<ShipAiScanComponent>(core))
            return;

        RemCompDeferred<ShipAiScanComponent>(core);
        _ui.CloseUi(core, HealthAnalyzerUiKey.Key);
    }

    private void UpdateScans()
    {
        var query = EntityQueryEnumerator<ShipAiScanComponent, ShipAiInterfaceComponent>();
        while (query.MoveNext(out var uid, out var scan, out var iface))
        {
            if (_timing.CurTime < scan.NextUpdate)
                continue;

            scan.NextUpdate = _timing.CurTime + iface.ScanInterval;
            if (TerminatingOrDeleted(scan.Patient) ||
                !TryGetBrain(uid, out var brain) ||
                !_shipAi.OnOwnShip(brain, scan.Patient))
            {
                StopScan(uid);
                continue;
            }

            _healthAnalyzer.UpdateScannedUser(uid, scan.Patient, true);
        }
    }
}
