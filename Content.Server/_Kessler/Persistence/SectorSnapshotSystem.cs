using System.Security.Cryptography;
using System.Text;
using Content.Server.Power.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Robust.Shared.Map.Components;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Builds <see cref="SectorSnapshot"/>s of entity trees, for checking that a save/load round trip keeps state.
/// </summary>
public sealed class SectorSnapshotSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<BatteryComponent> _batteryQuery;
    private EntityQuery<DamageableComponent> _damageQuery;
    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<PaperComponent> _paperQuery;
    private EntityQuery<SolutionComponent> _solutionQuery;
    private EntityQuery<StackComponent> _stackQuery;

    public override void Initialize()
    {
        base.Initialize();

        _batteryQuery = GetEntityQuery<BatteryComponent>();
        _damageQuery = GetEntityQuery<DamageableComponent>();
        _mapQuery = GetEntityQuery<MapComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _paperQuery = GetEntityQuery<PaperComponent>();
        _solutionQuery = GetEntityQuery<SolutionComponent>();
        _stackQuery = GetEntityQuery<StackComponent>();
    }

    /// <summary>
    /// Collects the given root entities and all of their transform descendants (which includes container contents).
    /// </summary>
    public HashSet<EntityUid> CollectTree(IEnumerable<EntityUid> roots)
    {
        var result = new HashSet<EntityUid>();
        var stack = new Stack<EntityUid>();

        foreach (var root in roots)
        {
            if (Exists(root))
                stack.Push(root);
        }

        while (stack.TryPop(out var uid))
        {
            if (!result.Add(uid))
                continue;

            var enumerator = Transform(uid).ChildEnumerator;
            while (enumerator.MoveNext(out var child))
            {
                stack.Push(child);
            }
        }

        return result;
    }

    public SectorSnapshot Snapshot(IEnumerable<EntityUid> entities)
    {
        var snapshot = new SectorSnapshot();

        foreach (var uid in entities)
        {
            var meta = MetaData(uid);
            var xform = Transform(uid);
            var proto = PrototypeKey(uid);

            snapshot.EntityCount++;
            Add(snapshot.PrototypeCounts, proto, 1);

            if ((meta.Flags & MetaDataFlags.InContainer) != 0)
                snapshot.ContainedCount++;

            if (_mapQuery.HasComp(uid))
                snapshot.MapCount++;
            else if (!xform.ParentUid.IsValid())
                snapshot.NullspaceCount++;

            if (_gridQuery.TryComp(uid, out var grid))
            {
                snapshot.GridCount++;
                var tiles = _map.GetAllTilesEnumerator(uid, grid);
                while (tiles.MoveNext(out _))
                {
                    snapshot.TileCount++;
                }
            }

            if (_batteryQuery.TryComp(uid, out var battery))
            {
                Add(snapshot.BatteryCharge, proto, battery.CurrentCharge);
                Add(snapshot.BatteryMaxCharge, proto, battery.MaxCharge);
            }

            // Solutions are separate entities stored in a container on the entity that owns them.
            if (_solutionQuery.TryComp(uid, out var solution))
            {
                Add(snapshot.SolutionVolume, PrototypeKey(xform.ParentUid), solution.Solution.Volume.Double());
                foreach (var reagent in solution.Solution.Contents)
                {
                    Add(snapshot.ReagentQuantity, reagent.Reagent.Prototype, reagent.Quantity.Double());
                }
            }

            if (_stackQuery.TryComp(uid, out var stack))
                Add(snapshot.StackCounts, stack.StackTypeId, stack.Count);

            if (_damageQuery.TryComp(uid, out var damage) && damage.TotalDamage > 0)
                Add(snapshot.Damage, proto, damage.TotalDamage.Double());

            if (_paperQuery.TryComp(uid, out var paper))
            {
                snapshot.PaperCount++;
                var hash = HashPaper(paper);
                snapshot.PaperHashes[hash] = snapshot.PaperHashes.GetValueOrDefault(hash) + 1;
            }
        }

        return snapshot;
    }

    public string PrototypeKey(EntityUid uid)
    {
        if (!TryComp(uid, out MetaDataComponent? meta))
            return SectorSnapshot.NoPrototype;

        return meta.EntityPrototype?.ID ?? SectorSnapshot.NoPrototype;
    }

    private static void Add(Dictionary<string, double> dict, string key, double value)
    {
        dict[key] = dict.GetValueOrDefault(key) + value;
    }

    private static string HashPaper(PaperComponent paper)
    {
        var builder = new StringBuilder();
        builder.Append(paper.Content).Append('\0');
        builder.Append(paper.StampState).Append('\0');

        foreach (var stamp in paper.StampedBy)
        {
            builder.Append(stamp.StampedName).Append('|')
                .Append(stamp.StampedColor.ToHex()).Append('|')
                .Append(stamp.Type).Append('|')
                .Append(stamp.Reapply).Append('\0');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes, 0, 8);
    }
}
