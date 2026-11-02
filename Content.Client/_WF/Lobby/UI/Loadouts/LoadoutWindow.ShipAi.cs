using Content.Client._WF.Silicons.ShipAi;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Silicons.StationAi;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI.Loadouts;

public sealed partial class LoadoutWindow
{
    private bool TryAddAiTab(IPrototypeManager protoManager, LoadoutGroupPrototype groupProto, ProtoId<LoadoutGroupPrototype> group, RoleLoadout loadout)
    {
        if (!protoManager.HasIndex<StationAiCustomizationGroupPrototype>(group.Id))
            return false;

        MinHeight = 480;
        SearchContainer.Visible = false;

        var current = ShipAiSystem.Chosen(loadout, group.Id);
        var grid = new ShipAiChoiceGrid(protoManager, group.Id, current, groupProto.MinLimit == 0 ? Loc.GetString("wf-loadout-ai-random") : null);
        grid.OnSelected += id =>
        {
            if (id != null)
                OnLoadoutPressed?.Invoke(group, id);
            else if (current != null)
                OnLoadoutUnpressed?.Invoke(group, current);

            current = id;
        };

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(grid);
        LoadoutGroupsContainer.AddTab(scroll, Loc.GetString(groupProto.Name));
        return true;
    }
}
