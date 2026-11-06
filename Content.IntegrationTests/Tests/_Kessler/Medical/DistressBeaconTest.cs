#pragma warning disable RA0002 // Tests read component state directly.
using System.Linq;
using System.Numerics;
using Content.Server._Kessler.Medical.Patients;
using Content.Shared.Shuttles.Components;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._Kessler.Medical;

/// <summary>
/// Distress beacons: a scrap of wreck on radar with a living patient in a stasis pod who pays a rescue bonus, gone once
/// the beacon expires unanswered.
/// </summary>
[TestFixture]
public sealed class DistressBeaconTest
{
    [Test]
    public async Task BeaconHoldsAPatientAndExpires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var rule = entMan.System<PatientDistressRule>();
        var patients = entMan.System<PatientSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid grid = default;
        await server.WaitAssertion(() =>
        {
            var settings = new PatientDistressRuleComponent();
            var spawned = rule.SpawnBeacon(settings, testMap.MapId, new Vector2(500, 500));
            Assert.That(spawned, Is.Not.Null);
            grid = spawned!.Value;

            Assert.That(entMan.HasComponent<MapGridComponent>(grid));
            Assert.That(entMan.HasComponent<IFFComponent>(grid), "the beacon shows on radar");
            Assert.That(entMan.HasComponent<DistressBeaconComponent>(grid));

            EntityUid pod = default;
            var pods = entMan.EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
            while (pods.MoveNext(out var candidate, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    pod = candidate;
            }
            Assert.That(pod, Is.Not.EqualTo(EntityUid.Invalid), "there's a pod on the wreck");
            Assert.That(containers.TryGetContainer(pod, "entity_storage", out var storage));
            var patient = storage!.ContainedEntities.FirstOrDefault(entMan.HasComponent<KesslerPatientComponent>);
            Assert.That(patient, Is.Not.EqualTo(EntityUid.Invalid), "a living patient is in the pod");

            var comp = entMan.GetComponent<KesslerPatientComponent>(patient);
            Assert.That(comp.RescueBonus, Is.EqualTo(settings.RescueBonus), "the patient carries the rescue bonus");

            rule.DeleteExpiredBeacons(DateTime.UtcNow);
            Assert.That(entMan.IsQueuedForDeletion(grid), Is.False, "a fresh beacon stays");

            rule.DeleteExpiredBeacons(DateTime.UtcNow + settings.Lifetime + TimeSpan.FromMinutes(1));
            Assert.That(entMan.IsQueuedForDeletion(grid) || entMan.Deleted(grid), "an unanswered beacon is gone after its lifetime");
        });

        await pair.CleanReturnAsync();
    }
}
