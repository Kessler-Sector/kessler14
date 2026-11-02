using Content.Server.EUI;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Eui;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed class ShipAiShipyardEui(ShipAiShipyardSystem system, EntityUid station) : BaseEui
{
    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return system.BuildState(station, Player);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is not ShipAiShipyardBuyMessage buy)
            return;

        if (system.TryBuy(station, buy.Vessel, Player))
            Close();
        else
            StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        system.OnShipyardClosed(Player);
    }
}
