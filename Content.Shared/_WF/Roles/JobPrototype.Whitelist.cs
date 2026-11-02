using Robust.Shared.Prototypes;

namespace Content.Shared.Roles;

public sealed partial class JobPrototype
{
    [DataField]
    public ProtoId<JobPrototype>? WhitelistJob { get; private set; }
}
