using Content.Client.UserInterface.Fragments;
using Content.Shared._Kessler.Contracts;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Kessler.Contracts;

/// <summary>
/// The Contracts PDA program: open procurement contracts at every station and your own.
/// </summary>
public sealed partial class ContractsUi : UIFragment
{
    private ContractsUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new ContractsUiFragment();
        _fragment.OnRefresh += () => userInterface.SendMessage(new CartridgeUiMessage(new ContractsRefreshMessage()));
        _fragment.OnAccept += (station, id) => userInterface.SendMessage(new CartridgeUiMessage(new ContractAcceptMessage(station, id)));
        _fragment.OnAbandon += (station, id) => userInterface.SendMessage(new CartridgeUiMessage(new ContractAbandonMessage(station, id)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is ContractsUiState contracts)
            _fragment?.UpdateState(contracts);
    }
}
