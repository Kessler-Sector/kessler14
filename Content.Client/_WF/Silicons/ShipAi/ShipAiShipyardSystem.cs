using System.Linq;
using System.Numerics;
using Content.Shared._WF.Silicons.ShipAi;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiShipyardSystem : EntitySystem
{
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private static readonly EntProtoId Ghost = "ComputerShipAi";

    private DefaultWindow? _confirm;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_placement.IsActive || _confirm != null ||
            !TryComp<ShipAiProjectionComponent>(_player.LocalEntity, out var projection) ||
            projection.Ship == null)
            return;

        _placement.BeginPlacing(new PlacementInformation
        {
            EntityType = Ghost,
            IsTile = false,
            PlacementOption = nameof(ShipAiCorePlacementMode),
        }, new ShipAiCorePlacementHijack(this));
    }

    public IEnumerable<IDirectionalTextureProvider> GhostTextures()
    {
        return _protos.TryIndex(Ghost, out var proto)
            ? _sprite.GetPrototypeTextures(proto)
            : Enumerable.Empty<IDirectionalTextureProvider>();
    }

    public void Confirm(EntityCoordinates coordinates, Direction direction)
    {
        if (_confirm != null)
            return;

        var yes = new Button { Text = Loc.GetString("ship-ai-shipyard-place-yes") };
        var no = new Button { Text = Loc.GetString("ship-ai-shipyard-place-no") };
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalAlignment = Control.HAlignment.Center, SeparationOverride = 10 };
        buttons.AddChild(yes);
        buttons.AddChild(no);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
        box.AddChild(new Label { Text = Loc.GetString("ship-ai-shipyard-place-text") });
        box.AddChild(buttons);

        _confirm = new DefaultWindow { Title = Loc.GetString("ship-ai-shipyard-place-title"), MinSize = new Vector2(300, 110) };
        _confirm.Contents.AddChild(box);
        _confirm.OnClose += () => _confirm = null;

        yes.OnPressed += _ =>
        {
            RaiseNetworkEvent(new ShipAiPlaceCoreRequest(GetNetCoordinates(coordinates), direction));
            _placement.Clear();
            _confirm?.Close();
        };
        no.OnPressed += _ => _confirm?.Close();

        _confirm.OpenCentered();
    }
}

public sealed class ShipAiCorePlacementHijack(ShipAiShipyardSystem system) : PlacementHijack
{
    public override bool HijackPlacementRequest(EntityCoordinates coordinates)
    {
        system.Confirm(coordinates, Manager.Direction);
        return true;
    }

    public override void StartHijack(PlacementManager manager)
    {
        base.StartHijack(manager);
        manager.CurrentTextures = system.GhostTextures().ToList();
    }
}
