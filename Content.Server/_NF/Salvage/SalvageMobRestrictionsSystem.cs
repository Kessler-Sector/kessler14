using Content.Shared.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Mobs;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Map.Events; // Kessler
using Content.Server._Kessler.Persistence; // Kessler

namespace Content.Server._NF.Salvage;

public sealed class SalvageMobRestrictionsSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly PersistenceSaveSystem _persistence = default!; // Kessler

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NFSalvageMobRestrictionsComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<NFSalvageMobRestrictionsComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<SalvageMobRestrictionsGridComponent, ComponentRemove>(OnRemoveGrid);
        SubscribeLocalEvent<NFSalvageMobRestrictionsComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<NFSalvageMobRestrictionsComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<NFSalvageMobRestrictionsComponent, EntityTerminatingEvent>(OnTerminating); // Kessler
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization); // Kessler
    }

    private void OnInit(EntityUid uid, NFSalvageMobRestrictionsComponent component, ComponentInit args)
    {
        var gridUid = Transform(uid).ParentUid;
        if (!EntityManager.EntityExists(gridUid))
        {
            // Give up, we were spawned improperly
            return;
        }
        // When this code runs, the system hasn't actually gotten ahold of the grid entity yet.
        // So it therefore isn't in a position to do this.
        if (!TryComp(gridUid, out SalvageMobRestrictionsGridComponent? rg))
        {
            rg = AddComp<SalvageMobRestrictionsGridComponent>(gridUid);
        }
        rg!.MobsToKill.Add(uid);
        component.LinkedGridEntity = gridUid;
    }

    private void OnRemove(EntityUid uid, NFSalvageMobRestrictionsComponent component, ComponentRemove args)
    {
        if (TryComp(component.LinkedGridEntity, out SalvageMobRestrictionsGridComponent? rg))
        {
            rg.MobsToKill.Remove(uid);
        }
    }

    // Kessler: a grid's list of mobs could keep a mob that was deleted (one deleted while it was still being set up
    // slipped past OnRemove), and saving the grid then threw on the missing entity, so the whole grid was left out of
    // the save. The same happened for mobs the persistent save leaves out (ones that despawn on a timer). Drop mobs
    // from the list as they are deleted, and prune anything that won't be saved before any save.
    private void OnTerminating(Entity<NFSalvageMobRestrictionsComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp(ent.Comp.LinkedGridEntity, out SalvageMobRestrictionsGridComponent? rg))
            rg.MobsToKill.Remove(ent);
    }

    private void OnBeforeSerialization(BeforeSerializationEvent args)
    {
        var query = EntityQueryEnumerator<SalvageMobRestrictionsGridComponent>();
        while (query.MoveNext(out _, out var rg))
            rg.MobsToKill.RemoveAll(mob => !Exists(mob)
                                           || MetaData(mob).EntityPrototype is { } proto && !_persistence.IsPersisted(proto));
    }
    // End Kessler

    private void OnRemoveGrid(EntityUid uid, SalvageMobRestrictionsGridComponent component, ComponentRemove args)
    {
        foreach (EntityUid target in component.MobsToKill)
        {
            // Don't destroy yourself, don't destroy things being destroyed.
            if (uid == target || TerminatingOrDeleted(target))
                continue;

            // Check if this mob should be despawned when off grid
            if (!TryComp(target, out NFSalvageMobRestrictionsComponent? mobRestrictions))
                continue;

            // Skip detonation if the mob is set to not despawn when off linked grid
            if (!mobRestrictions.DespawnIfOffLinkedGrid)
                continue;

            if (TryComp(target, out BodyComponent? body))
            {
                // Creates a pool of blood on death, but remove the organs.
                var gibs = _body.GibBody(target, body: body, gibOrgans: true);
                foreach (var gib in gibs)
                    Del(gib);
            }
            else
            {
                // No body, probably a robot - explode it and delete the body
                // _explosion.QueueExplosion(target, ExplosionSystem.DefaultExplosionPrototypeId, 5, 10, 5);
                Del(target);
            }
        }
    }

    private void OnMobState(EntityUid uid, NFSalvageMobRestrictionsComponent component, MobStateChangedEvent args)
    {
        // If this entity is being destroyed, no need to fiddle with components
        if (TerminatingOrDeleted(uid))
            return;

        if (args.NewMobState == MobState.Dead)
        {
            EntityManager.AddComponents(uid, component.AddComponentsOnDeath);
            EntityManager.RemoveComponents(uid, component.RemoveComponentsOnDeath);
        }
        else if (args.OldMobState == MobState.Dead)
        {
            EntityManager.AddComponents(uid, component.AddComponentsOnRevival);
            EntityManager.RemoveComponents(uid, component.RemoveComponentsOnRevival);
        }
    }

    private void OnParentChanged(EntityUid uid, NFSalvageMobRestrictionsComponent component, ref EntParentChangedMessage args)
    {
        // If this entity is being destroyed, no need to fiddle with components
        if (TerminatingOrDeleted(uid))
            return;

        var gridUid = Transform(uid).GridUid;
        var popupMessage = Loc.GetString(component.LeaveGridPopup);

        if (component.LinkedGridEntity == gridUid && HasComp<SalvageMobRestrictionsGridComponent>(gridUid))
        {
            EntityManager.AddComponents(uid, component.AddComponentsReturnGrid);
            EntityManager.RemoveComponents(uid, component.RemoveComponentsReturnGrid);

            if (!EntityManager.TryGetComponent(uid, out ActorComponent? actor))
                return;

            if (actor.PlayerSession.AttachedEntity == null)
                return;

            if (component.DespawnIfOffLinkedGrid)
                _adminLogger.Add(LogType.AdminMessage, LogImpact.Low, $"{ToPrettyString(actor.PlayerSession.AttachedEntity.Value):player} returned to dungeon grid");
        }
        else
        {
            EntityManager.AddComponents(uid, component.AddComponentsLeaveGrid);
            EntityManager.RemoveComponents(uid, component.RemoveComponentsLeaveGrid);

            if (!EntityManager.TryGetComponent(uid, out ActorComponent? actor))
                return;

            if (actor.PlayerSession.AttachedEntity == null)
                return;

            if (component.DespawnIfOffLinkedGrid)
            {
                _adminLogger.Add(LogType.AdminMessage, LogImpact.Low, $"{ToPrettyString(actor.PlayerSession.AttachedEntity.Value):player} left the dungeon grid");
                _popupSystem.PopupEntity(popupMessage, actor.PlayerSession.AttachedEntity.Value, actor.PlayerSession, PopupType.MediumCaution);
            }
        }
    }
}

