#pragma warning disable RA0002 // Tests set component state directly.
using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Licences;
using Content.Server._Kessler.Reputation;
using Content.Server.Preferences.Managers;
using Content.Shared._Kessler.Licences;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Pilot licences gate ship sizes, need reputation and time with the previous licence, and are bought for a fee.
/// </summary>
[TestFixture]
public sealed class LicenceTest
{
    [Test]
    public async Task ShipSizesNeedTheirLicence()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var licences = server.EntMan.System<KesslerLicenceSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var vessels = protoMan.EnumeratePrototypes<VesselPrototype>().Where(v => v.Group == ShipyardConsoleUiKey.Shipyard).ToList();
            List<string> Needs(VesselPrototype v) => licences.RequiredFor(v).Select(l => l.ID).ToList();

            var micro = vessels.FirstOrDefault(v => v.Category == VesselSize.Micro);
            if (micro != null)
                Assert.That(Needs(micro), Is.Empty, "micro ships need no licence");

            var small = vessels.First(v => v.Category == VesselSize.Small && v.Price < 300000);
            Assert.That(Needs(small), Is.EquivalentTo(new[] { "PilotI" }));

            var medium = vessels.First(v => v.Category == VesselSize.Medium && v.Price < 300000);
            Assert.That(Needs(medium), Is.EquivalentTo(new[] { "PilotII" }));

            var capital = vessels.FirstOrDefault(v => v.Price >= 300000);
            if (capital != null)
                Assert.That(Needs(capital), Does.Contain("PilotCapital"));

            var security = protoMan.EnumeratePrototypes<VesselPrototype>().FirstOrDefault(v => v.Group == ShipyardConsoleUiKey.Security);
            if (security != null)
                Assert.That(Needs(security), Is.Empty, "job shipyards keep their own rules");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RequirementsNeedReputationAndTime()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var licences = server.EntMan.System<KesslerLicenceSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var pilot1 = protoMan.Index<LicencePrototype>("PilotI");
            var pilot2 = protoMan.Index<LicencePrototype>("PilotII");
            var now = DateTime.UtcNow;
            var none = new Dictionary<string, DateTime>();

            Assert.That(licences.MissingRequirements(pilot1, new Dictionary<string, int>(), none, now), Has.Count.EqualTo(1),
                "no reputation, no licence");
            Assert.That(licences.MissingRequirements(pilot1, new Dictionary<string, int> { ["Medical"] = 130 }, none, now), Is.Empty,
                "level 1 on any track is enough");

            var rich = new Dictionary<string, int> { ["Station"] = 700 };
            Assert.That(licences.MissingRequirements(pilot2, rich, none, now), Has.Count.EqualTo(1), "needs Pilot I first");
            var recent = new Dictionary<string, DateTime> { ["PilotI"] = now - TimeSpan.FromDays(1) };
            Assert.That(licences.MissingRequirements(pilot2, rich, recent, now), Has.Count.EqualTo(1), "held for too short a time");
            var old = new Dictionary<string, DateTime> { ["PilotI"] = now - TimeSpan.FromDays(4) };
            Assert.That(licences.MissingRequirements(pilot2, rich, old, now), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BuyingAPilotLicence()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
        var licences = entMan.System<KesslerLicenceSystem>();
        var reputation = entMan.System<KesslerReputationSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid body = default;
        var slot = 0;
        await server.WaitPost(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            slot = prefs!.SelectedCharacterIndex;
            body = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<BankAccountComponent>(body).CharacterSlot = slot;
            mind.TransferTo(mind.CreateMind(session.UserId, "Licensee"), body);
        });
        await pair.RunTicksSync(5);

        // Enough reputation and money for Pilot I.
        await server.WaitPost(() => reputation.TryAward(body, "Station", 200, "test"));
        for (var i = 0; i < 20; i++)
        {
            Task<Dictionary<string, int>> read = null;
            await server.WaitPost(() => read = reputation.GetReputationAsync(session.UserId, slot)!);
            await PoolManager.WaitUntil(server, () => read.IsCompleted);
            if ((await read).GetValueOrDefault("Station") >= 120)
                break;
            await pair.RunTicksSync(10);
        }

        int before = default;
        await server.WaitPost(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            before = ((HumanoidCharacterProfile) prefs!.Characters[slot]).BankBalance;
        });

        Task<string> buy = null;
        await server.WaitPost(() => buy = licences.TryBuyAsync(body, protoMan.Index<LicencePrototype>("PilotI")));
        await PoolManager.WaitUntil(server, () => buy.IsCompleted);
        var failure = await buy;

        if (before < 2000)
        {
            Assert.That(failure, Is.Not.Null, "a character who can't afford the fee doesn't get it");
        }
        else
        {
            Assert.That(failure, Is.Null, failure);
            await server.WaitAssertion(() =>
            {
                Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
                Assert.That(((HumanoidCharacterProfile) prefs!.Characters[slot]).BankBalance, Is.EqualTo(before - 2000), "the fee was paid");

                var small = protoMan.EnumeratePrototypes<VesselPrototype>()
                    .First(v => v.Group == ShipyardConsoleUiKey.Shipyard && v.Category == VesselSize.Small && v.Price < 300000);
                Assert.That(licences.CanBuyShip(body, small, out var reason), reason);

                var medium = protoMan.EnumeratePrototypes<VesselPrototype>()
                    .First(v => v.Group == ShipyardConsoleUiKey.Shipyard && v.Category == VesselSize.Medium && v.Price < 300000);
                Assert.That(licences.CanBuyShip(body, medium, out _), Is.False, "Pilot I doesn't cover medium ships");
            });

            Task<string> again = null;
            await server.WaitPost(() => again = licences.TryBuyAsync(body, protoMan.Index<LicencePrototype>("PilotI")));
            await PoolManager.WaitUntil(server, () => again.IsCompleted);
            Assert.That(await again, Is.Not.Null, "a licence can't be bought twice");
        }

        await server.WaitPost(() => entMan.DeleteEntity(body));
        await pair.CleanReturnAsync();
    }
}
