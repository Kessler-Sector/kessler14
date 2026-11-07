using Content.Shared.Actions;
using Content.Shared.Atmos.Components;

namespace Content.Shared.Atmos.EntitySystems;

public abstract class SharedFirestarterSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FirestarterComponent, MapInitEvent>(OnMapInit); // Kessler: was ComponentInit
    }

    /// <summary>
    /// Adds the firestarter action.
    /// </summary>
    // Kessler: on map init, not component init. The action is saved, and on a load component init ran before the
    // actions containers were set up, which crashed the whole load.
    private void OnMapInit(EntityUid uid, FirestarterComponent component, MapInitEvent args)
    {
        _actionsSystem.AddAction(uid, ref component.FireStarterActionEntity, component.FireStarterAction, uid);
        Dirty(uid, component);
    }
}
