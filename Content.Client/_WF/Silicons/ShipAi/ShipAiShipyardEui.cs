using System.Numerics;
using Content.Client._NF.Shipyard.UI;
using Content.Client.Eui;
using Content.Client.UserInterface.Controls;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Eui;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Content.Shared._NF.Shipyard;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiShipyardEui : BaseEui
{
    private readonly IPrototypeManager _protos = IoCManager.Resolve<IPrototypeManager>();
    private readonly FancyWindow _window = new() { MinSize = new Vector2(520, 460) };
    private readonly Label _balance = new() { Margin = new Thickness(10, 5) };
    private readonly TabContainer _tabs = new() { VerticalExpand = true };
    private bool _closed;

    public ShipAiShipyardEui()
    {
        _window.Title = Loc.GetString("ship-ai-shipyard-title");
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        box.AddChild(_balance);
        box.AddChild(_tabs);
        _window.ContentsContainer.AddChild(box);
        _window.OnClose += () =>
        {
            if (!_closed)
                SendMessage(new CloseEuiMessage());
        };
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _closed = true;
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not ShipAiShipyardEuiState shipyard)
            return;

        _balance.Text = Loc.GetString("ship-ai-shipyard-balance", ("balance", shipyard.Balance));
        _tabs.RemoveAllChildren();
        var tabs = new Dictionary<ShipyardConsoleUiKey, BoxContainer>();
        foreach (var listing in shipyard.Listings)
        {
            if (!_protos.TryIndex<VesselPrototype>(listing.Vessel, out var vessel))
                continue;

            if (!tabs.TryGetValue(listing.Group, out var rows))
            {
                rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 0) };
                var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
                scroll.AddChild(rows);
                _tabs.AddChild(scroll);
                _tabs.SetTabTitle(tabs.Count, Loc.GetString($"ship-ai-shipyard-tab-{listing.Group}"));
                tabs[listing.Group] = rows;
            }

            var row = new VesselRow
            {
                Vessel = vessel,
                VesselName = { Text = vessel.Name },
                Price = { Text = listing.Price == 0 ? Loc.GetString("shipyard-console-menu-listing-free") : BankSystemExtensions.ToSpesoString(listing.Price) },
                Purchase = { Disabled = listing.Price > shipyard.Balance },
                Guidebook = { Disabled = vessel.GuidebookPage is null, TooltipDelay = 0.2f, ToolTip = vessel.Description },
            };
            var id = listing.Vessel;
            row.Purchase.OnPressed += _ => SendMessage(new ShipAiShipyardBuyMessage(id));
            rows.AddChild(row);
        }

        _tabs.TabsVisible = tabs.Count > 1;
    }
}
