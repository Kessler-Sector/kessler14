using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Doors.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Client.Player;
using Robust.Shared.Utility;
using Content.Client.Silicons.StationAi;
using Content.Shared.Verbs;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiRadialSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    private static readonly SpriteSpecifier EditAccessIcon =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/Objects/Tools/access_configurator.rsi"), "icon");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorComponent, GetStationAiRadialEvent>(OnGetRadial);
        SubscribeLocalEvent<ShipAiInterfaceComponent, GetVerbsEvent<Verb>>(OnGetVerbs, after: [typeof(StationAiSystem)]);
    }

    private void OnGetVerbs(Entity<ShipAiInterfaceComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        var customizeVerb = Loc.GetString("station-ai-customization-menu");
        args.Verbs.RemoveWhere(verb => verb.Text == customizeVerb);
    }

    private void OnGetRadial(Entity<DoorComponent> ent, ref GetStationAiRadialEvent args)
    {
        if (!HasComp<ShipAiComponent>(_player.LocalEntity))
            return;

        args.Actions.Add(new StationAiRadial
        {
            Sprite = EditAccessIcon,
            Tooltip = Loc.GetString("ship-ai-edit-access"),
            Event = new ShipAiEditAccessEvent(),
        });
    }
}
