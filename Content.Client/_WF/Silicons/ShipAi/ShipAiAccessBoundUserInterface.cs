using Content.Client.Access.UI;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Access;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using static Content.Shared.Access.Components.AccessOverriderComponent;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiAccessBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private AccessOverriderWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<AccessOverriderWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.PrivilegedIdButton.Parent!.Visible = false;
        _window.OnSubmit += access => SendMessage(new ShipAiSetAccessMessage(access));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not AccessOverriderBoundUserInterfaceState castState)
            return;

        _window.SetAccessLevels(_prototypeManager, new List<ProtoId<AccessLevelPrototype>>(castState.AllowedModifyAccessList ?? []));
        _window.UpdateState(_prototypeManager, castState);
    }
}
