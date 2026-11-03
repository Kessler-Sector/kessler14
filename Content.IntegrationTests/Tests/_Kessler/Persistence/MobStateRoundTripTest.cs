using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Shared.Actions.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Mobs in a state with extra actions (critical, dead) must load cleanly. Recreating those actions during the load used
/// to delete the saved ones while the action container still listed them, which failed the whole save.
/// </summary>
[TestFixture]
public sealed class MobStateRoundTripTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [Test]
    public async Task CriticalMobLoadsCleanly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid loaded = default;
        await server.WaitPost(() =>
        {
            var mouse = entManager.SpawnEntity("MobMouse", testMap.GridCoords);
            // Damage, not just the state: the state is recomputed from damage on load.
            Assert.That(entManager.System<MobThresholdSystem>().TryGetThresholdForState(mouse, MobState.Critical, out var threshold));
            var blunt = server.ProtoMan.Index<DamageTypePrototype>(Blunt);
            entManager.System<DamageableSystem>().TryChangeDamage(mouse, new DamageSpecifier(blunt, threshold!.Value), true);
            Assert.That(entManager.System<MobStateSystem>().IsCritical(mouse));
            var granted = entManager.GetComponent<MobStateActionsComponent>(mouse).GrantedActions.Count;
            Assert.That(granted, Is.GreaterThan(0), "A critical mouse has no state actions to test with");

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-critical-mouse.yml"));
            loaded = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<MobStateActionsComponent>(uid));
        });

        // Actions replaced during the load are deleted at the end of the tick.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // The granted actions must match the loaded state.
            var stateActions = entManager.GetComponent<MobStateActionsComponent>(loaded);
            var state = entManager.GetComponent<MobStateComponent>(loaded).CurrentState;
            var expected = stateActions.Actions.TryGetValue(state, out var protos) ? protos.Count : 0;
            var container = entManager.GetComponent<ActionsContainerComponent>(loaded).Container;
            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(MobState.Critical));
                Assert.That(expected, Is.GreaterThan(0), "A critical mouse has no state actions to test with");
                Assert.That(stateActions.GrantedActions, Has.Count.EqualTo(expected), $"Wrong actions for {state}");
                Assert.That(stateActions.GrantedActions.All(entManager.EntityExists), "A state action was deleted");
                Assert.That(stateActions.GrantedActions.All(container.ContainedEntities.Contains),
                    "A state action is not in the action container");
                Assert.That(container.ContainedEntities.All(entManager.EntityExists),
                    "The action container lists a deleted action");
            });
        });

        await pair.CleanReturnAsync();
    }
}
