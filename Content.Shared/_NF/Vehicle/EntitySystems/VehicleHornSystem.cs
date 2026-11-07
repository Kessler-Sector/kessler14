using Content.Shared._NF.Vehicle.Components;
using Content.Shared.Actions;
using Content.Shared._Goobstation.Vehicles;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._NF.Vehicle.EntitySystems;

public sealed class VehicleHornSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleHornComponent, ComponentStartup>(OnVehicleHornInit); // Kessler: was ComponentInit, see the handler
        SubscribeLocalEvent<VehicleHornComponent, ComponentShutdown>(OnVehicleHornShutdown);
        SubscribeLocalEvent<VehicleHornComponent, HornActionEvent>(OnHornHonkAction);
    }

    /// Horn-only functions
    private void OnVehicleHornShutdown(EntityUid uid, VehicleHornComponent component, ComponentShutdown args)
    {
        // Perf: If the entity is deleting itself, no reason to change these back.
        if (Terminating(uid))
            return;

        _actions.RemoveAction(uid, component.ActionEntity);
    }

    // Kessler: actions are added at startup, not init. On a persistent load, init runs before the actions containers
    // are set up and crashed the whole load; at startup EnsureAction reuses the saved action.
    private void OnVehicleHornInit(EntityUid uid, VehicleHornComponent component, ComponentStartup args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, out var _, component.Action);
    }

    /// <summary>
    /// This fires when the vehicle entity presses the honk action
    /// </summary>
    private void OnHornHonkAction(EntityUid uid, VehicleHornComponent vehicle, HornActionEvent args)
    {
        if (args.Handled || vehicle.HornSound == null)
            return;

        // TODO: Need audio refactor maybe, just some way to null it when the stream is over.
        // For now better to just not loop to keep the code much cleaner.
        vehicle.HonkPlayingStream = _audio.PlayPredicted(vehicle.HornSound, uid, args.Performer)?.Entity;
        args.Handled = true;
    }
}
