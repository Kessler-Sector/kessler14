#pragma warning disable RA0002 // Tests read component state directly.
using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Licences;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.Licences;
using Content.Shared._Kessler.Reputation;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// The licensing terminals: Dusk has one in each shipyard, and a terminal shows the character using it their licences
/// and reputation.
/// </summary>
[TestFixture]
public sealed class LicenceOfficeTest
{
    [Test]
    public async Task DuskShipyardsHaveWorkingTerminals()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            Dirty = true,
            Map = "Dusk",
        });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var stations = entMan.System<StationSystem>();

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var terminals = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<LicenceOfficeComponent>();
            while (query.MoveNext(out var uid, out _))
                terminals.Add(uid);

            Assert.That(terminals, Has.Count.EqualTo(2), "one terminal in each Dusk shipyard");
            foreach (var terminal in terminals)
            {
                var xform = entMan.GetComponent<TransformComponent>(terminal);
                Assert.That(xform.Anchored, $"{entMan.ToPrettyString(terminal)} is anchored");
                Assert.That(stations.GetOwningStation(terminal), Is.Not.Null, "it's on the station");
                Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(terminal).Powered, "it has power");

                var grid = xform.GridUid!.Value;
                var gridComp = entMan.GetComponent<MapGridComponent>(grid);
                var tile = mapSys.TileIndicesFor(grid, gridComp, xform.Coordinates);
                var others = mapSys.GetAnchoredEntities(grid, gridComp, tile).Where(e => e != terminal).ToList();
                Assert.That(others, Is.Empty,
                    $"nothing else is anchored on its tile: {string.Join(", ", others.Select(e => entMan.ToPrettyString(e)))}");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TerminalShowsYourLicencesAndStanding()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var office = entMan.System<LicenceOfficeSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid body = default;
        EntityUid stranger = default;
        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<BankAccountComponent>(body);
            mind.TransferTo(mind.CreateMind(session.UserId, "Applicant"), body);
            stranger = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
        });
        await pair.RunTicksSync(5);

        Task<LicenceOfficeStateMessage> mine = null;
        Task<LicenceOfficeStateMessage> theirs = null;
        await server.WaitPost(() =>
        {
            mine = office.BuildStateAsync(body);
            theirs = office.BuildStateAsync(stranger);
        });
        await PoolManager.WaitUntil(server, () => mine.IsCompleted && theirs.IsCompleted);

        var state = await mine;
        var strangerState = await theirs;
        Assert.Multiple(() =>
        {
            Assert.That(strangerState.Problem, Is.Not.Null, "a body that isn't a saved character gets an explanation");
            Assert.That(state.Problem, Is.Null);
            Assert.That(state.Licences, Has.Count.EqualTo(protoMan.EnumeratePrototypes<LicencePrototype>().Count()));
            Assert.That(state.Tracks, Has.Count.EqualTo(protoMan.EnumeratePrototypes<ReputationTrackPrototype>().Count()));

            var pilot1 = state.Licences.Single(l => l.Id == "PilotI");
            if (pilot1.HeldSince == null && state.Tracks.All(t => t.Points < 120))
                Assert.That(pilot1.Missing, Is.Not.Empty, "without reputation, Pilot I says what's missing");
            Assert.That(state.Tracks.All(t => t.NextLevel == null || t.NextLevel > t.LevelStart));
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(body);
            entMan.DeleteEntity(stranger);
        });
        await pair.CleanReturnAsync();
    }
}
