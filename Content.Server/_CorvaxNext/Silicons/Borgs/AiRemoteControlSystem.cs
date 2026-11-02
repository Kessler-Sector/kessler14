// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 ImHoks <142083149+ImHoks@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 ImHoks <imhokzzzz@gmail.com>
// SPDX-FileCopyrightText: 2025 KillanGenifer <killangenifer@gmail.com>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Radio.Components;
using Content.Server.Silicons.Laws;
using Content.Shared._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Actions;
using Content.Shared.Mind;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationAi;
using Content.Shared.Verbs;
using Robust.Shared.Map; // Mono
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Content.Server._WF.Silicons.ShipAi; // Wayfarer

namespace Content.Server._CorvaxNext.Silicons.Borgs;

public sealed partial class AiRemoteControlSystem : SharedAiRemoteControlSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SiliconLawSystem _lawSystem = default!;
    [Dependency] private SharedStationAiSystem _stationAiSystem = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private UserInterfaceSystem _userInterface = default!;
    [Dependency] private SharedTransformSystem _xformSystem = default!;

    [Dependency] private IMapManager _map = default!; // Mono
    [Dependency] private ShipAiInterfaceSystem _shipAi = default!; // Wayfarer

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AiRemoteControllerComponent, ReturnMindIntoAiEvent>(OnReturnMindIntoAi);
        SubscribeLocalEvent<AiRemoteControllerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AiRemoteControllerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AiRemoteControllerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<StationAiHeldComponent, AiRemoteControllerComponent.RemoteDeviceActionMessage>(OnUiRemoteAction);
        SubscribeLocalEvent<StationAiHeldComponent, ToggleRemoteDevicesScreenEvent>(OnToggleRemoteDevicesScreen);
    }

    // Wayfarer
    private bool OnAiGrid(EntityUid ai, EntityUid target)
    {
        return _shipAi.InRemoteRange(ai, target) ||
               Transform(ai).GridUid is { } grid && Transform(target).GridUid == grid;
    }

    public void RestoreRadio(Entity<AiRemoteControllerComponent> entity)
    {
        if (entity.Comp.PreviouslyTransmitterChannels is { } transmitterChannels &&
            TryComp(entity, out IntrinsicRadioTransmitterComponent? transmitter))
            transmitter.Channels = [.. transmitterChannels];

        if (entity.Comp.PreviouslyActiveRadioChannels is { } radioChannels &&
            TryComp(entity, out ActiveRadioComponent? activeRadio))
            activeRadio.Channels = [.. radioChannels];
    }

    public void ReturnToAi(Entity<AiRemoteControllerComponent> entity)
    {
        if (entity.Comp.AiHolder == null)
            return;

        RestoreRadio(entity);
        ReturnMindIntoAi(entity);
    }
    // Wayfarer End

    private void OnMapInit(Entity<AiRemoteControllerComponent> entity, ref MapInitEvent args)
    {
        EnsureComp<StationAiVisionComponent>(entity.Owner);
        EntityUid? actionEnt = null;

        _actions.AddAction(entity.Owner, ref actionEnt, entity.Comp.BackToAiAction);

        if (actionEnt != null)
            entity.Comp.BackToAiActionEntity = actionEnt.Value;
    }

    private void OnShutdown(Entity<AiRemoteControllerComponent> entity, ref ComponentShutdown args)
    {
        _actions.RemoveAction(entity.Owner, entity.Comp.BackToAiActionEntity);

        if (TryComp(entity, out IntrinsicRadioTransmitterComponent? transmitter)
            && entity.Comp.PreviouslyTransmitterChannels != null)
            transmitter.Channels = [.. entity.Comp.PreviouslyTransmitterChannels];

        if (TryComp(entity, out ActiveRadioComponent? activeRadio)
            && entity.Comp.PreviouslyActiveRadioChannels != null)
            activeRadio.Channels = [.. entity.Comp.PreviouslyActiveRadioChannels];

        ReturnMindIntoAi(entity);
    }

    private void OnGetVerbs(Entity<AiRemoteControllerComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;

        if (!TryComp<StationAiHeldComponent>(user, out _))
            return;

        if (!OnAiGrid(user, entity)) // Wayfarer
            return; // Wayfarer

        var verb = new AlternativeVerb
        {
            Text = Loc.GetString("ai-remote-control"),
            Act = () => AiTakeControl(user, entity)
        };
        args.Verbs.Add(verb);
    }

    private void OnReturnMindIntoAi(Entity<AiRemoteControllerComponent> entity, ref ReturnMindIntoAiEvent args) =>
        ReturnToAi(entity); // Wayfarer: ReturnMindIntoAi(entity)<ReturnToAi(entity)

    public void AiTakeControl(EntityUid ai, EntityUid entity)
    {
        if (!_mind.TryGetMind(ai, out var mindId, out var mind))
            return;

        if (_mind.TryGetMind(entity, out _, out _))
            return;

        if (!TryComp<StationAiHeldComponent>(ai, out var stationAiHeldComp))
            return;

        if (!TryComp<AiRemoteControllerComponent>(entity, out var aiRemoteComp))
            return;

        // Wayfarer
        /*
        if (!_map.TryFindGridAt(Transform(ai).MapPosition, out var grid, out var _) || Transform(entity).GridUid != grid)
            return; // Mono no controlling borgs outside the ai's grid.
        */
        if (!OnAiGrid(ai, entity))
            return;
        // Wayfarer End

        if (TryComp(entity, out IntrinsicRadioTransmitterComponent? transmitter))
        {
            aiRemoteComp.PreviouslyTransmitterChannels = [.. transmitter.Channels];

            if (TryComp(ai, out IntrinsicRadioTransmitterComponent? stationAiTransmitter))
                transmitter.Channels = [.. stationAiTransmitter.Channels];
        }

        if (TryComp(entity, out ActiveRadioComponent? activeRadio))
        {
            aiRemoteComp.PreviouslyActiveRadioChannels = [.. activeRadio.Channels];

            if (TryComp(ai, out ActiveRadioComponent? stationAiActiveRadio))
                activeRadio.Channels = [.. stationAiActiveRadio.Channels];
        }

        _mind.ControlMob(ai, entity);
        aiRemoteComp.AiHolder = ai;
        aiRemoteComp.LinkedMind = mindId;

        stationAiHeldComp.CurrentConnectedEntity = entity;

        if (!_stationAiSystem.TryGetCore(ai, out var stationAiCore))
            return;

        _stationAiSystem.SwitchRemoteEntityMode(stationAiCore, false);

        RewriteLaws(ai, entity);
    }

    private void OnToggleRemoteDevicesScreen(EntityUid uid, StationAiHeldComponent component, ToggleRemoteDevicesScreenEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.Performer, out var actor))
            return;
        args.Handled = true;

        _userInterface.TryToggleUi(uid, RemoteDeviceUiKey.Key, actor.PlayerSession);

        var aiGrid = Transform(uid).GridUid; // Mono
        var query = EntityManager.EntityQueryEnumerator<AiRemoteControllerComponent>();
        var remoteDevices = new List<RemoteDevicesData>();

        while (query.MoveNext(out var queryUid, out var comp))
        {
            if (!OnAiGrid(uid, queryUid)) // Wayfarer: Transform(queryUid).GridUid != aiGrid<!OnAiGrid(uid, queryUid)
                continue;
            var data = new RemoteDevicesData
            {
                NetEntityUid = GetNetEntity(queryUid),
                DisplayName = Comp<MetaDataComponent>(queryUid).EntityName
            };

            remoteDevices.Add(data);
        }

        var state = new RemoteDevicesBuiState(remoteDevices);
        _userInterface.SetUiState(uid, RemoteDeviceUiKey.Key, state);
    }

    private void OnUiRemoteAction(EntityUid uid, StationAiHeldComponent component, AiRemoteControllerComponent.RemoteDeviceActionMessage msg)
    {
        if (msg.RemoteAction == null)
            return;

        var target = GetEntity(msg.RemoteAction?.Target);

        if (!HasComp<AiRemoteControllerComponent>(target))
            return;

        if (!OnAiGrid(uid, target.Value)) // Wayfarer
            return; // Wayfarer

        switch (msg.RemoteAction?.ActionType)
        {
            case RemoteDeviceActionEvent.RemoteDeviceActionType.MoveToDevice:
                if (!_stationAiSystem.TryGetCore(uid, out var stationAiCore)
                    || stationAiCore.Comp?.RemoteEntity == null)
                    return;
                _xformSystem.SetCoordinates(stationAiCore.Comp.RemoteEntity.Value, Transform(target.Value).Coordinates);
                break;

            case RemoteDeviceActionEvent.RemoteDeviceActionType.TakeControl:
                AiTakeControl(uid, target.Value);
                break;
        }
    }

    private void RewriteLaws(EntityUid from, EntityUid to)
    {
        if (!TryComp<SiliconLawProviderComponent>(from, out var fromLawsComp))
            return;

        if (!TryComp<SiliconLawProviderComponent>(to, out var toLawsComp))
            return;

        if (fromLawsComp.Lawset == null)
            return;

        var fromLaws = _lawSystem.GetLaws(from);
        _lawSystem.SetLaws(fromLaws.Laws, to, silent: true);
    }
}
