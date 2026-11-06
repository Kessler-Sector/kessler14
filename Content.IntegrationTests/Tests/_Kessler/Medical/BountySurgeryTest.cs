#pragma warning disable RA0002 // Tests read component state directly.
using System.Linq;
using Content.Server._Kessler.Medical;
using Content.Shared._Kessler.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Medical;

/// <summary>
/// Medical bounty corpses can arrive with limbs detached; the limbs stay with the corpse, and each one attached again
/// earns back its bonus.
/// </summary>
[TestFixture]
public sealed class BountySurgeryTest
{
    [Test]
    public async Task DetachedLimbsLoseTheirBonusUntilAttached()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var surgery = entMan.System<BountySurgerySystem>();
        var body = entMan.System<SharedBodySystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var chance = cfg.GetCVar(KesslerCVars.MedicalBountyLimbChance);
            cfg.SetCVar(KesslerCVars.MedicalBountyLimbChance, 1f);
            try
            {
                // A corpse in a crate, like one in a rescue pod.
                var crate = entMan.SpawnEntity("CrateGenericSteel", testMap.GridCoords);
                var corpse = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
                Assert.That(containers.TryGetContainer(crate, "entity_storage", out var storage));
                Assert.That(containers.Insert(corpse, storage!));

                int Limbs() => body.GetBodyChildrenOfType(corpse, BodyPartType.Arm).Count()
                               + body.GetBodyChildrenOfType(corpse, BodyPartType.Leg).Count();
                var before = Limbs();

                var bonus = surgery.ApplyMissingLimbs(corpse);
                var bonusPerLimb = cfg.GetCVar(KesslerCVars.MedicalBountyLimbBonus);
                Assert.That(bonus, Is.GreaterThan(0), "with a chance of 1 some limbs come off");
                var removed = bonus / bonusPerLimb;
                Assert.That(Limbs(), Is.EqualTo(before - removed));
                Assert.That(surgery.MissingLimbPenalty(corpse), Is.EqualTo(bonus), "missing limbs cost their bonus");

                // The limbs are in the crate with the corpse.
                var loose = storage!.ContainedEntities
                    .Where(e => entMan.TryGetComponent<BodyPartComponent>(e, out var p) && p.Body == null
                                && p.PartType is BodyPartType.Arm or BodyPartType.Leg)
                    .ToList();
                Assert.That(loose, Has.Count.EqualTo(removed), "the detached limbs travel with the corpse");

                // Attach one again.
                var limb = loose[0];
                var limbComp = entMan.GetComponent<BodyPartComponent>(limb);
                var torso = body.GetBodyChildrenOfType(corpse, BodyPartType.Torso).First().Id;
                Assert.That(containers.Remove(limb, storage));
                Assert.That(body.AttachPart(torso, body.GetSlotFromBodyPart(limbComp), limb), "the limb goes back on");
                Assert.That(surgery.MissingLimbPenalty(corpse), Is.EqualTo(bonus - bonusPerLimb), "an attached limb earns its bonus back");
            }
            finally
            {
                cfg.SetCVar(KesslerCVars.MedicalBountyLimbChance, chance);
            }
        });

        await pair.CleanReturnAsync();
    }
}
