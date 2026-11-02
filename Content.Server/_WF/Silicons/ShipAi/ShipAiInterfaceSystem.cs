using Content.Server.Chat.Systems;
using Content.Server.Silicons.StationAi;
using Content.Server.Station.Systems;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Movement.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using static Content.Server.Chat.Systems.ChatSystem;
using Content.Shared.Mind;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Popups;
using System.Linq;
using Content.Shared.NameIdentifier;
using Content.Shared.NameModifier.EntitySystems;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Content.Server.Radio.Components;
using Content.Shared.Radio;
using Content.Shared.Holopad;
using Robust.Shared.Map.Components;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Server.GameTicking;
using Content.Server._NF.Station.Components;
using Content.Server.EUI;
using Content.Shared.Wires;
using Content.Shared.Interaction.Components;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.GameTicking;
using Content.Server.Silicons.Laws;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.Actions.Components;
using Content.Shared.Access.Systems;
using Content.Server._NF.Shipyard.Systems;
using Robust.Shared.GameObjects.Components.Localization;
using Content.Shared.Telephone;
using Content.Shared.SSDIndicator;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed partial class ShipAiInterfaceSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly StationAiSystem _stationAi = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NameModifierSystem _nameModifier = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly SiliconLawSystem _laws = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly GrammarSystem _grammar = default!;
    [Dependency] private readonly SSDIndicatorSystem _ssd = default!;

    private static readonly ProtoId<TagPrototype> CanPilotTag = "CanPilot";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipAiInterfaceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipAiInterfaceComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShipAiInterfaceComponent, EntInsertedIntoContainerMessage>(OnInserted, before: [typeof(StationAiSystem)], after: [typeof(ContainerCompSystem)]);
        SubscribeLocalEvent<ShipAiInterfaceComponent, EntRemovedFromContainerMessage>(OnRemoved, after: [typeof(StationAiSystem), typeof(ContainerCompSystem)]);
        SubscribeLocalEvent<ShipAiEyeComponent, EntParentChangedMessage>(OnEyeParentChanged);
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandICChatRecipients);
        SubscribeLocalEvent<ShipAiInterfaceComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<ShipAiInterfaceComponent, StationAiCustomizationMessage>(OnCustomization, after: [typeof(SharedStationAiSystem)]);
        SubscribeLocalEvent<ShipAiInterfaceComponent, EncryptionChannelsChangedEvent>(OnChannelsChanged);
        SubscribeLocalEvent<ShipAiCoreComponent, MapInitEvent>(OnCoreMapInit);
        SubscribeLocalEvent<ShipAiCoreComponent, MindAddedMessage>(OnCoreMindAdded);
        SubscribeLocalEvent<ShipAiCoreComponent, MindRemovedMessage>(OnCoreMindRemoved);

        InitializeControls();
        InitializeTransfer();
        InitializeVisuals();
        InitializeHelpers();
        InitializeModules();
    }

    private void OnMapInit(Entity<ShipAiInterfaceComponent> ent, ref MapInitEvent args)
    {
        UpdateLooks(ent);
    }

    private void OnAnchorAttempt(Entity<ShipAiInterfaceComponent> ent, ref AnchorAttemptEvent args)
    {
        if (HasComp<ShuttleDeedComponent>(Transform(ent).GridUid))
            return;

        _popup.PopupEntity(Loc.GetString("ship-ai-needs-deed"), ent, args.User);
        args.Cancel();
    }

    private void OnShutdown(Entity<ShipAiInterfaceComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.SplitBlocker);
    }

    private void OnInserted(Entity<ShipAiInterfaceComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ModuleSlot)
        {
            if (TryGetBrain(ent, out var held))
                ApplyModule(args.Entity, held);
            return;
        }

        if (args.Container.ID != StationAiHolderComponent.Container)
            return;

        var brain = args.Entity;
        var generatedName = HasComp<NameIdentifierComponent>(brain);
        RemComp<BlockMovementComponent>(brain);
        RemComp<ShipAiLostComponent>(brain);
        RemComp<NameIdentifierComponent>(brain);
        _nameModifier.RefreshNameModifiers(brain);
        EnsureComp<EyeComponent>(brain);
        EnsureComp<ContentEyeComponent>(brain);
        EnsureComp<InputMoverComponent>(brain);
        _tags.AddTag(brain, CanPilotTag);
        SyncRadio(ent);

        if (HasComp<ActorComponent>(brain) && TryComp<SSDIndicatorComponent>(brain, out var ssd))
            _ssd.StopSSD(brain, ssd);

        if (_mind.TryGetMind(brain, out _, out var mind) && mind.UserId is { } userId)
        {
            Stamp(ent, brain);
            ApplyIdentity(ent, brain, userId, generatedName ? null : mind.CharacterName);
            EnsureIdCard(brain);
            if (_players.TryGetSessionById(userId, out var session))
                Brief(brain, session);
        }

        SetModules(ent, brain, true);
        _saleChecks.Add(ent);
        if (HasAi(brain))
        {
            if (TryComp<ShipAiCoreComponent>(brain, out var core))
                core.Occupied = true;
            StartBoot(ent);
        }
        else
            UpdateLooks(ent);
        UpdateCoreLook(brain);
    }

    private void OnRemoved(Entity<ShipAiInterfaceComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ModuleSlot)
        {
            if (TryGetBrain(ent, out var held))
                RemoveModule(args.Entity, held);
            StopScan(ent);
            return;
        }

        if (args.Container.ID != StationAiHolderComponent.Container)
            return;

        var brain = args.Entity;
        if (TryComp<ShipAiCoreComponent>(brain, out var core))
            core.Occupied = false;
        StopScan(ent);
        RemComp<ShipAiBootingComponent>(ent);
        _audio.PlayPvs(ent.Comp.ShutdownSound, ent);
        if (!TerminatingOrDeleted(brain))
        {
            EnsureComp<BlockMovementComponent>(brain);
            _tags.RemoveTag(brain, CanPilotTag);
            RestoreRadio(brain);
            if (MetaData(brain).EntityPrototype is { } proto && !proto.Components.ContainsKey(_factory.GetComponentName<InputMoverComponent>()))
                RemComp<InputMoverComponent>(brain);
            RecallFromBorg(brain);
            SetModules(ent, brain, false);
            _alerts.ClearAlert(brain, CoreHealthAlert);
            UpdateCoreLook(brain);
            DeleteDetachedActions(brain);
        }

        _saleChecks.Add(ent);
    }

    private void DeleteDetachedActions(EntityUid brain)
    {
        if (!_containers.TryGetContainer(brain, ActionsContainerComponent.ContainerId, out var container))
            return;

        foreach (var action in container.ContainedEntities.ToList())
        {
            if (CompOrNull<ActionComponent>(action)?.AttachedEntity == null)
                QueueDel(action);
        }
    }

    private ShipAiFaction? StationFaction(Entity<ShipAiInterfaceComponent> ent, EntityUid? station)
    {
        if (!TryComp<ExtraShuttleInformationComponent>(station, out var info) ||
            info.Vessel is not { } vessel ||
            !_protos.TryIndex(vessel, out var proto) ||
            !ent.Comp.Factions.TryGetValue(proto.Group, out var faction))
            return null;

        return faction;
    }

    private void Brief(EntityUid core, ICommonSession session)
    {
        var memory = EnsureComp<ShipAiBrainComponent>(core);
        if (memory.Briefed ||
            CompOrNull<ShipAiCoreComponent>(core)?.Faction is not { } faction ||
            faction == ShipAiFaction.Other)
            return;

        memory.Briefed = true;
        _eui.OpenEui(new ShipAiBriefingEui(faction), session);
    }

    private void Stamp(Entity<ShipAiInterfaceComponent> ent, EntityUid core)
    {
        if (!TryComp<ShipAiCoreComponent>(core, out var stamp) || stamp.Faction != null)
            return;

        stamp.Faction = StationFaction(ent, _station.GetOwningStation(ent)) ?? ShipAiFaction.Other;
        ApplyStamp((core, stamp));
    }

    private void OnCoreMapInit(Entity<ShipAiCoreComponent> ent, ref MapInitEvent args)
    {
        ApplyStamp(ent);
        UpdateCoreLook(ent);
    }

    private void ApplyStamp(Entity<ShipAiCoreComponent> core)
    {
        if (core.Comp.Faction is not { } faction)
            return;

        if (core.Comp.StampComponents.TryGetValue(faction, out var components))
        {
            EntityManager.AddComponents(core, components, removeExisting: false);
            _laws.GetLaws(core);
        }
        if (TryGetInterface(core, out var iface))
            SyncRadio(iface);
        if (core.Comp.StampActions.TryGetValue(faction, out var actions))
        {
            foreach (var action in actions)
                _actions.AddAction(core, action);
        }
    }

    private void EnsureIdCard(EntityUid brain)
    {
        if (CompOrNull<PdaComponent>(brain)?.ContainedId is { } existing)
        {
            _idCard.TryChangeFullName(existing, Name(brain));
            _shipyard.SetDeedOwner(existing, Name(brain));
            if (Transform(brain).GridUid is { } grid && CompOrNull<ShuttleDeedComponent>(existing)?.ShuttleUid == grid)
                _shipyard.SetDeedOwner(grid, Name(brain));
            return;
        }

        if (!TryComp<ShipAiCoreComponent>(brain, out var stamp) || stamp.Faction is not { } faction ||
            !stamp.IdCards.TryGetValue(faction, out var cardProto) ||
            !TryComp<PdaComponent>(brain, out var pda) || pda.ContainedId != null)
            return;

        var card = SpawnInContainerOrDrop(cardProto, brain, PdaComponent.PdaIdSlotId);
        _idCard.TryChangeFullName(card, Name(brain));
    }

    private void OnCoreMindAdded(Entity<ShipAiCoreComponent> ent, ref MindAddedMessage args)
    {
        if (TryGetInterface(ent, out var iface))
        {
            ent.Comp.Occupied = true;
            if (ent.Comp.Faction == null)
            {
                Stamp(iface, ent);
                EnsureIdCard(ent);
                if (args.Mind.Comp.UserId is { } userId && _players.TryGetSessionById(userId, out var session))
                    Brief(ent, session);
            }
        }
        OnCoreMindChanged(ent);
    }

    private bool WrongFaction(Entity<ShipAiInterfaceComponent> ent, EntityUid brain)
    {
        return CompOrNull<ShipAiCoreComponent>(brain)?.Faction is { } faction &&
               faction != (StationFaction(ent, _station.GetOwningStation(ent)) ?? ShipAiFaction.Other);
    }

    private void OnCoreMindRemoved(Entity<ShipAiCoreComponent> ent, ref MindRemovedMessage args)
    {
        OnCoreMindChanged(ent);
    }

    private void OnCoreMindChanged(EntityUid core)
    {
        UpdateCoreLook(core);
        if (TryGetInterface(core, out var iface))
            _saleChecks.Add(iface);
    }

    private ProtoId<JobPrototype> JobFor(EntityUid brain)
    {
        var core = CompOrNull<ShipAiCoreComponent>(brain);
        return core != null && core.Jobs.TryGetValue(core.Faction ?? ShipAiFaction.Other, out var job) ? job : ShipAiSystem.Job;
    }

    private bool HasAi(EntityUid core)
    {
        return _mind.TryGetMind(core, out _, out _) ||
               CompOrNull<StationAiHeldComponent>(core)?.CurrentConnectedEntity != null;
    }

    public bool TryGetShipCore(EntityUid? grid, out Entity<TelephoneComponent> core, out EntityUid brain)
    {
        core = default;
        brain = default;
        if (grid is not { } g)
            return false;

        foreach (var iface in OnGrid<ShipAiInterfaceComponent>(g))
        {
            if (Transform(iface).Anchored &&
                TryGetBrain(iface, out brain) &&
                HasAi(brain) &&
                TryComp<TelephoneComponent>(iface, out var telephone))
            {
                core = (iface, telephone);
                return true;
            }
        }

        return false;
    }

    private bool TryGetWaitingCore(EntityUid iface, out EntityUid core)
    {
        return TryGetBrain(iface, out core) && !HasAi(core);
    }

    private void UpdateCoreLook(EntityUid core)
    {
        _appearance.SetData(core, ShipAiCoreVisuals.State, HasAi(core) ? "On" : "Off");
    }

    public bool InstallAi(Entity<ShipAiInterfaceComponent> ent, EntityUid mindId, ICommonSession session, EntityUid station, string name)
    {
        if (!TryGetWaitingCore(ent, out var core))
            return false;

        Stamp(ent, core);
        _mind.MakeSentient(core);
        EnsureComp<ShipAiBrainComponent>(core);
        ApplyIdentity(ent, core, session.UserId, name);
        _mind.TransferTo(mindId, core);
        EnsureIdCard(core);
        if (TryComp<PdaComponent>(core, out var pda) && pda.ContainedId is { } card && Transform(ent).GridUid is { } grid)
            _shipyard.CopyDeedToCard(card, grid);

        var profile = _prefs.GetPreferences(session.UserId).SelectedCharacter as HumanoidCharacterProfile ?? HumanoidCharacterProfile.Random();
        RaiseLocalEvent(core, new PlayerSpawnCompleteEvent(core, session, JobFor(core).Id, true, true,
            _gameTicker.PlayersJoinedRoundNormally, station, profile), true);
        return true;
    }

    private void RecallFromBorg(EntityUid brain)
    {
        if (!TryUnlinkBorg(brain, out var mind))
            return;

        if (TryComp<StationAiHeldComponent>(brain, out var held))
            held.CurrentConnectedEntity = null;
        _mind.TransferTo(mind, brain);
    }

    private bool TryUnlinkBorg(EntityUid brain, out EntityUid mind)
    {
        mind = default;
        var query = EntityQueryEnumerator<AiRemoteControllerComponent>();
        while (query.MoveNext(out var borg, out var remote))
        {
            if (remote.AiHolder != brain || remote.LinkedMind is not { } linked)
                continue;

            mind = linked;
            remote.AiHolder = null;
            remote.LinkedMind = null;
            _remote.RestoreRadio((borg, remote));
            return true;
        }

        return false;
    }

    private void OnEyeParentChanged(Entity<ShipAiEyeComponent> ent, ref EntParentChangedMessage args)
    {
        KeepEyeNearShip(ent);
    }

    private void KeepEyeNearShip(Entity<ShipAiEyeComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!TryComp<MovementRelayTargetComponent>(ent, out var relay) ||
            !_stationAi.TryGetCore(relay.Source, out var core) ||
            Transform(core).GridUid is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var eyeXform = Transform(ent);
        if (eyeXform.ParentUid == grid && _shipAi.NearShipTile(grid, gridComp, eyeXform.LocalPosition, ent.Comp.MercyRange))
            return;

        _xforms.DropNextTo(ent.Owner, core.Owner);
    }

    private void OnExpandICChatRecipients(ExpandICChatRecipientsEvent ev)
    {
        var sourceXform = Transform(ev.Source);
        var sourcePos = _xforms.GetWorldPosition(sourceXform);

        var query = EntityQueryEnumerator<ShipAiInterfaceComponent, StationAiCoreComponent>();
        while (query.MoveNext(out var uid, out _, out var core))
        {
            if (!core.Remote || core.RemoteEntity is not { } eye)
                continue;

            var eyeXform = Transform(eye);
            if (eyeXform.MapID != sourceXform.MapID)
                continue;

            var range = (sourcePos - _xforms.GetWorldPosition(eyeXform)).Length();
            if (range > ev.VoiceRange ||
                !TryGetBrain(uid, out var held) ||
                !TryComp<ActorComponent>(held, out var actor))
                continue;

            ev.Recipients.TryAdd(actor.PlayerSession, new ICChatRecipientData(range, false));
        }
    }

    private bool TryGetBrain(EntityUid core, out EntityUid brain)
    {
        return _stationAi.TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out brain);
    }

    public void ApplyIdentity(Entity<ShipAiInterfaceComponent> ent, EntityUid brain, NetUserId userId, string? characterName = null)
    {
        if (!_protos.TryIndex(ShipAiSystem.Loadout, out var roleProto))
            return;

        RoleLoadout? loadout = null;
        if (_prefs.GetPreferencesOrNull(userId)?.SelectedCharacter is HumanoidCharacterProfile profile)
            profile.Loadouts.TryGetValue(ShipAiSystem.Loadout, out loadout);

        if (characterName != null)
            _metaData.SetEntityName(brain, characterName);
        else
            _spawning.EquipRoleName(brain, loadout ?? new RoleLoadout(ShipAiSystem.Loadout), roleProto);
        if (_mind.TryGetMind(brain, out var mindId, out var mind))
        {
            mind.CharacterName = Name(brain);
            Dirty(mindId, mind);
        }
        _metaData.SetEntityName(ent, Name(brain));
        _grammar.SetProperNoun((brain, EnsureComp<GrammarComponent>(brain)), true);
        _grammar.SetProperNoun((ent, EnsureComp<GrammarComponent>(ent)), true);
        if (TryComp<StationAiCoreComponent>(ent, out var core) && core.RemoteEntity is { } eye)
            _metaData.SetEntityName(eye, Loc.GetString("station-ai-eye-name", ("name", Name(brain))));

        var look = CompOrNull<ShipAiBrainComponent>(brain)?.Look;
        string? Pick(ProtoId<StationAiCustomizationGroupPrototype> group) =>
            look != null && look.TryGetValue(group, out var choice) ? choice.Id : ShipAiSystem.Chosen(loadout, group);

        ApplyChoice(ent, ShipAiSystem.FaceGroup, Pick(ShipAiSystem.FaceGroup), null);
        ApplyChoice(ent, ShipAiSystem.ScreenGroup, Pick(ShipAiSystem.ScreenGroup), null);
        ApplyChoice(ent, ShipAiSystem.HologramGroup, Pick(ShipAiSystem.HologramGroup), ShipAiSystem.FaceHologram);
        ApplyChoice(ent, ShipAiSystem.CasingGroup, Pick(ShipAiSystem.CasingGroup), ShipAiSystem.Fallback(_protos, ShipAiSystem.CasingGroup));
        ApplyChoice(ent, ShipAiSystem.AccessoryGroup, Pick(ShipAiSystem.AccessoryGroup), ShipAiSystem.Fallback(_protos, ShipAiSystem.AccessoryGroup));
    }

    private void ApplyChoice(Entity<ShipAiInterfaceComponent> ent, ProtoId<StationAiCustomizationGroupPrototype> group, string? chosen, ProtoId<StationAiCustomizationPrototype>? fallback)
    {
        if (!_protos.TryIndex(group, out var choices) || choices.ProtoIds.Count == 0)
            return;

        var allowed = choices.ProtoIds.Where(c => ShipAiSystem.ShowsChoice(true, group, c)).ToList();
        if (allowed.Count == 0)
            return;

        var pick = chosen != null && allowed.Contains(chosen)
            ? new ProtoId<StationAiCustomizationPrototype>(chosen)
            : fallback ?? _random.Pick(allowed);

        var msg = new StationAiCustomizationMessage(group, pick);
        RaiseLocalEvent(ent, msg);
    }

    private void OnChannelsChanged(Entity<ShipAiInterfaceComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        SyncRadio(ent);
    }

    private void RestoreRadio(EntityUid brain)
    {
        if (MetaData(brain).EntityPrototype is not { } proto)
            return;

        if (proto.TryGetComponent<ActiveRadioComponent>(out var radio, _factory))
        {
            var channels = new HashSet<string>(radio.Channels);
            if (TryComp<ShipAiCoreComponent>(brain, out var core) &&
                core.AlertChannels.TryGetValue(core.Faction ?? ShipAiFaction.Other, out var alert))
                channels.Add(alert);
            EnsureComp<ActiveRadioComponent>(brain).Channels = channels;
        }
        if (proto.TryGetComponent<IntrinsicRadioTransmitterComponent>(out var transmitter, _factory))
            EnsureComp<IntrinsicRadioTransmitterComponent>(brain).Channels = new HashSet<string>(transmitter.Channels);
    }

    private void OnCustomization(Entity<ShipAiInterfaceComponent> ent, ref StationAiCustomizationMessage args)
    {
        UpdateHologram(ent);
        UpdateLooks(ent);

        if (TryGetBrain(ent, out var held) &&
            TryComp<StationAiCustomizationComponent>(held, out var customization))
            EnsureComp<ShipAiBrainComponent>(held).Look = new(customization.ProtoIds);
    }

    private void UpdateHologram(Entity<ShipAiInterfaceComponent> ent)
    {
        if (!TryGetBrain(ent, out var held) ||
            !TryComp<StationAiCustomizationComponent>(held, out var customization) ||
            !customization.ProtoIds.TryGetValue(ShipAiSystem.HologramGroup, out var hologram) ||
            hologram != ShipAiSystem.FaceHologram)
            return;

        var layers = new List<PrototypeLayerData>();
        if (customization.ProtoIds.TryGetValue(ShipAiSystem.ScreenGroup, out var screenId) &&
            _protos.TryIndex(screenId, out var screen) &&
            screen.LayerData.TryGetValue("Screen", out var screenLayer))
            layers.Add(screenLayer);

        if (customization.ProtoIds.TryGetValue(ShipAiSystem.FaceGroup, out var faceId) &&
            ShipAiSystem.TryGetFaceLayer(_protos, faceId, out var faceLayer))
            layers.Add(faceLayer);

        if (layers.Count == 0)
            return;

        var avatar = EnsureComp<HolographicAvatarComponent>(held);
        avatar.LayerData = layers.ToArray();
        Dirty(held, avatar);
    }
}
