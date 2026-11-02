using Content.Shared._WF.Silicons.ShipAi;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiCorePlacementMode : PlacementMode
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xforms;
    private readonly ShipAiSystem _shipAi;

    public ShipAiCorePlacementMode(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
        _map = _entMan.System<SharedMapSystem>();
        _xforms = _entMan.System<SharedTransformSystem>();
        _shipAi = _entMan.System<ShipAiSystem>();
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        MouseCoords = ScreenToCursorGrid(mouseScreen);
        if (_xforms.GetGrid(MouseCoords) is not { } gridUid ||
            !_entMan.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return;

        CurrentTile = _map.GetTileRef(gridUid, grid, MouseCoords);
        MouseCoords = _map.GridTileToLocal(gridUid, grid, CurrentTile.GridIndices);
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        return _entMan.TryGetComponent<ShipAiProjectionComponent>(_player.LocalEntity, out var projection) &&
               projection.Ship is { } ship &&
               _shipAi.TryGetCoreTile(ship, position, out _);
    }
}
