using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Moves ships out of the world into storage files, and brings them back when their owner recalls them at a shipyard
/// console. Impounded ships (unpaid upkeep) and ships whose owner hasn't logged in for a while are stored
/// automatically.
/// </summary>
/// <remarks>
/// A ship is only stored when nobody who might come back is aboard (the same rule as selling a ship). Stored files are
/// never deleted, so restoring an older sector save, whose list may still name a recalled ship, keeps working.
/// </remarks>
public sealed class ShipStorageSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PersistenceSaveSystem _persistenceSave = default!;
    [Dependency] private readonly PersistenceLoadSystem _persistenceLoad = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly ShipOwnershipSystem _ownership = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private TimeSpan _nextCheck;

    /// <summary>
    /// When each offline owner was last seen, cached so the database isn't asked every check.
    /// </summary>
    private readonly Dictionary<Guid, (DateTime LastSeen, TimeSpan CheckedAt)> _lastSeen = new();
    private readonly HashSet<Guid> _lastSeenPending = new();

    private static readonly TimeSpan LastSeenCacheTime = TimeSpan.FromHours(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipyardConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnShipyardVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromMinutes(_cfg.GetCVar(KesslerCVars.StorageCheckMinutes));

        if (_cfg.GetCVar(KesslerCVars.StorageEnabled))
            StoreDueShips(DateTime.UtcNow);
    }

    #region Automatic storage

    /// <summary>
    /// Stores impounded ships and ships whose owner has been away longer than the inactivity limit.
    /// </summary>
    public void StoreDueShips(DateTime now)
    {
        var inactiveAfter = TimeSpan.FromDays(_cfg.GetCVar(KesslerCVars.StorageInactiveDays));
        var due = new List<(EntityUid Ship, ShipStorageReason Reason)>();

        var query = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (query.MoveNext(out var uid, out var ownership))
        {
            if (ownership.ImpoundDue)
            {
                due.Add((uid, ShipStorageReason.Impounded));
                continue;
            }

            if (IsOwnerInactive(ownership.OwnerUserId, now, inactiveAfter))
                due.Add((uid, ShipStorageReason.Inactive));
        }

        foreach (var (ship, reason) in due)
        {
            if (!TryStoreShip(ship, reason, now, out var problem))
                Log.Info($"Not storing {ToPrettyString(ship)} ({reason}) yet: {problem}");
        }
    }

    /// <summary>
    /// Whether the owner has been away for at least <paramref name="inactiveAfter"/>. Unknown until their last
    /// login has been looked up, which happens in the background; until then they count as active.
    /// </summary>
    private bool IsOwnerInactive(Guid owner, DateTime now, TimeSpan inactiveAfter)
    {
        var userId = new NetUserId(owner);
        if (_player.TryGetSessionById(userId, out _))
        {
            _lastSeen[owner] = (now, _timing.CurTime);
            return false;
        }

        if (_lastSeen.TryGetValue(owner, out var seen) && _timing.CurTime - seen.CheckedAt < LastSeenCacheTime)
            return now - seen.LastSeen >= inactiveAfter;

        LookUpLastSeen(userId);
        return seen.LastSeen != default && now - seen.LastSeen >= inactiveAfter;
    }

    private async void LookUpLastSeen(NetUserId userId)
    {
        if (!_lastSeenPending.Add(userId.UserId))
            return;

        try
        {
            var record = await _db.GetPlayerRecordByUserId(userId);
            // Never seen (for example a test account) counts as just seen, so nothing is stored by mistake.
            var lastSeen = record?.LastSeenTime.UtcDateTime ?? DateTime.UtcNow;
            _lastSeen[userId.UserId] = (lastSeen, _timing.CurTime);
        }
        catch (Exception e)
        {
            Log.Error($"Looking up when {userId} was last seen failed: {e}");
        }
        finally
        {
            _lastSeenPending.Remove(userId.UserId);
        }
    }

    /// <summary>
    /// Records when an owner was last seen, for tests and admin use.
    /// </summary>
    public void SetLastSeen(Guid owner, DateTime lastSeen)
    {
        _lastSeen[owner] = (lastSeen, _timing.CurTime);
    }

    #endregion

    #region Storing

    /// <summary>
    /// Saves <paramref name="ship"/> to a storage file and removes it from the world.
    /// </summary>
    public bool TryStoreShip(EntityUid ship, ShipStorageReason reason, DateTime now, [NotNullWhen(false)] out string? problem)
    {
        if (!TryComp<ShipOwnershipComponent>(ship, out var ownership))
        {
            problem = "it has no owner";
            return false;
        }

        if (HasComp<FTLComponent>(ship))
        {
            problem = "it is in FTL";
            return false;
        }

        if (_shipyard.FoundOrganics(ship, GetEntityQuery<MobStateComponent>(), GetEntityQuery<TransformComponent>()) is { } aboard)
        {
            problem = $"{aboard} is aboard";
            return false;
        }

        var registry = GetRegistry(ship);
        var id = Guid.NewGuid();
        var path = new ResPath(_cfg.GetCVar(KesslerCVars.StoragePath)) / $"{id}.yml";

        var entry = new StoredShip
        {
            Id = id,
            File = path.ToString(),
            ShipName = Name(ship),
            OwnerUserId = ownership.OwnerUserId,
            OwnerSlot = ownership.OwnerSlot,
            OwnerProfileId = ownership.OwnerProfileId,
            OwnerName = ownership.OwnerName,
            Reason = reason,
            Debt = reason == ShipStorageReason.Impounded ? ownership.UpkeepDebt : 0,
            StoredAt = now,
        };

        try
        {
            if (!_persistenceSave.TrySaveGrid(ship, path))
            {
                problem = "saving it failed";
                return false;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Saving {ToPrettyString(ship)} to storage threw: {e}");
            problem = "saving it threw";
            return false;
        }

        // ID cards keep no deed for a ship that isn't in the world; the ship's own deed is stored with it.
        _shipyard.ClearDeedsForStoredShip(ship);

        registry.Comp.Ships.Add(entry);
        Log.Info($"Stored {ToPrettyString(ship)} of {entry.OwnerName} ({reason}) as {path}");
        NotifyOwner(entry.OwnerUserId, Loc.GetString(reason switch
        {
            ShipStorageReason.Impounded => "ship-storage-impounded",
            ShipStorageReason.Inactive => "ship-storage-inactive",
            _ => "ship-storage-admin",
        }, ("ship", entry.ShipName), ("debt", entry.Debt)));

        Del(ship);
        problem = null;
        return true;
    }

    private Entity<ShipStorageRegistryComponent> GetRegistry(EntityUid? near = null)
    {
        var query = EntityQueryEnumerator<ShipStorageRegistryComponent>();
        if (query.MoveNext(out var existing, out var comp))
            return (existing, comp);

        // The sector map is saved, so the list survives restarts. Without one (tests), use the ship's map.
        var holder = _map.GetMapOrInvalid(_ticker.DefaultMap);
        if (!holder.IsValid() && near != null)
            holder = Transform(near.Value).MapUid ?? EntityUid.Invalid;

        if (!holder.IsValid())
            throw new InvalidOperationException("No map to keep the ship storage list on");

        return (holder, EnsureComp<ShipStorageRegistryComponent>(holder));
    }

    public IReadOnlyList<StoredShip> GetStoredShips()
    {
        var query = EntityQueryEnumerator<ShipStorageRegistryComponent>();
        return query.MoveNext(out _, out var comp) ? comp.Ships : Array.Empty<StoredShip>();
    }

    #endregion

    #region Recalling

    private void OnShipyardVerbs(Entity<ShipyardConsoleComponent> console, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!_player.TryGetSessionByEntity(args.User, out var session))
            return;

        var slot = TryComp<BankAccountComponent>(args.User, out var bank) ? bank.CharacterSlot : -1;
        var user = args.User;
        foreach (var entry in GetStoredShips())
        {
            if (entry.OwnerUserId != session.UserId.UserId || entry.OwnerSlot != slot)
                continue;

            var id = entry.Id;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = entry.Debt > 0
                    ? Loc.GetString("ship-storage-verb-recall-debt", ("ship", entry.ShipName), ("debt", entry.Debt))
                    : Loc.GetString("ship-storage-verb-recall", ("ship", entry.ShipName)),
                Act = () => TryRecallAtConsole(console, user, id),
            });
        }
    }

    private void TryRecallAtConsole(Entity<ShipyardConsoleComponent> console, EntityUid user, Guid id)
    {
        var entry = GetStoredShips().FirstOrDefault(s => s.Id == id);
        if (entry == null)
            return;

        if (console.Comp.TargetIdSlot.ContainerSlot?.ContainedEntity is not { } idCard)
        {
            _popup.PopupEntity(Loc.GetString("ship-storage-recall-no-id"), console, user);
            return;
        }

        if (_station.GetOwningStation(console) is not { } station)
        {
            _popup.PopupEntity(Loc.GetString("ship-storage-recall-no-station"), console, user);
            return;
        }

        if (entry.Debt > 0 && !_bank.TryBankWithdraw(user, entry.Debt))
        {
            _popup.PopupEntity(Loc.GetString("ship-storage-recall-debt", ("debt", entry.Debt)), console, user);
            return;
        }

        if (!TryRecallShip(id, station, out var ship, out var problem))
        {
            Log.Error($"Recalling stored ship {id} for {ToPrettyString(user)} failed: {problem}");
            _popup.PopupEntity(Loc.GetString("ship-storage-recall-failed"), console, user);
            // The debt was paid but the ship didn't come back; give it back.
            if (entry.Debt > 0)
                _bank.TryBankDeposit(user, entry.Debt);
            return;
        }

        _shipyard.AssignRecalledShipDeed(idCard, ship.Value);
        _popup.PopupEntity(Loc.GetString("ship-storage-recalled", ("ship", entry.ShipName)), console, user);
    }

    /// <summary>
    /// Loads a stored ship back into the world and docks it at <paramref name="station"/>, or leaves it on the
    /// shipyard map when no station is given. The entry is removed; the file is kept.
    /// </summary>
    public bool TryRecallShip(Guid id, EntityUid? station, [NotNullWhen(true)] out EntityUid? ship, [NotNullWhen(false)] out string? problem)
    {
        ship = null;
        var registry = GetRegistry();
        var entry = registry.Comp.Ships.FirstOrDefault(s => s.Id == id);
        if (entry == null)
        {
            problem = "no such stored ship";
            return false;
        }

        var path = new ResPath(entry.File);
        if (!_res.UserData.Exists(path))
        {
            problem = $"its file {path} is missing";
            return false;
        }

        _shipyard.SetupShipyardIfNeeded();
        if (_shipyard.ShipyardMap is not { } shipyardMap)
        {
            problem = "there is no shipyard map";
            return false;
        }

        if (!_persistenceLoad.TryLoadStoredGrid(path, shipyardMap, new Vector2(500f, 1f), out var grid))
        {
            problem = $"loading {path} failed";
            return false;
        }

        ship = grid.Value.Owner;
        registry.Comp.Ships.Remove(entry);

        if (TryComp<ShipOwnershipComponent>(ship, out var ownership))
            _ownership.ResetAfterRecall((ship.Value, ownership), DateTime.UtcNow);

        if (station != null
            && TryComp<StationDataComponent>(station, out var stationData)
            && _station.GetLargestGrid((station.Value, stationData)) is { } target
            && TryComp<ShuttleComponent>(ship, out var shuttle))
        {
            _shuttle.TryFTLDock(ship.Value, shuttle, target);
        }

        Log.Info($"Recalled {ToPrettyString(ship)} of {entry.OwnerName} from {path}");
        problem = null;
        return true;
    }

    #endregion

    private void NotifyOwner(Guid owner, string message)
    {
        if (_player.TryGetSessionById(new NetUserId(owner), out var session))
            _chat.DispatchServerMessage(session, message);
    }
}
