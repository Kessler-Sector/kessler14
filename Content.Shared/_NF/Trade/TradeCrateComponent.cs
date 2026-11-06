using System.Threading;
using Content.Shared._NF.Cargo;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom; // Kessler

namespace Content.Shared._NF.Trade;

/// <summary>
/// This is used to mark an entity to be used as a trade crate
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause, Access(typeof(SharedNFCargoSystem))]
public sealed partial class TradeCrateComponent : Component
{
    /// <summary>
    /// The value of the crate, in spesos, when delivered to its destination.
    /// </summary>
    [DataField(serverOnly: true)]
    public int ValueAtDestination;

    /// <summary>
    /// The value of the crate, in spesos, when delivered elsewhere.
    /// </summary>
    [DataField(serverOnly: true)]
    public int ValueElsewhere;

    /// <summary>
    /// If non-zero, this crate will be an express delivery.
    /// </summary>
    [DataField(serverOnly: true)]
    public TimeSpan ExpressDeliveryDuration = TimeSpan.Zero;

    /// <summary>
    /// If non-null, the package must be redeemed before this time to arrive unpenalized.
    /// </summary>
    [DataField(serverOnly: true, customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] // Kessler: saved as time remaining
    public TimeSpan? ExpressDeliveryTime;

    /// <summary>
    /// The bonus this package will receive if delivered on-time.
    /// </summary>
    [DataField(serverOnly: true)]
    public int ExpressOnTimeBonus;

    /// <summary>
    /// The penalty this package will receive if delivered late.
    /// </summary>
    [DataField(serverOnly: true)]
    public int ExpressLatePenalty;

    /// <summary>
    /// This crate's destination.
    /// </summary>
    // Kessler: saved, or a restart picks a new destination. Nullable, because an invalid entity in the prototype's
    // default would be logged as an error on every save.
    [DataField(serverOnly: true)]
    public EntityUid? DestinationStation;

    /// <summary>
    /// Kessler: whether the destination and express deadline have been set. Component init also runs when a saved
    /// crate is loaded, and must not roll them again.
    /// </summary>
    [DataField(serverOnly: true)]
    public bool Initialized;

    /// <summary>
    /// Cancellation token used to disable the express marker on the crate.
    /// </summary>
    public CancellationTokenSource? ExpressCancelToken;
}
