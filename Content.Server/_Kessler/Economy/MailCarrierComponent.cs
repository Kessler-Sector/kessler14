namespace Content.Server._Kessler.Economy;

/// <summary>
/// The last person other than the recipient who picked up a parcel. They get a share of its value when the recipient
/// opens it.
/// </summary>
[RegisterComponent, Access(typeof(MailCarrierSystem))]
public sealed partial class MailCarrierComponent : Component
{
    [DataField]
    public EntityUid? Carrier;
}
