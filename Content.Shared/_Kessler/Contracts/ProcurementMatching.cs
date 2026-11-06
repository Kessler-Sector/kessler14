using Content.Shared.Stacks;
using Content.Shared.Whitelist;

namespace Content.Shared._Kessler.Contracts;

public static class ProcurementMatching
{
    /// <summary>
    /// Whether an entity is the goods a procurement contract wants.
    /// </summary>
    public static bool Matches(this ProcurementContractPrototype proto, EntityUid uid, IEntityManager entMan, EntityWhitelistSystem whitelist)
    {
        if (proto.Stack != null)
            return entMan.TryGetComponent<StackComponent>(uid, out var stack) && stack.StackTypeId == proto.Stack;

        return whitelist.IsWhitelistPass(proto.Whitelist, uid) && !whitelist.IsWhitelistPass(proto.Blacklist, uid);
    }

    /// <summary>
    /// How many units of goods an entity is: its stack count, or one.
    /// </summary>
    public static int Units(EntityUid uid, IEntityManager entMan)
    {
        return entMan.TryGetComponent<StackComponent>(uid, out var stack) ? stack.Count : 1;
    }
}
