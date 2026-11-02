using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server.Database;
using Robust.Shared.Network;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed class ShipAiShipyardSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly ShipAiInterfaceSystem _interface = default!;
    [Dependency] private readonly ShipAiSystem _shipAi = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private bool _enabled;

    private readonly Dictionary<NetUserId, ShipAiShipyardEui> _buyEuis = new();
    private readonly Dictionary<NetUserId, Purchase> _pending = new();

    private sealed record Purchase(EntityUid Ship, EntityUid? ShipStation, int Paid, EntProtoId? Module);

    private ShipAiShipyardPrototype Proto => _protos.Index(ShipAiShipyardPrototype.DefaultId);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipAiShipyardPointComponent, StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning, before: [typeof(ContainerSpawnPointSystem), typeof(ShipAiInterfaceSystem)]);
        SubscribeLocalEvent<PlayerJoinAttemptEvent>(OnJoinAttempt);
        SubscribeLocalEvent<ShipAiProjectionComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<ShipAiProjectionComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<ShipAiProjectionComponent, PlayerDetachedEvent>(OnDetached);
        SubscribeNetworkEvent<ShipAiPlaceCoreRequest>(OnPlaceRequest);
        Subs.CVar(_cfg, WFCCVars.ShipAiShipyard, OnCVarChanged, true);
    }

    private void OnCVarChanged(bool value)
    {
        _enabled = value;
        var query = EntityQueryEnumerator<ShipAiShipyardPointComponent>();
        while (query.MoveNext(out var station, out var point))
            UpdateSlot((station, point));
    }

    private void OnStationPostInit(Entity<ShipAiShipyardPointComponent> ent, ref StationPostInitEvent args)
    {
        UpdateSlot(ent);
    }

    private void UpdateSlot(Entity<ShipAiShipyardPointComponent> ent)
    {
        if (!Proto.Points.TryGetValue(ent.Comp.Point, out var point))
            return;

        if (_enabled)
            _stationJobs.MakeJobUnlimited(ent, point.Job);
        else
            _stationJobs.TrySetJobSlot(ent, point.Job, 0);
    }

    private void OnJoinAttempt(PlayerJoinAttemptEvent args)
    {
        if (!_enabled || _pending.ContainsKey(args.Player.UserId) ||
            !TryComp<ShipAiShipyardPointComponent>(args.Station, out var point) ||
            !Proto.Points.TryGetValue(point.Point, out var pointProto) ||
            args.Job != pointProto.Job.Id)
            return;

        args.Cancel();
        if (_buyEuis.ContainsKey(args.Player.UserId))
            return;

        var eui = new ShipAiShipyardEui(this, args.Station);
        _buyEuis[args.Player.UserId] = eui;
        _eui.OpenEui(eui, args.Player);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null || !_enabled ||
            args.Station is not { } station ||
            args.Session is not { } session ||
            !_pending.TryGetValue(session.UserId, out var purchase) ||
            !TryComp<ShipAiShipyardPointComponent>(station, out var point) ||
            !Proto.Points.TryGetValue(point.Point, out var pointProto) ||
            args.Job != pointProto.Job)
            return;

        _pending.Remove(session.UserId);
        var projection = Spawn(Proto.Projection, CenterTile(purchase.Ship));
        var comp = EnsureComp<ShipAiProjectionComponent>(projection);
        comp.Ship = purchase.Ship;
        comp.ShipStation = purchase.ShipStation;
        comp.Paid = purchase.Paid;
        comp.Module = purchase.Module;
        Dirty(projection, comp);

        if (_protos.TryIndex(ShipAiSystem.Loadout, out var roleProto))
        {
            RoleLoadout? loadout = null;
            args.HumanoidCharacterProfile?.Loadouts.TryGetValue(ShipAiSystem.Loadout, out loadout);
            _spawning.EquipRoleName(projection, loadout ?? new RoleLoadout(ShipAiSystem.Loadout), roleProto);
        }

        args.SpawnResult = projection;
    }

    private EntityCoordinates CenterTile(EntityUid grid)
    {
        var gridComp = Comp<MapGridComponent>(grid);
        var center = gridComp.LocalAABB.Center;
        var best = center;
        var bestDistance = float.MaxValue;
        var tiles = _map.GetAllTilesEnumerator(grid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            var position = _map.GridTileToLocal(grid, gridComp, tile.Value.GridIndices).Position;
            var distance = (position - center).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = position;
        }

        return new EntityCoordinates(grid, best);
    }

    public ShipAiShipyardEuiState BuildState(EntityUid station, ICommonSession session)
    {
        var listings = new List<ShipAiShipyardListing>();
        if (TryGetPoint(station, out var point))
        {
            foreach (var vessel in _protos.EnumeratePrototypes<VesselPrototype>())
            {
                if (Listed(point, vessel))
                    listings.Add(new ShipAiShipyardListing(vessel.ID, point.Free ? 0 : vessel.Price, vessel.Group));
            }

            listings.Sort((a, b) =>
            {
                var group = point.Groups.IndexOf(a.Group).CompareTo(point.Groups.IndexOf(b.Group));
                if (group != 0)
                    return group;
                return a.Price != b.Price ? a.Price.CompareTo(b.Price) : string.CompareOrdinal(a.Vessel, b.Vessel);
            });
        }

        _bank.TryGetBalance(session, out var balance);
        return new ShipAiShipyardEuiState(listings, balance);
    }

    private bool TryGetPoint(EntityUid station, [NotNullWhen(true)] out ShipAiShipyardPoint? point)
    {
        point = null;
        return TryComp<ShipAiShipyardPointComponent>(station, out var stationPoint) &&
               Proto.Points.TryGetValue(stationPoint.Point, out point);
    }

    private static bool Listed(ShipAiShipyardPoint point, VesselPrototype vessel)
    {
        return point.Groups.Contains(vessel.Group) && vessel.Price > 0 &&
               (point.Whitelist == null || point.Whitelist.Contains(vessel.ID)) &&
               !point.Blacklist.Contains(vessel.ID);
    }

    public bool TryBuy(EntityUid station, string vesselId, ICommonSession session)
    {
        if (_pending.ContainsKey(session.UserId) ||
            !TryGetPoint(station, out var point) ||
            !_protos.TryIndex<VesselPrototype>(vesselId, out var vessel) || !Listed(point, vessel))
            return false;

        var price = point.Free ? 0 : vessel.Price;
        if (price > 0 && !TryCharge(session, price))
            return false;

        var owner = _prefs.GetPreferences(session.UserId).SelectedCharacter is HumanoidCharacterProfile profile ? profile.Name : session.Name;
        if (!_shipyard.TryPurchaseVessel(station, vessel, owner, price, out var ship, out var shipStation))
        {
            Refund(session, price);
            return false;
        }

        point.Modules.TryGetValue(vessel.Group, out var module);
        _pending[session.UserId] = new Purchase(ship.Value, shipStation, price, module);
        _gameTicker.MakeJoinGame(session, station, point.Job);

        // The projection takes the purchase when it spawns, so one still waiting means the join failed.
        if (!_pending.Remove(session.UserId))
            return true;

        Refund(session, price);
        if (shipStation is { } shipStationUid)
            _station.DeleteStation(shipStationUid);
        QueueDel(ship);
        return false;
    }

    private bool TryCharge(ICommonSession session, int amount)
    {
        var prefs = _prefs.GetPreferences(session.UserId);
        return prefs.SelectedCharacter is HumanoidCharacterProfile profile &&
               _bank.TryBankWithdraw(session, prefs, profile, amount, out _);
    }

    private void Refund(ICommonSession session, int amount)
    {
        if (amount <= 0)
            return;

        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs))
        {
            RefundOffline(session.UserId, amount);
            return;
        }

        if (prefs.SelectedCharacter is HumanoidCharacterProfile profile)
            _bank.TryBankDeposit(session, prefs, profile, amount, out _);
    }

    private async void RefundOffline(NetUserId user, int amount)
    {
        var prefs = await _db.GetPlayerPreferencesAsync(user, CancellationToken.None);
        if (prefs?.SelectedCharacter is not HumanoidCharacterProfile profile)
            return;

        var slot = prefs.IndexOfCharacter(profile);
        await _db.SaveCharacterSlotAsync(user, profile.WithBankBalance(profile.BankBalance + amount), slot);
    }

    public void OnShipyardClosed(ICommonSession session)
    {
        _buyEuis.Remove(session.UserId);
    }

    private void OnInteractionAttempt(Entity<ShipAiProjectionComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMindRemoved(Entity<ShipAiProjectionComponent> ent, ref MindRemovedMessage args)
    {
        _players.TryGetSessionById(args.Mind.Comp.UserId, out var session);
        Cancel(ent, session);
    }

    private void OnDetached(Entity<ShipAiProjectionComponent> ent, ref PlayerDetachedEvent args)
    {
        Cancel(ent, args.Player);
    }

    private void Cancel(Entity<ShipAiProjectionComponent> ent, ICommonSession? session)
    {
        if (ent.Comp.Done)
            return;

        ent.Comp.Done = true;
        if (session != null)
            Refund(session, ent.Comp.Paid);
        if (ent.Comp.ShipStation is { } shipStation)
            _station.DeleteStation(shipStation);
        if (ent.Comp.Ship is { } ship)
            QueueDel(ship);
        QueueDel(ent);
    }

    private void OnPlaceRequest(ShipAiPlaceCoreRequest msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid ||
            !TryComp<ShipAiProjectionComponent>(uid, out var comp) || comp.Done ||
            comp.Ship is not { } ship || comp.ShipStation is not { } shipStation ||
            !_mind.TryGetMind(uid, out var mindId, out _))
            return;

        if (!_shipAi.TryGetCoreTile(ship, GetCoordinates(msg.Coordinates), out var coordinates))
        {
            _popup.PopupEntity(Loc.GetString("ship-ai-shipyard-bad-spot"), uid, uid);
            return;
        }

        var iface = Spawn(Proto.Interface, coordinates);
        _xforms.SetLocalRotation(iface, msg.Direction.ToAngle());
        if (!TryComp<ShipAiInterfaceComponent>(iface, out var ifaceComp))
        {
            QueueDel(iface);
            return;
        }

        if (comp.Module is { } module)
            SpawnInContainerOrDrop(module, iface, ShipAiInterfaceSystem.ModuleSlot);

        comp.Done = true;
        if (!_interface.InstallAi((iface, ifaceComp), mindId, args.SenderSession, shipStation, Name(uid)))
        {
            comp.Done = false;
            QueueDel(iface);
            return;
        }

        QueueDel(uid);
    }
}
