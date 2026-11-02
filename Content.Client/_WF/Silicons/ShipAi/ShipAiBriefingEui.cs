using System.Numerics;
using Content.Client.Eui;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Eui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiBriefingEui : BaseEui
{
    private readonly FancyWindow _window = new() { MinSize = new Vector2(420, 200) };
    private readonly RichTextLabel _text = new() { VerticalExpand = true, Margin = new Thickness(10) };

    public ShipAiBriefingEui()
    {
        var ok = new Button
        {
            Text = Loc.GetString("ship-ai-briefing-ok"),
            HorizontalAlignment = Control.HAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        ok.OnPressed += _ => _window.Close();

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        box.AddChild(_text);
        box.AddChild(ok);
        _window.ContentsContainer.AddChild(box);
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not ShipAiBriefingEuiState briefing)
            return;

        var key = briefing.Faction.ToString().ToLowerInvariant();
        _window.Title = Loc.GetString($"ship-ai-briefing-{key}-title");
        _text.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString($"ship-ai-briefing-{key}-text")));
    }
}
