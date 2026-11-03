using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Damage must survive a persistent save and load. It used to be read-only, so every injured body and damaged wall
/// loaded with zero damage, and downed mobs loaded alive.
/// </summary>
[TestFixture]
public sealed class DamageRoundTripTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [Test]
    public async Task CriticalMobKeepsDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();

        var testMap = await pair.CreateTestMap();

        FixedPoint2 total = default;
        Dictionary<string, FixedPoint2> damage = null;
        EntityUid loaded = default;
        await server.WaitPost(() =>
        {
            var mouse = entManager.SpawnEntity("MobMouse", testMap.GridCoords);
            Assert.That(entManager.System<MobThresholdSystem>().TryGetThresholdForState(mouse, MobState.Critical, out var threshold));
            var blunt = server.ProtoMan.Index<DamageTypePrototype>(Blunt);
            entManager.System<DamageableSystem>().TryChangeDamage(mouse, new DamageSpecifier(blunt, threshold!.Value), true);
            Assert.That(entManager.System<MobStateSystem>().IsCritical(mouse));

            var damageable = entManager.GetComponent<DamageableComponent>(mouse);
            total = damageable.TotalDamage;
            damage = new Dictionary<string, FixedPoint2>(damageable.Damage.DamageDict);

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-damaged-mouse.yml"));
            loaded = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<MobStateComponent>(uid));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var damageable = entManager.GetComponent<DamageableComponent>(loaded);
            Assert.Multiple(() =>
            {
                Assert.That(damageable.TotalDamage, Is.EqualTo(total));
                // Zero entries are not saved, so this also checks they are filled in again for supported types.
                Assert.That(damageable.Damage.DamageDict, Is.EquivalentTo(damage));
                Assert.That(entManager.GetComponent<MobStateComponent>(loaded).CurrentState, Is.EqualTo(MobState.Critical));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Ordinary map saves must not grow: undamaged entities write no damage at all.
    /// </summary>
    [Test]
    public async Task UndamagedEntitiesWriteNoDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // Map saves skip mobs (save: false), so use structures.
            var wall = entManager.SpawnEntity("WallSolid", testMap.GridCoords);
            entManager.SpawnEntity("Window", testMap.GridCoords);

            var (node, _) = entManager.System<MapLoaderSystem>().SerializeEntitiesRecursive([testMap.MapUid]);
            Assert.That(DamageableData(node).Where(comp => comp.Has("damage")), Is.Empty);

            // Sanity check that the search finds damage once there is some.
            var blunt = server.ProtoMan.Index<DamageTypePrototype>(Blunt);
            entManager.System<DamageableSystem>().TryChangeDamage(wall, new DamageSpecifier(blunt, 5), true);
            (node, _) = entManager.System<MapLoaderSystem>().SerializeEntitiesRecursive([testMap.MapUid]);
            Assert.That(DamageableData(node).Count(comp => comp.Has("damage")), Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<MappingDataNode> DamageableData(DataNode node)
    {
        switch (node)
        {
            case MappingDataNode mapping:
                if (mapping.TryGet<ValueDataNode>("type", out var type) && type.Value == "Damageable")
                    yield return mapping;
                foreach (var child in mapping.Children.Values)
                {
                    foreach (var found in DamageableData(child))
                        yield return found;
                }
                break;
            case SequenceDataNode sequence:
                foreach (var child in sequence.Sequence)
                {
                    foreach (var found in DamageableData(child))
                        yield return found;
                }
                break;
        }
    }
}
