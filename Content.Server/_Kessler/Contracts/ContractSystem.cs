using System.Linq;
using Content.Server._Kessler.Economy;
using Content.Server._Kessler.Reputation;
using Content.Server._NF.Bank;
using Content.Server._NF.Cargo.Components;
using Content.Server._NF.Cargo.Systems;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.CCVar;
using Content.Shared._Kessler.Contracts;
using Content.Shared._Kessler.Reputation;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Kessler.Contracts;

/// <summary>
/// The contract board's procurement contracts (docs/kessler/design/departments.md, 5.2). Every station with a pallet
/// console posts contracts for goods it wants; a character accepts one in the Contracts PDA program and completes it
/// by selling the goods on that station's pallets. The pallet pays for the goods as usual; the contract pays its reward
/// into the holder's bank and gives reputation on its track. Contracts that run out of time drop without penalty.
/// </summary>
public sealed class ContractSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly StationNeedsSystem _needs = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public const int MaxHeld = 2;
    public const int BaseOpen = 2;
    public const int PlayersPerExtraOpen = 5;
    public const int MaxOpen = 5;
    public static readonly TimeSpan PostInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OpenLifetime = TimeSpan.FromHours(3);

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NFEntitySoldEvent>(OnEntitiesSold);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;
        _nextRefresh = _timing.CurTime + RefreshInterval;

        if (_cfg.GetCVar(KesslerCVars.ContractsEnabled))
            Refresh(DateTime.UtcNow);
    }

    /// <summary>
    /// Stations that post contracts: those with a pallet console to deliver to.
    /// </summary>
    public HashSet<EntityUid> IssuerStations()
    {
        var stations = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<NFCargoPalletConsoleComponent>();
        while (query.MoveNext(out var console, out _))
        {
            if (_station.GetOwningStation(console) is { } station)
                stations.Add(station);
        }

        return stations;
    }

    /// <summary>
    /// Drops expired contracts and posts new ones where a station has fewer open than it should.
    /// </summary>
    public void Refresh(DateTime now)
    {
        var target = Math.Min(MaxOpen, BaseOpen + _players.PlayerCount / PlayersPerExtraOpen);
        foreach (var station in IssuerStations())
        {
            var board = EnsureComp<StationContractsComponent>(station);
            board.Contracts.RemoveAll(c => c.Expires <= now);

            PostForLowNeeds((station, board), now);

            if (now < board.NextPost || board.Contracts.Count(c => !c.Held) >= target)
                continue;

            Post((station, board), now);
            board.NextPost = now + PostInterval;
        }
    }

    /// <summary>
    /// A station short of a stock posts a contract for it straight away, if it has none out for it already.
    /// </summary>
    private void PostForLowNeeds(Entity<StationContractsComponent> station, DateTime now)
    {
        foreach (var need in _needs.LowNeeds(station, now))
        {
            if (need.Contracts.Count == 0
                || station.Comp.Contracts.Count(c => !c.Held) >= MaxOpen
                || station.Comp.Contracts.Any(c => need.Contracts.Contains(c.Template)))
            {
                continue;
            }

            Post(station, now, _random.Pick(need.Contracts));
        }
    }

    /// <summary>
    /// Posts a new open contract at a station.
    /// </summary>
    public ProcurementContract? Post(Entity<StationContractsComponent> station, DateTime now, ProtoId<ProcurementContractPrototype>? template = null)
    {
        ProcurementContractPrototype? proto;
        if (template != null)
            proto = _proto.Index(template.Value);
        else if ((proto = PickTemplate()) == null)
            return null;

        var amount = _random.Next(proto.MinAmount, proto.MaxAmount + 1);
        var minutes = Math.Max(5, (int) Math.Round(amount * proto.MinutesPerUnit));
        var contract = new ProcurementContract
        {
            Id = station.Comp.NextId++,
            Template = proto.ID,
            Amount = amount,
            Minutes = minutes,
            Reward = minutes * _cfg.GetCVar(KesslerCVars.ContractRate),
            Expires = now + OpenLifetime,
        };
        station.Comp.Contracts.Add(contract);
        return contract;
    }

    private ProcurementContractPrototype? PickTemplate()
    {
        var templates = _proto.EnumeratePrototypes<ProcurementContractPrototype>().Where(t => t.Weight > 0).ToList();
        if (templates.Count == 0)
            return null;

        var roll = _random.NextFloat() * templates.Sum(t => t.Weight);
        foreach (var template in templates)
        {
            roll -= template.Weight;
            if (roll <= 0)
                return template;
        }

        return templates[^1];
    }

    /// <summary>
    /// Who a character is for contracts: their user and character slot. Null if they aren't a player character.
    /// </summary>
    private (string User, int Slot)? Holder(EntityUid actor)
    {
        if (!TryComp<ActorComponent>(actor, out var actorComp)
            || !TryComp<BankAccountComponent>(actor, out var bank)
            || bank.CharacterSlot < 0)
        {
            return null;
        }

        return (actorComp.PlayerSession.UserId.UserId.ToString(), bank.CharacterSlot);
    }

    private static bool IsHeldBy(ProcurementContract contract, (string User, int Slot) holder)
    {
        return contract.HolderUser == holder.User && contract.HolderSlot == holder.Slot;
    }

    private IEnumerable<(EntityUid Station, StationContractsComponent Board, ProcurementContract Contract)> AllContracts()
    {
        var query = EntityQueryEnumerator<StationContractsComponent>();
        while (query.MoveNext(out var station, out var board))
        {
            foreach (var contract in board.Contracts)
            {
                yield return (station, board, contract);
            }
        }
    }

    /// <summary>
    /// Takes an open contract. Returns why not as a locale id, or null on success.
    /// </summary>
    public string? Accept(EntityUid actor, EntityUid station, int id, DateTime now)
    {
        if (Holder(actor) is not { } holder)
            return "kessler-contracts-no-character";

        if (AllContracts().Count(c => IsHeldBy(c.Contract, holder)) >= MaxHeld)
            return "kessler-contracts-too-many";

        if (!TryComp<StationContractsComponent>(station, out var board)
            || board.Contracts.FirstOrDefault(c => c.Id == id) is not { Held: false } contract
            || contract.Expires <= now
            || !_proto.TryIndex(contract.Template, out var proto))
        {
            return "kessler-contracts-gone";
        }

        contract.HolderUser = holder.User;
        contract.HolderSlot = holder.Slot;
        contract.HolderName = Name(actor);
        contract.Expires = now + TimeSpan.FromHours(proto.Hours);
        return null;
    }

    /// <summary>
    /// Gives up a held contract; it's withdrawn, with no penalty.
    /// </summary>
    public bool Abandon(EntityUid actor, EntityUid station, int id)
    {
        if (Holder(actor) is not { } holder || !TryComp<StationContractsComponent>(station, out var board))
            return false;

        return board.Contracts.RemoveAll(c => c.Id == id && IsHeldBy(c, holder)) > 0;
    }

    private void OnEntitiesSold(ref NFEntitySoldEvent ev)
    {
        if (ev.Seller is not { } seller
            || Holder(seller) is not { } holder
            || _station.GetOwningStation(ev.Grid) is not { } station
            || !TryComp<StationContractsComponent>(station, out var board))
        {
            return;
        }

        var held = board.Contracts.Where(c => IsHeldBy(c, holder)).OrderBy(c => c.Id).ToList();
        if (held.Count == 0)
            return;

        foreach (var sold in ev.Sold)
        {
            Deliver(sold, held);
        }

        foreach (var contract in held)
        {
            if (!_proto.TryIndex(contract.Template, out var proto))
                continue;

            if (contract.Delivered >= contract.Amount)
            {
                board.Contracts.Remove(contract);
                _bank.TryBankDeposit(seller, contract.Reward);
                _reputation.TryAward(seller, proto.Track, contract.Minutes, Loc.GetString("kessler-reputation-reason-contract"));
                _popup.PopupEntity(Loc.GetString("kessler-contracts-complete",
                    ("item", Loc.GetString(proto.Item)), ("reward", contract.Reward)), seller, seller, PopupType.Medium);
            }
            else if (contract.Delivered > 0)
            {
                _popup.PopupEntity(Loc.GetString("kessler-contracts-progress",
                    ("item", Loc.GetString(proto.Item)), ("delivered", contract.Delivered), ("amount", contract.Amount)), seller, seller);
            }
        }
    }

    /// <summary>
    /// Counts a sold entity, and anything inside it, towards the first held contracts it matches.
    /// </summary>
    private void Deliver(EntityUid uid, List<ProcurementContract> held)
    {
        var units = TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1;
        foreach (var contract in held)
        {
            if (units <= 0)
                break;

            if (contract.Delivered >= contract.Amount
                || !_proto.TryIndex(contract.Template, out var proto)
                || !Matches(uid, proto))
            {
                continue;
            }

            var used = Math.Min(units, contract.Amount - contract.Delivered);
            contract.Delivered += used;
            units -= used;
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                Deliver(child, held);
            }
        }
    }

    public bool Matches(EntityUid uid, ProcurementContractPrototype proto)
    {
        return proto.Matches(uid, EntityManager, _whitelist);
    }

    /// <summary>
    /// What the Contracts program shows <paramref name="viewer"/>.
    /// </summary>
    public ContractsUiState BuildState(EntityUid? viewer, DateTime now)
    {
        var holder = viewer is { } v ? Holder(v) : null;
        var held = new List<ContractEntry>();
        var open = new List<ContractEntry>();

        foreach (var (station, _, contract) in AllContracts())
        {
            if (contract.Expires <= now || !_proto.TryIndex(contract.Template, out var proto))
                continue;

            var entry = new ContractEntry
            {
                Station = GetNetEntity(station),
                Id = contract.Id,
                StationName = Name(station),
                Item = Loc.GetString(proto.Item),
                Amount = contract.Amount,
                Delivered = contract.Delivered,
                Reward = contract.Reward,
                Reputation = contract.Minutes,
                Track = _proto.TryIndex<ReputationTrackPrototype>(proto.Track, out var track) ? Loc.GetString(track.Name) : proto.Track,
                Remaining = contract.Expires - now,
                TimeToDeliver = TimeSpan.FromHours(proto.Hours),
            };

            if (!contract.Held)
                open.Add(entry);
            else if (holder is { } h && IsHeldBy(contract, h))
                held.Add(entry);
        }

        open.Sort((a, b) => string.Compare(a.StationName, b.StationName, StringComparison.CurrentCultureIgnoreCase));
        return new ContractsUiState(held, open, MaxHeld, holder == null ? "kessler-contracts-no-character" : null);
    }
}
