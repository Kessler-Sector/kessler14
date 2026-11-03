using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Kessler.Persistence;
using Content.Server.GameTicking;
using Content.Shared._Kessler.CCVar;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// What happens when part of a persistent save goes wrong: an entity that throws while being saved, and a save that
/// fails to load.
/// </summary>
[TestFixture]
public sealed class PersistenceFailureTest
{
    private const string Marker = "kessler failed load marker";

    private static readonly Regex BodySection =
        new(@"(?ms)^- proto: MobHuman\r?\n  entities:\r?\n.*?(?=^- proto: )");

    /// <summary>
    /// A body whose component throws on save is left out together with everything inside it. The engine used to drop
    /// only the body and still write its organs, actions and clothing, which pointed at a missing parent and made the
    /// whole save fail to load.
    /// </summary>
    [Test]
    public async Task ThrowingEntityIsLeftOutCleanly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var save = entMan.System<PersistenceSaveSystem>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.EnsureComponent<ThrowOnSaveComponent>(body).Value = 2;
            var paper = entMan.SpawnEntity("Paper", testMap.GridCoords);

            // The throw and the entities left out are logged as errors.
            server.CfgMan.SetCVar(RTCVars.FailureLogLevel, LogLevel.Fatal);
            var options = SerializationOptions.Default with
            {
                Category = FileCategory.Map,
                EntityExceptionBehaviour = EntityExceptionBehaviour.IgnoreEntityAndChildren,
            };
            var (data, _) = save.Serialize([testMap.MapUid], options, applyPolicy: true, new List<string>());
            server.CfgMan.SetCVar(RTCVars.FailureLogLevel, LogLevel.Error);

            Assert.That(save.LastLeftOut, Does.Contain(body));
            Assert.That(save.LastLeftOut.Count, Is.GreaterThan(1), "The body's contents were not left out with it");

            var path = new ResPath("/kessler-failure-test-throwing-body.yml");
            save.Write(path, data);
            Assert.That(entMan.System<PersistenceLoadSystem>().TryLoadSave(path, LoadOptions, out var result),
                "A save without the throwing body failed to load");

            var loaded = snapshots.CollectTree([result!.Maps.Single().Owner]);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.Any(uid => entMan.HasComponent<ThrowOnSaveComponent>(uid)), Is.False);
                Assert.That(loaded.Count(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "Paper"),
                    Is.EqualTo(1), "The rest of the map was not saved");
            });
            entMan.DeleteEntity(paper);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A save that fails to load is moved aside and a new sector is generated, with nothing left from the failed
    /// load. It used to leave a half-loaded sector running (stations twice, and entities missing their startup).
    /// </summary>
    [Test]
    public async Task FailedLoadFallsBackCleanly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Dirty = true,
            Map = "Dusk",
        });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        var sector = entMan.System<PersistentSectorSystem>();
        var userData = server.ResolveDependency<IResourceManager>().UserData;
        var folder = new ResPath(server.CfgMan.GetCVar(KesslerCVars.PersistencePath)).ToRootedPath();
        await pair.RunTicksSync(10);

        // Save the generated sector with a body and a marker on the station.
        server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, true);
        await server.WaitPost(() =>
        {
            var station = entMan.EntityQuery<StationDataComponent>().First().Grids.First();
            var coords = new Robust.Shared.Map.EntityCoordinates(station, 0, 0);
            entMan.SpawnEntity("MobHuman", coords);
            var marker = entMan.SpawnEntity("Paper", coords);
            entMan.System<MetaDataSystem>().SetEntityName(marker, Marker);
            Assert.That(sector.TrySaveSector("test"));
        });

        // Break it the way the user's save was broken: the body is missing while its organs and actions are not.
        var text = userData.ReadAllText(folder / "sector.yml");
        var broken = BodySection.Replace(text, "- proto: MobHuman\n  entities: []\n");
        Assert.That(broken, Is.Not.EqualTo(text), "Could not remove the body from the save");
        userData.WriteAllText(folder / "sector.yml", broken);

        // Back to the lobby without saving over the broken save, then start a round that loads it.
        server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, false);
        server.CfgMan.SetCVar(CCVars.GameLobbyEnabled, true);
        await server.WaitPost(() => ticker.RestartRound());
        await pair.RunTicksSync(5);

        server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, true);
        server.CfgMan.SetCVar(RTCVars.FailureLogLevel, LogLevel.Fatal);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(30);
        server.CfgMan.SetCVar(RTCVars.FailureLogLevel, LogLevel.Error);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(sector.SectorLoaded, Is.False, "The broken save loaded");
            Assert.That(userData.Exists(folder / "sector.yml"), Is.False, "The broken save was not moved aside");

            var names = new List<string>();
            var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                Assert.That(meta.EntityName, Is.Not.EqualTo(Marker), "An entity from the failed load is still alive");
            }

            var stations = entMan.EntityQueryEnumerator<StationDataComponent, MetaDataComponent>();
            while (stations.MoveNext(out _, out _, out var meta))
            {
                names.Add(meta.EntityName);
            }

            Assert.That(names, Is.Unique, "Stations from the failed load are still alive");
        });

        server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, false);
        server.CfgMan.SetCVar(CCVars.GameLobbyEnabled, false);
        await pair.CleanReturnAsync();
    }

    private static readonly MapLoadOptions LoadOptions = new()
    {
        ExpectedCategory = FileCategory.Map,
        DeserializationOptions = DeserializationOptions.Default with { PauseMaps = true },
    };
}

/// <summary>
/// Throws when it is saved, standing in for a component the engine cannot serialize.
/// </summary>
[RegisterComponent]
public sealed partial class ThrowOnSaveComponent : Component
{
    [DataField(customTypeSerializer: typeof(ThrowOnWriteSerializer))]
    public int Value = 1;
}

public sealed class ThrowOnWriteSerializer : ITypeSerializer<int, ValueDataNode>
{
    public int Read(ISerializationManager serializationManager, ValueDataNode node, IDependencyCollection dependencies,
        SerializationHookContext hookCtx, ISerializationContext context = null,
        ISerializationManager.InstantiationDelegate<int> instanceProvider = null)
    {
        return int.Parse(node.Value);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext context = null)
    {
        return new ValidatedValueNode(node);
    }

    public DataNode Write(ISerializationManager serializationManager, int value, IDependencyCollection dependencies,
        bool alwaysWrite = false, ISerializationContext context = null)
    {
        throw new System.InvalidOperationException("ThrowOnSaveComponent cannot be saved");
    }
}
