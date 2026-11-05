using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

// Shitmed Change
public sealed partial class CCVars
{
    /// <summary>
    ///     Whether a player can perform surgery on themselves.
    /// </summary>
    public static readonly CVarDef<bool> CanOperateOnSelf =
        CVarDef.Create("surgery.can_operate_on_self", true, CVar.SERVERONLY);
}
