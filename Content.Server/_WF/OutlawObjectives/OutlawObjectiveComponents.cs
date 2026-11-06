using Content.Shared._NF.Shipyard;
using Content.Shared._WF.OutlawObjectives;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom; // Kessler

namespace Content.Server._WF.OutlawObjectives;

[RegisterComponent]
public sealed partial class OutlawObjectiveComponent : Component
{
    // Kessler: objective state is saved; it was runtime only, so a restart dropped every objective.
    [DataField]
    public HashSet<ProtoId<OutlawObjectivePrototype>> Pending = new();

    [DataField]
    public HashSet<ProtoId<OutlawObjectivePrototype>> Active = new();

    [DataField]
    public Dictionary<ProtoId<OutlawObjectivePrototype>, EntityUid> Items = new();

    [ViewVariables]
    public EntityUid? LastAttacker;

    public bool Settled => Pending.Count == 0 && Active.Count == 0 && Items.Count == 0;
}

[RegisterComponent]
public sealed partial class OutlawObjectivePendingComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] // Kessler: saved as time remaining
    public TimeSpan NextActivation;
}

[RegisterComponent]
public sealed partial class OutlawObjectiveItemComponent : Component
{
    [DataField] // Kessler: saved
    public EntityUid OwningCharacter;

    [DataField]
    public string OwnerName = string.Empty;

    [DataField]
    public ProtoId<OutlawObjectivePrototype> Objective;

    // The drive is handed out at spawn, before its objective activates, so it has its own flag.
    [DataField]
    public bool Active;
}

[RegisterComponent]
public sealed partial class OutlawObjectiveSettingsComponent : Component
{
    [DataField(required: true)]
    public ProtoId<StackPrototype> StackDC;

    [DataField(required: true)]
    public ProtoId<StackPrototype> StackSpesos;

    [DataField(required: true)]
    public SoundSpecifier RewardSound = default!;

    [DataField(required: true)]
    public string ItemSlot = default!;

    [DataField(required: true)]
    public EntityWhitelist OutlawRoles = default!;

    [DataField(required: true)]
    public float GibPayoutRange;

    [DataField(required: true)]
    public TimeSpan ActivationIntervalMin;

    [DataField(required: true)]
    public TimeSpan ActivationIntervalMax;

    [DataField(required: true)]
    public TimeSpan CheckInterval;

    [DataField(required: true)]
    public List<ShipyardConsoleUiKey> ShipSaleConsoles = new();

    [DataField(required: true)]
    public TimeSpan DeedForgeTime;

    [DataField(required: true)]
    public EntProtoId ForgedDeed;

    [DataField(required: true)]
    public float ShipDamageFraction;
}

// On a player ship while its owner's ship damage objective is active.
[RegisterComponent]
public sealed partial class OutlawShipDamageComponent : Component
{
    // Kessler: saved, so damage credited before a restart still counts.
    [DataField]
    public EntityUid Target;

    [DataField]
    public ProtoId<OutlawObjectivePrototype> Objective;

    // Anchored things with health when tracking started.
    [DataField]
    public int Total;

    [DataField]
    public int Needed;

    [DataField]
    public Dictionary<EntityUid, int> Credit = new();

    // The outlaw each attacking ship's guns are credited to. A cache, rebuilt as guns fire.
    [ViewVariables]
    public Dictionary<EntityUid, EntityUid> GunOwners = new();
}

[RegisterComponent]
public sealed partial class ForgedDeedComponent : Component
{
    // Kessler: saved. Nullable, because the forged deed prototype carries this component unset, and an unset entity
    // can't be written to a save.
    [DataField]
    public EntityUid? Forger;

    [DataField]
    public EntityUid? Target;

    [DataField]
    public ProtoId<OutlawObjectivePrototype>? Objective;
}

[RegisterComponent]
public sealed partial class LastConstructionUserComponent : Component
{
    [ViewVariables]
    public EntityUid User;
}
