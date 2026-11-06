#pragma warning disable RA0002 // Tests set component state directly.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.Atmos.Components;
using Content.Server._NF.BountyContracts;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server._NF.Smuggling.Components;
using Content.Server._NF.Trade;
using Content.Server._WF.OutlawObjectives;
using Content.Shared._NF.Atmos.Components;
using Content.Shared._NF.BountyContracts;
using Content.Shared._NF.Trade;
using Content.Shared.Atmos;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// State that used to be lost or rolled again when the sector was saved and loaded: gas waiting in a sale point, the
/// gas deposit's "low" mark, trade crate destinations and express deadlines, dead drop timers and outlaw objectives.
/// </summary>
[TestFixture]
public sealed class RestartStateRoundTripTest
{
    /// <summary>
    /// Reads a saved number field from a save file. Times must be written as time remaining, because game time starts
    /// again from zero after a restart.
    /// </summary>
    private static double ReadSavedSeconds(IResourceManager res, ResPath path, string field)
    {
        using var reader = res.UserData.OpenText(path);
        var match = Regex.Match(reader.ReadToEnd(), $@"\b{field}: ([0-9.]+)");
        Assert.That(match.Success, $"{field} was saved");
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Runs the clock forward, so a time saved as an absolute game time can't pass for time remaining.
    /// </summary>
    private static async Task AdvanceClock(Pair.TestPair pair, IGameTiming timing)
    {
        await pair.RunTicksSync(300);
        Assert.That(timing.CurTime, Is.GreaterThan(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public async Task GasStateSurvives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var salePoint = entMan.SpawnEntity("GasSalePoint", testMap.GridCoords);
            entMan.GetComponent<GasSalePointComponent>(salePoint).GasStorage.AdjustMoles(Gas.Plasma, 1234f);

            var deposit = entMan.SpawnEntity("GasDepositOxygen", testMap.GridCoords);
            var depositComp = entMan.GetComponent<GasDepositComponent>(deposit);
            depositComp.LowMoles = 777f;

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-gas.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();

            var loadedSalePoint = tree.Single(entMan.HasComponent<GasSalePointComponent>);
            var loadedDeposit = tree.Single(entMan.HasComponent<GasDepositComponent>);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<GasSalePointComponent>(loadedSalePoint).GasStorage.GetMoles(Gas.Plasma),
                    Is.EqualTo(1234f).Within(0.01f), "unsold gas is kept");
                Assert.That(entMan.GetComponent<GasDepositComponent>(loadedDeposit).LowMoles, Is.EqualTo(777f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TradeCrateKeepsDestinationAndDeadline()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var res = server.ResolveDependency<IResourceManager>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();
        var path = new ResPath("/kessler-roundtrip-test-trade-crate.yml");
        await AdvanceClock(pair, timing);

        await server.WaitAssertion(() =>
        {
            // Several destinations, so a destination rolled again on load would usually differ.
            var destinations = new List<EntityUid>();
            foreach (var name in new[] { "Depot Alpha", "Depot Beta", "Depot Gamma", "Depot Delta" })
            {
                var destination = entMan.SpawnEntity(null, testMap.GridCoords);
                entMan.System<MetaDataSystem>().SetEntityName(destination, name);
                var destComp = entMan.ComponentFactory.GetComponent<TradeCrateDestinationComponent>();
                destComp.DestinationProto = "CargoA";
                entMan.AddComponent(destination, destComp);
                destinations.Add(destination);
            }

            // The test server's own stations are destinations too; point the crate at one on the saved map.
            var crate = entMan.SpawnEntity("CrateTradeSecureHigh", testMap.GridCoords);
            var comp = entMan.GetComponent<TradeCrateComponent>(crate);
            comp.DestinationStation = destinations[2];
            Assert.That(comp.ExpressDeliveryTime, Is.Not.Null, "it is an express crate");
            var destinationName = entMan.GetComponent<MetaDataComponent>(comp.DestinationStation!.Value).EntityName;
            var remaining = comp.ExpressDeliveryTime!.Value - timing.CurTime;

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, path);
            var loadedCrate = snapshots.CollectTree([loadedMap]).Single(entMan.HasComponent<TradeCrateComponent>);
            var loaded = entMan.GetComponent<TradeCrateComponent>(loadedCrate);

            Assert.Multiple(() =>
            {
                Assert.That(ReadSavedSeconds(res, path, "expressDeliveryTime"),
                    Is.EqualTo(remaining.TotalSeconds).Within(1), "the deadline is saved as time remaining");
                Assert.That(loaded.DestinationStation is { } dest && entMan.GetComponent<TransformComponent>(dest).MapUid == loadedMap,
                    "the destination points at the loaded station");
                Assert.That(entMan.GetComponent<MetaDataComponent>(loaded.DestinationStation!.Value).EntityName,
                    Is.EqualTo(destinationName), "the destination is not rolled again");
                Assert.That(loaded.ExpressDeliveryTime, Is.Not.Null);
                Assert.That((loaded.ExpressDeliveryTime!.Value - timing.CurTime).TotalSeconds,
                    Is.EqualTo(remaining.TotalSeconds).Within(1), "the deadline is kept");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeadDropTimerIsTimeRemaining()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var res = server.ResolveDependency<IResourceManager>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();
        var path = new ResPath("/kessler-roundtrip-test-dead-drop.yml");
        await AdvanceClock(pair, timing);

        await server.WaitAssertion(() =>
        {
            var chair = entMan.SpawnEntity("Chair", testMap.GridCoords);
            var deadDrop = entMan.AddComponent<DeadDropComponent>(chair);
            deadDrop.NextDrop = timing.CurTime + TimeSpan.FromMinutes(10);

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, path);
            var loaded = snapshots.CollectTree([loadedMap]).Single(entMan.HasComponent<DeadDropComponent>);

            Assert.Multiple(() =>
            {
                Assert.That(ReadSavedSeconds(res, path, "nextDrop"), Is.EqualTo(600).Within(1),
                    "the next drop is saved as time remaining");
                Assert.That((entMan.GetComponent<DeadDropComponent>(loaded).NextDrop!.Value - timing.CurTime).TotalSeconds,
                    Is.EqualTo(600).Within(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OutlawObjectivesSurvive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var res = server.ResolveDependency<IResourceManager>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();
        var path = new ResPath("/kessler-roundtrip-test-outlaw.yml");
        await AdvanceClock(pair, timing);

        await server.WaitAssertion(() =>
        {
            var target = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(target, "Outlaw Target");
            var outlaw = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(outlaw, "Outlaw");

            // A kill objective is active (no ship counters involved), a theft objective is still pending with its drive.
            var drive = entMan.SpawnEntity("WFOutlawObjectiveDataDrive", testMap.GridCoords);
            var item = entMan.EnsureComponent<OutlawObjectiveItemComponent>(drive);
            item.OwningCharacter = target;
            item.OwnerName = "Outlaw Target";
            item.Objective = "WFOutlawObjectiveTheft";

            var objectives = entMan.AddComponent<OutlawObjectiveComponent>(target);
            objectives.Active.Add("WFOutlawObjectiveKill");
            objectives.Pending.Add("WFOutlawObjectiveTheft");
            objectives.Items["WFOutlawObjectiveTheft"] = drive;
            entMan.AddComponent<OutlawObjectivePendingComponent>(target).NextActivation = timing.CurTime + TimeSpan.FromMinutes(20);

            // Damage credited to the outlaw on the target's ship (the test grid stands in for it).
            var damage = entMan.AddComponent<OutlawShipDamageComponent>(testMap.Grid.Owner);
            damage.Target = target;
            damage.Objective = "WFOutlawObjectiveShipDamage";
            damage.Total = 40;
            damage.Needed = 10;
            damage.Credit[outlaw] = 3;

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, path);
            var tree = snapshots.CollectTree([loadedMap]).ToList();
            var loadedTarget = tree.Single(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityName == "Outlaw Target");
            var loadedOutlaw = tree.Single(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityName == "Outlaw");
            var loadedDrive = tree.Single(entMan.HasComponent<OutlawObjectiveItemComponent>);
            var loadedGrid = tree.Single(entMan.HasComponent<OutlawShipDamageComponent>);

            var loadedObjectives = entMan.GetComponent<OutlawObjectiveComponent>(loadedTarget);
            var loadedItem = entMan.GetComponent<OutlawObjectiveItemComponent>(loadedDrive);
            var loadedDamage = entMan.GetComponent<OutlawShipDamageComponent>(loadedGrid);
            Assert.Multiple(() =>
            {
                Assert.That(loadedObjectives.Active, Is.EquivalentTo(new[] { "WFOutlawObjectiveKill" }));
                Assert.That(loadedObjectives.Pending, Is.EquivalentTo(new[] { "WFOutlawObjectiveTheft" }));
                Assert.That(loadedObjectives.Items["WFOutlawObjectiveTheft"], Is.EqualTo(loadedDrive));
                Assert.That(ReadSavedSeconds(res, path, "nextActivation"), Is.EqualTo(1200).Within(1),
                    "the next activation is saved as time remaining");
                Assert.That((entMan.GetComponent<OutlawObjectivePendingComponent>(loadedTarget).NextActivation - timing.CurTime).TotalSeconds,
                    Is.EqualTo(1200).Within(1));

                Assert.That(loadedItem.OwningCharacter, Is.EqualTo(loadedTarget));
                Assert.That(loadedItem.Objective.Id, Is.EqualTo("WFOutlawObjectiveTheft"));

                Assert.That(loadedDamage.Target, Is.EqualTo(loadedTarget));
                Assert.That(loadedDamage.Needed, Is.EqualTo(10));
                Assert.That(loadedDamage.Credit, Is.EquivalentTo(new[] { new KeyValuePair<EntityUid, int>(loadedOutlaw, 3) }));
            });

            // The test grid carries the damage tracking; take it off before the pool cleans up, with the originals.
            entMan.RemoveComponent<OutlawShipDamageComponent>(testMap.Grid.Owner);
            entMan.RemoveComponent<OutlawShipDamageComponent>(loadedGrid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BountyContractsSurvive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // Contracts live on the sector service entity; a plain entity on the test map stands in for it.
            var holder = entMan.SpawnEntity(null, testMap.GridCoords);
            var data = entMan.AddComponent<BountyContractDataComponent>(holder);
            var collection = server.ProtoMan.EnumeratePrototypes<BountyContractCollectionPrototype>().First().ID;
            var created = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
            data.LastId = 7;
            data.Contracts![collection][7] = new BountyContract(7, BountyContractCategory.Criminal, "Wanted Person", 25000,
                NetEntity.Invalid, "ACGT", "Some Ship", "Stole a canister", "Posted by", "Canister thief", "Call me", created);

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-bounty-contracts.yml"));
            var loadedHolder = snapshots.CollectTree([loadedMap]).Single(entMan.HasComponent<BountyContractDataComponent>);
            var loaded = entMan.GetComponent<BountyContractDataComponent>(loadedHolder);

            Assert.That(loaded.Contracts, Is.Not.Null);
            Assert.That(loaded.Contracts![collection].TryGetValue(7, out var contract), "the posted contract is kept");
            Assert.Multiple(() =>
            {
                Assert.That(loaded.LastId, Is.EqualTo(7));
                Assert.That(contract!.Name, Is.EqualTo("Wanted Person"));
                Assert.That(contract.Reward, Is.EqualTo(25000));
                Assert.That(contract.Category, Is.EqualTo(BountyContractCategory.Criminal));
                Assert.That(contract.DNA, Is.EqualTo("ACGT"));
                Assert.That(contract.Created, Is.EqualTo(created));
                Assert.That(loaded.OrderedCollections, Is.Not.Empty, "the collection list is rebuilt");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeedsKeepTheirTraits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var packet = entMan.SpawnEntity("TomatoSeeds", testMap.GridCoords);
            var seedComp = entMan.GetComponent<SeedComponent>(packet);
            var bred = server.ProtoMan.Index<SeedPrototype>(seedComp.SeedId!).Clone();
            bred.Potency = 77f;
            bred.Yield = 9;
            seedComp.Seed = bred;

            var tray = entMan.SpawnEntity("hydroponicsTray", testMap.GridCoords);
            var plant = entMan.GetComponent<PlantHolderComponent>(tray);
            var growing = bred.Clone();
            growing.Potency = 55f;
            plant.Seed = growing;
            plant.Age = 12;

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-seeds.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();
            var loadedSeed = entMan.GetComponent<SeedComponent>(tree.Single(entMan.HasComponent<SeedComponent>)).Seed;
            var loadedPlant = entMan.GetComponent<PlantHolderComponent>(tree.Single(entMan.HasComponent<PlantHolderComponent>));

            Assert.Multiple(() =>
            {
                Assert.That(loadedSeed, Is.Not.Null, "the bred seed is saved, not just its prototype");
                Assert.That(loadedSeed!.Potency, Is.EqualTo(77f));
                Assert.That(loadedSeed.Yield, Is.EqualTo(9));
                Assert.That(loadedPlant.Seed, Is.Not.Null, "the growing plant is saved");
                Assert.That(loadedPlant.Seed!.Potency, Is.EqualTo(55f));
                Assert.That(loadedPlant.Age, Is.EqualTo(12));
            });
        });

        await pair.CleanReturnAsync();
    }
}
