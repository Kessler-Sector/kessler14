using Content.Shared.Atmos;
using Content.Shared._NF.Atmos.Systems;

namespace Content.Shared._NF.Atmos.Components;

[RegisterComponent, Access(typeof(SharedGasDepositSystem))]
public sealed partial class GasDepositComponent : Component
{
    /// <summary>
    /// Gases left in the deposit.
    /// </summary>
    [DataField]
    public GasMixture Deposit = new();

    /// <summary>
    /// The maximum number of moles for this deposit to be considered "mostly depleted".
    /// </summary>
    [DataField] // Kessler: only set at map init, so it has to be saved
    public float LowMoles;
}
