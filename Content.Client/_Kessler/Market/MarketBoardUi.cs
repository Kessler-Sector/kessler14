using Content.Client.UserInterface.Fragments;
using Content.Shared._Kessler.Market;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Kessler.Market;

/// <summary>
/// The market board PDA program: what each station pays for each category of goods right now.
/// </summary>
public sealed partial class MarketBoardUi : UIFragment
{
    private MarketBoardUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MarketBoardUiFragment();
        _fragment.OnRefresh += () => userInterface.SendMessage(new CartridgeUiMessage(new MarketBoardRefreshMessage()));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is MarketBoardUiState board)
            _fragment?.UpdateState(board);
    }
}
