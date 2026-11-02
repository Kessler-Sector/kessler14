using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Damage;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server.Destructible;
using Content.Shared._NF.Shipyard.Components;
using System.Numerics;
using Content.Server.StationEvents.Components;
using Robust.Shared.Map;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed partial class ShipAiInterfaceSystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly List<Entity<ShipAiInterfaceComponent>> _booted = new();
    private readonly HashSet<EntityUid> _saleChecks = new();
    private static readonly Dictionary<ProtoId<StationAiCustomizationGroupPrototype>, ProtoId<StationAiCustomizationPrototype>> NoLooks = new();
    private static readonly EntProtoId SaleBlocker = "ShipAiSaleBlocker";

    private void InitializeVisuals()
    {
        SubscribeLocalEvent<ShipAiInterfaceComponent, DamageChangedEvent>(OnDamageChanged, before: [typeof(DestructibleSystem)]);
        SubscribeLocalEvent<ShipAiInterfaceComponent, EntParentChangedMessage>(OnCoreParentChanged);
    }

    private void OnDamageChanged(Entity<ShipAiInterfaceComponent> ent, ref DamageChangedEvent args)
    {
        UpdateLooks(ent);
        UpdateCoreAlert(ent, args);
    }

    private void StartBoot(Entity<ShipAiInterfaceComponent> ent)
    {
        EnsureComp<ShipAiBootingComponent>(ent).End = _timing.CurTime + ent.Comp.BootTime;
        UpdateCoreAlert(ent, null);
        _audio.PlayPvs(ent.Comp.BootSound, ent);
        UpdateLooks(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateScans();
        UpdateRemoteRange();
        UpdateLost();

        foreach (var uid in _saleChecks)
        {
            if (!TryComp<ShipyardSellConditionComponent>(uid, out var sale) ||
                !TryComp<ShipAiInterfaceComponent>(uid, out var iface))
                continue;

            sale.BlockSale = TryGetBrain(uid, out var brain) &&
                             (HasAi(brain) || CompOrNull<ShipAiCoreComponent>(brain)?.Occupied == true);
            SyncSplitBlocker((uid, iface), sale.BlockSale);
        }
        _saleChecks.Clear();

        _booted.Clear();
        var query = EntityQueryEnumerator<ShipAiBootingComponent, ShipAiInterfaceComponent>();
        while (query.MoveNext(out var uid, out var boot, out var iface))
        {
            if (_timing.CurTime >= boot.End)
                _booted.Add((uid, iface));
        }

        foreach (var core in _booted)
        {
            RemComp<ShipAiBootingComponent>(core);
            UpdateLooks(core);
        }
    }

    private void SyncSplitBlocker(Entity<ShipAiInterfaceComponent> ent, bool block)
    {
        EntityUid? deedGrid = block && !TerminatingOrDeleted(ent) &&
                              TryComp<LinkedLifecycleGridChildComponent>(Transform(ent).GridUid, out var child)
            ? child.LinkedUid
            : null;

        if (ent.Comp.SplitBlocker is { } blocker && (deedGrid == null || Transform(blocker).ParentUid != deedGrid))
        {
            QueueDel(blocker);
            ent.Comp.SplitBlocker = null;
        }

        if (deedGrid is { } grid && ent.Comp.SplitBlocker == null && !TerminatingOrDeleted(grid))
            ent.Comp.SplitBlocker = SpawnAttachedTo(SaleBlocker, new EntityCoordinates(grid, Vector2.Zero));
    }

    private void OnCoreParentChanged(Entity<ShipAiInterfaceComponent> ent, ref EntParentChangedMessage args)
    {
        _saleChecks.Add(ent);
    }

    private void UpdateLooks(Entity<ShipAiInterfaceComponent> ent)
    {
        var hasHeld = TryGetBrain(ent, out var held) && HasAi(held);
        var looks = (hasHeld ? CompOrNull<StationAiCustomizationComponent>(held)?.ProtoIds : null) ?? NoLooks;
        if (looks.TryGetValue(ShipAiSystem.FaceGroup, out var faceId) &&
            ShipAiSystem.TryGetFaceLayer(_protos, faceId, out var faceLayer))
            _appearance.SetData(ent, StationAiVisualState.Key, faceLayer);
        else
            _appearance.RemoveData(ent, StationAiVisualState.Key);

        var casing = looks.TryGetValue(ShipAiSystem.CasingGroup, out var c) ? c : ShipAiSystem.Fallback(_protos, ShipAiSystem.CasingGroup);
        var accessory = looks.TryGetValue(ShipAiSystem.AccessoryGroup, out var a) ? a : ShipAiSystem.Fallback(_protos, ShipAiSystem.AccessoryGroup);
        var screen = looks.TryGetValue(ShipAiSystem.ScreenGroup, out var s) ? s.Id : "None";
        _appearance.SetData(ent, ShipAiVisuals.Casing, casing.Id);
        _appearance.SetData(ent, ShipAiVisuals.Accessory, accessory.Id);
        _appearance.SetData(ent, ShipAiVisuals.Screen, screen);

        var total = CompOrNull<DamageableComponent>(ent)?.TotalDamage.Float() ?? 0f;
        _appearance.SetData(ent, ShipAiVisuals.Static, hasHeld && (HasComp<ShipAiBootingComponent>(ent) || total >= ent.Comp.StaticDamage));
        _appearance.SetData(ent, ShipAiVisuals.NoBrain, !hasHeld);
    }
}
