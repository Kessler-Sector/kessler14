namespace Content.Server._NF.SectorServices;

/// <summary>
/// A station with this component will host all sector-wide services.
/// </summary>
[RegisterComponent]
[Access(typeof(SectorServiceSystem))]
public sealed partial class StationSectorServiceHostComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField] // Kessler: saved, so the service entity is part of a persistent save
    public EntityUid? SectorUid; // Kessler: EntityUid = Invalid<EntityUid?, only the hosting station has one
}
