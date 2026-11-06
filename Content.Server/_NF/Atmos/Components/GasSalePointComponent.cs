using Content.Shared._NF.Atmos.Systems;
using Content.Shared.Atmos;

namespace Content.Server._NF.Atmos.Components;

[RegisterComponent, Access(typeof(SharedGasDepositSystem))]
public sealed partial class GasSalePointComponent : Component
{
    [DataField]
    public string InletPipePortName = "inlet";

    // An unlimited internal gas storage, tracking how much gas has been put into the entity.
    [DataField] // Kessler: saved, so gas piped in but not sold yet survives a restart
    public GasMixture GasStorage = new();
}
