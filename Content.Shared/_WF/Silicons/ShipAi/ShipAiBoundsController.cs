using System.Numerics;
using Content.Shared.Friction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WF.Silicons.ShipAi;

public sealed class ShipAiBoundsController : VirtualController
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ShipAiSystem _shipAi = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));
        UpdatesAfter.Add(typeof(TileFrictionController));
        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var projections = EntityQueryEnumerator<ShipAiProjectionComponent, PhysicsComponent>();
        while (projections.MoveNext(out var uid, out var projection, out var body))
        {
            if (projection.Ship is { } ship && (!prediction || body.Predict))
                KeepOnShip(uid, body, ship, 0f, frameTime);
        }

        var eyes = EntityQueryEnumerator<ShipAiEyeComponent, MovementRelayTargetComponent, PhysicsComponent>();
        while (eyes.MoveNext(out var uid, out var eye, out var relay, out var body))
        {
            if ((!prediction || body.Predict) && body.LinearVelocity != Vector2.Zero &&
                _stationAi.TryGetCore(relay.Source, out var core) &&
                Transform(core).GridUid is { } grid)
                KeepOnShip(uid, body, grid, eye.MercyRange, frameTime);
        }
    }

    private void KeepOnShip(EntityUid uid, PhysicsComponent body, EntityUid grid, float margin, float frameTime)
    {
        var velocity = body.LinearVelocity;
        if (velocity == Vector2.Zero || frameTime <= 0f)
            return;

        var xform = Transform(uid);
        if (xform.ParentUid != grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var position = xform.LocalPosition;
        var rotation = _xforms.GetWorldRotation(grid);
        var step = (-rotation).RotateVec(velocity) * frameTime;
        if (!_shipAi.NearShipTile(grid, gridComp, position, margin))
        {
            var toward = gridComp.LocalAABB.ClosestPoint(position) - position;
            if (MathF.Sign(step.X) != MathF.Sign(toward.X))
                step.X = 0f;
            if (MathF.Sign(step.Y) != MathF.Sign(toward.Y))
                step.Y = 0f;
        }
        else if (!_shipAi.NearShipTile(grid, gridComp, position + step, margin))
        {
            var alongX = new Vector2(step.X, 0f);
            var alongY = new Vector2(0f, step.Y);
            step = _shipAi.NearShipTile(grid, gridComp, position + alongX, margin) ? alongX
                : _shipAi.NearShipTile(grid, gridComp, position + alongY, margin) ? alongY
                : Vector2.Zero;
        }

        _physics.SetLinearVelocity(uid, rotation.RotateVec(step / frameTime), body: body);
    }
}
