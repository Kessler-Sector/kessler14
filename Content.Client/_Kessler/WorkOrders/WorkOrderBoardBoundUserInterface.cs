using Content.Shared._Kessler.WorkOrders;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Kessler.WorkOrders;

[UsedImplicitly]
public sealed class WorkOrderBoardBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WorkOrderBoardMenu? _menu;

    public WorkOrderBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu ??= this.CreateWindow<WorkOrderBoardMenu>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WorkOrderBoardUiState boardState)
            _menu?.UpdateState(boardState);
    }
}
