using Content.Shared._Kessler.Licences;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Kessler.Licences;

[UsedImplicitly]
public sealed class LicenceOfficeBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private LicenceOfficeMenu? _menu;

    public LicenceOfficeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<LicenceOfficeMenu>();
        _menu.OnBuy += id => SendMessage(new LicenceOfficeBuyMessage(id));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is LicenceOfficeStateMessage state)
            _menu?.UpdateState(state);
    }
}
