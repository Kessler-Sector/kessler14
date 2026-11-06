using System.Linq;
using Content.Server._Kessler.Reputation;
using Content.Server._NF.Bank;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Kessler.CCVar;
using Content.Shared._Kessler.WorkOrders;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Kessler.WorkOrders;

/// <summary>
/// Generated station jobs, so station work pays at low population (docs/kessler/design/progression.md, 4.6). Cleaning
/// orders spill messes around the station and pay whoever cleans each one; delivery orders ask for a dish or drink and
/// pay whoever hands it in at the board. Pay goes to the worker, and each order gives reputation.
/// </summary>
public sealed class WorkOrderSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How close a player has to be to a mess when it disappears to be paid for cleaning it.
    /// </summary>
    private const float CleanerRange = 3f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WorkOrderBoardComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WorkOrderBoardComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WorkOrderMessComponent, EntityTerminatingEvent>(OnMessTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WorkOrderBoardComponent>();
        while (query.MoveNext(out var uid, out var board))
        {
            if (_timing.CurTime < board.NextUpdate)
                continue;

            board.NextUpdate = _timing.CurTime + UpdateInterval;
            UpdateBoard((uid, board), DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Drops expired orders and posts new ones. Public so tests can run it at a chosen time.
    /// </summary>
    public void UpdateBoard(Entity<WorkOrderBoardComponent> board, DateTime now)
    {
        var changed = board.Comp.Orders.RemoveAll(o => o.Expires <= now) > 0;

        var target = Math.Min(board.Comp.MaxOpen,
            board.Comp.BaseOpen + _players.PlayerCount / Math.Max(1, board.Comp.PlayersPerExtraOrder));

        if (board.Comp.Orders.Count < board.Comp.BaseOpen
            || board.Comp.Orders.Count < target && now >= board.Comp.NextGenerate)
        {
            if (TryPost(board, now))
            {
                board.Comp.NextGenerate = now + board.Comp.Interval;
                changed = true;
            }
        }

        if (changed)
            UpdateUi(board);
    }

    private bool TryPost(Entity<WorkOrderBoardComponent> board, DateTime now)
    {
        var candidates = _proto.EnumeratePrototypes<WorkOrderPrototype>()
            .Where(p => board.Comp.Allowed.Count == 0 || board.Comp.Allowed.Contains(p.ID))
            .ToList();
        if (candidates.Count == 0)
            return false;

        var proto = PickWeighted(candidates);
        var order = new WorkOrder
        {
            Id = board.Comp.NextId++,
            Prototype = proto.ID,
            Expires = now + board.Comp.Lifetime,
        };

        switch (proto.Kind)
        {
            case WorkOrderKind.Clean:
                if (!TryMakeMesses(board, proto, order))
                    return false;
                break;
            case WorkOrderKind.Deliver:
                var choices = proto.Items.Select(i => (Id: (string) i, Reagent: false))
                    .Concat(proto.Reagents.Select(r => (Id: r, Reagent: true)))
                    .ToList();
                if (choices.Count == 0)
                    return false;
                var choice = _random.Pick(choices);
                order.Target = choice.Id;
                order.TargetIsReagent = choice.Reagent;
                break;
        }

        board.Comp.Orders.Add(order);
        return true;
    }

    private WorkOrderPrototype PickWeighted(List<WorkOrderPrototype> candidates)
    {
        var total = candidates.Sum(c => Math.Max(0f, c.Weight));
        var roll = _random.NextFloat() * total;
        foreach (var candidate in candidates)
        {
            roll -= Math.Max(0f, candidate.Weight);
            if (roll <= 0)
                return candidate;
        }

        return candidates[^1];
    }

    private bool TryMakeMesses(Entity<WorkOrderBoardComponent> board, WorkOrderPrototype proto, WorkOrder order)
    {
        if (proto.MessReagents.Count == 0
            || Transform(board).GridUid is not { } gridUid
            || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var tiles = _map.GetAllTiles(gridUid, grid)
            .Where(t => !_turf.IsTileBlocked(t, CollisionGroup.Impassable))
            .ToList();
        if (tiles.Count == 0)
            return false;

        var count = _random.Next(proto.MessMin, proto.MessMax + 1);
        for (var i = 0; i < count; i++)
        {
            var tile = _random.Pick(tiles);
            var solution = new Solution(_random.Pick(proto.MessReagents), proto.MessVolume);
            if (!_puddle.TrySpillAt(tile, solution, out var puddle, sound: false))
                continue;

            var mess = EnsureComp<WorkOrderMessComponent>(puddle);
            if (mess.Board != null)
                continue; // An earlier mess on the same tile; it already counts.

            mess.Board = board;
            mess.OrderId = order.Id;
            order.MessesTotal++;
        }

        order.MessesLeft = order.MessesTotal;
        return order.MessesTotal > 0;
    }

    private void OnMessTerminating(Entity<WorkOrderMessComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Board is not { } boardUid
            || TerminatingOrDeleted(boardUid)
            || !TryComp<WorkOrderBoardComponent>(boardUid, out var board))
        {
            return;
        }

        var order = board.Orders.FirstOrDefault(o => o.Id == ent.Comp.OrderId);
        if (order == null)
            return;

        order.MessesLeft--;
        if (order.MessesLeft <= 0)
            board.Orders.Remove(order);

        // Whoever is standing at the mess when it disappears cleaned it up.
        if (FindCleaner(ent) is { } cleaner && _proto.TryIndex(order.Prototype, out var proto))
        {
            var total = Math.Max(1, order.MessesTotal);
            Pay(cleaner, proto, GetPayout(proto) / total, Math.Max(1, proto.Minutes / total));
        }

        UpdateUi((boardUid, board));
    }

    private EntityUid? FindCleaner(EntityUid mess)
    {
        var xform = Transform(mess);
        // A mess deleted along with its station or map (a round ending) wasn't cleaned by anyone.
        if (xform.MapUid is not { } map || TerminatingOrDeleted(map) || TerminatingOrDeleted(xform.GridUid))
            return null;

        var position = _transform.GetWorldPosition(xform);
        EntityUid? best = null;
        var bestDistance = CleanerRange;
        var query = EntityQueryEnumerator<ActorComponent, BankAccountComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var playerXform))
        {
            if (playerXform.MapUid != map)
                continue;

            var distance = (_transform.GetWorldPosition(playerXform) - position).Length();
            if (distance > bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    private void OnInteractUsing(Entity<WorkOrderBoardComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        foreach (var order in ent.Comp.Orders)
        {
            if (order.Target == null
                || !_proto.TryIndex(order.Prototype, out var proto)
                || proto.Kind != WorkOrderKind.Deliver
                || !Matches(args.Used, order, proto))
            {
                continue;
            }

            args.Handled = true;
            ent.Comp.Orders.Remove(order);
            QueueDel(args.Used);
            Pay(args.User, proto, GetPayout(proto), proto.Minutes);
            UpdateUi(ent);
            return;
        }
    }

    private bool Matches(EntityUid item, WorkOrder order, WorkOrderPrototype proto)
    {
        if (order.TargetIsReagent)
        {
            foreach (var (_, soln) in _solution.EnumerateSolutions(item))
            {
                if (soln.Comp.Solution.GetTotalPrototypeQuantity(order.Target!) >= proto.ReagentAmount)
                    return true;
            }

            return false;
        }

        if (MetaData(item).EntityPrototype is not { } itemProto)
            return false;

        return _proto.EnumerateParents<EntityPrototype>(itemProto.ID, includeSelf: true).Any(p => p.ID == order.Target);
    }

    private int GetPayout(WorkOrderPrototype proto)
    {
        return proto.Minutes * _cfg.GetCVar(KesslerCVars.WorkOrderRate);
    }

    private void Pay(EntityUid worker, WorkOrderPrototype proto, int amount, int reputation)
    {
        if (amount > 0 && _bank.TryBankDeposit(worker, amount))
            _popup.PopupEntity(Loc.GetString("kessler-work-order-paid", ("amount", amount)), worker, worker);

        _reputation.TryAward(worker, proto.Track, reputation, Loc.GetString("kessler-work-order-reason"));
    }

    private void OnUiOpened(Entity<WorkOrderBoardComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<WorkOrderBoardComponent> board)
    {
        if (!_ui.IsUiOpen(board.Owner, WorkOrderBoardUiKey.Key))
            return;

        var now = DateTime.UtcNow;
        var entries = new List<WorkOrderBoardEntry>();
        foreach (var order in board.Comp.Orders.OrderBy(o => o.Expires))
        {
            if (!_proto.TryIndex(order.Prototype, out var proto))
                continue;

            var item = order.Target == null ? string.Empty : TargetName(order);
            entries.Add(new WorkOrderBoardEntry
            {
                Title = Loc.GetString(proto.Title, ("item", item)),
                Description = Loc.GetString(proto.Description, ("item", item), ("amount", (int) proto.ReagentAmount)),
                Payout = GetPayout(proto),
                Reputation = proto.Minutes,
                Track = _proto.TryIndex(proto.Track, out var track) ? Loc.GetString(track.Name) : proto.Track.Id,
                MinutesLeft = Math.Max(0, (int) (order.Expires - now).TotalMinutes),
                Remaining = order.MessesLeft,
                Total = order.MessesTotal,
            });
        }

        _ui.SetUiState(board.Owner, WorkOrderBoardUiKey.Key, new WorkOrderBoardUiState(entries));
    }

    private string TargetName(WorkOrder order)
    {
        if (order.TargetIsReagent)
            return _proto.TryIndex<ReagentPrototype>(order.Target!, out var reagent) ? reagent.LocalizedName : order.Target!;

        return _proto.TryIndex<EntityPrototype>(order.Target!, out var proto) ? proto.Name : order.Target!;
    }
}
