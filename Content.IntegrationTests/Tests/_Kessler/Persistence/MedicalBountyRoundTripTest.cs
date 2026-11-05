using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.Medical.Components;
using Content.Shared._NF.Medical.Prototypes;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// A medical bounty corpse applies its damage and reagents once, when the bounty starts. A loaded corpse must keep
/// what it was saved with instead of getting a second dose, and must keep its bounty and value.
/// </summary>
[TestFixture]
public sealed class MedicalBountyRoundTripTest
{
    // Has both damage sets and reagents.
    private const string Bounty = "SpiderAttackMild";
    private const string Reagent = "Mechanotoxin";

    [Test]
    public async Task BountyIsNotAppliedAgain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();
        var solutions = entManager.System<SharedSolutionContainerSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var corpse = entManager.SpawnEntity("MobHuman", testMap.GridCoords);
            entManager.AddComponent(corpse, new MedicalBountyComponent
            {
                Bounty = Bounty,
            });

            var bounty = entManager.GetComponent<MedicalBountyComponent>(corpse);
            Assert.That(bounty.BountyInitialized, "the bounty was applied when the component started");
            var value = bounty.MaxBountyValue;
            var damage = new Dictionary<string, FixedPoint2>(entManager.GetComponent<DamageableComponent>(corpse).Damage.DamageDict);
            var reagent = ChemicalQuantity(entManager, solutions, corpse);
            Assert.That(reagent, Is.GreaterThan(FixedPoint2.Zero), "the bounty injected its reagent");

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-medical-bounty.yml"));
            var loaded = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<MedicalBountyComponent>(uid));

            var loadedBounty = entManager.GetComponent<MedicalBountyComponent>(loaded);
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<DamageableComponent>(loaded).Damage.DamageDict, Is.EquivalentTo(damage),
                    "damage is not applied a second time");
                Assert.That(ChemicalQuantity(entManager, solutions, loaded), Is.EqualTo(reagent),
                    "reagents are not injected a second time");
                Assert.That(loadedBounty.Bounty, Is.EqualTo(new ProtoId<MedicalBountyPrototype>(Bounty)));
                Assert.That(loadedBounty.MaxBountyValue, Is.EqualTo(value));
                Assert.That(loadedBounty.BountyInitialized);
            });
        });

        await pair.CleanReturnAsync();
    }

    private static FixedPoint2 ChemicalQuantity(IEntityManager entManager, SharedSolutionContainerSystem solutions, EntityUid uid)
    {
        var name = entManager.GetComponent<BloodstreamComponent>(uid).ChemicalSolutionName;
        Assert.That(solutions.TryGetSolution(uid, name, out _, out var solution));
        return solution!.GetTotalPrototypeQuantity(Reagent);
    }
}
