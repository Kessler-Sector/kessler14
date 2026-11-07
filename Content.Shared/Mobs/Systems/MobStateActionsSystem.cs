using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Mobs.Components;

namespace Content.Shared.Mobs.Systems;

/// <summary>
///     Adds and removes defined actions when a mob's <see cref="MobState"/> changes.
/// </summary>
public sealed class MobStateActionsSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MobStateActionsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MobStateComponent, ComponentStartup>(OnMobStateComponentInit); // Kessler: was ComponentInit
    }

    private void OnMobStateChanged(EntityUid uid, MobStateActionsComponent component, MobStateChangedEvent args)
    {
        ComposeActions(uid, component, args.NewMobState);
    }

    // Kessler: on startup, not init. On a persistent load, init ran before the actions container was set up, and
    // adding an action crashed the whole load. A mob loaded with its actions keeps them.
    private void OnMobStateComponentInit(EntityUid uid, MobStateComponent component, ComponentStartup args)
    {
        if (!TryComp<MobStateActionsComponent>(uid, out var mobStateActionsComp))
            return;

        if (mobStateActionsComp.GrantedActions.Count > 0 && mobStateActionsComp.GrantedActions.TrueForAll(Exists))
            return;

        ComposeActions(uid, mobStateActionsComp, component.CurrentState);
    }

    /// <summary>
    /// Adds or removes actions from a mob based on mobstate.
    /// </summary>
    private void ComposeActions(EntityUid uid, MobStateActionsComponent component, MobState newMobState)
    {
        if (!TryComp<ActionsComponent>(uid, out var action))
            return;

        foreach (var act in component.GrantedActions)
        {
            // Kessler: while a mob is being loaded from a save, deleting its saved actions (still listed by its action
            // container) makes the whole load fail, so they are deleted after the load instead.
            if (LifeStage(uid) < EntityLifeStage.MapInitialized)
                QueueDel(act);
            else
                Del(act);
            // End Kessler
        }
        component.GrantedActions.Clear();

        if (!component.Actions.TryGetValue(newMobState, out var toGrant))
            return;

        foreach (var id in toGrant)
        {
            EntityUid? act = null;
            if (_actions.AddAction(uid, ref act, id, uid, action))
                component.GrantedActions.Add(act.Value);
        }
    }
}
