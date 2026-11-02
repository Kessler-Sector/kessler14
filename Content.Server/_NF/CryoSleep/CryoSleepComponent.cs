using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Server._NF.CryoSleep;

[RegisterComponent]
public sealed partial class CryoSleepComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public ContainerSlot BodyContainer = default!;

    /// <summary>
    /// The sound that is played when a player leaves the game via cryo
    /// </summary>
    [DataField]
    public SoundSpecifier LeaveSound = new SoundCollectionSpecifier("RadiationPulse");

    // Wayfarer
    /// <summary>
    /// The sound that is played when the pod refuses a body.
    /// </summary>
    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");
    // Wayfarer End

    /// <summary>
    ///   The ID of the latest DoAfter event associated with this entity. May be null if there's no DoAfter going on.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public DoAfterId? CryosleepDoAfter = null;

    /// <summary>
    /// The next time something should be able to try and escape the pod.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextInternalOpenAttempt;

    /// <summary>
    /// The amount of time to wait between attempting to remove entities from the pod.
    /// </summary>
    [ViewVariables]
    public TimeSpan InternalOpenAttemptDelay = TimeSpan.FromSeconds(0.5);
}
