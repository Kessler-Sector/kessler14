using Content.Server.EUI;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Eui;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed class ShipAiBriefingEui(ShipAiFaction faction) : BaseEui
{
    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new ShipAiBriefingEuiState(faction);
    }
}
