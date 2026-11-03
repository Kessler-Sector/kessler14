using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Players;
using Content.Shared.Station.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// A connected player's character goes through a persistent save, a simulated restart and a load, then the player
/// reconnects and must end up back in their own body with their own mind.
/// </summary>
[TestFixture]
public sealed class MindRoundTripTest
{
    [Test]
    public async Task PlayerReturnsToLoadedBody()
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
        var playerMan = server.PlayerMan;
        var mindSystem = entMan.System<SharedMindSystem>();
        var persistenceSave = entMan.System<PersistenceSaveSystem>();
        var persistenceLoad = entMan.System<PersistenceLoadSystem>();
        var ticker = entMan.System<GameTicker>();
        var path = new ResPath("/kessler-mind-roundtrip.yml");

        await pair.RunTicksSync(10);

        var session = playerMan.Sessions.Single();
        var userId = session.UserId;
        var username = session.Name;
        var body = session.AttachedEntity!.Value;
        var mindId = session.GetMind()!.Value;
        var mind = entMan.GetComponent<MindComponent>(mindId);
        var characterName = mind.CharacterName;
        var roles = mind.MindRoleContainer.ContainedEntities.Count;
        Assert.Multiple(() =>
        {
            Assert.That(mind.OwnedEntity, Is.EqualTo(body));
            Assert.That(entMan.GetComponent<MindContainerComponent>(body).Mind, Is.EqualTo(mindId));
            Assert.That(roles, Is.GreaterThan(0), "Player should have a job role");
        });

        // Disconnect: the body stays in the world without a player, as it would at a restart.
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientDisconnect("Restart"));
        await pair.RunTicksSync(5);
        Assert.That(session.Status, Is.EqualTo(SessionStatus.Disconnected));

        EntityUid loadedMind = default;
        EntityUid loadedBody = default;

        await server.WaitPost(() =>
        {
            var sectorMap = entMan.System<SharedMapSystem>().GetMap(ticker.DefaultMap);
            Assert.That(persistenceSave.TrySaveMap(sectorMap, path));

            // A restart starts from nothing: delete the sector, the stations and every mind and ghost.
            entMan.DeleteEntity(sectorMap);
            DeleteAll<StationDataComponent>(entMan);
            DeleteAll<MindComponent>(entMan);
            DeleteAll<GhostComponent>(entMan);
            Assert.That(mindSystem.GetMind(userId), Is.Null);

            var options = new MapLoadOptions { ExpectedCategory = FileCategory.Map };
            Assert.That(persistenceLoad.TryLoadSave(path, options, out var result));

            loadedMind = mindSystem.GetMind(userId) ?? default;
            Assert.That(loadedMind, Is.Not.EqualTo(default(EntityUid)), "No loaded mind is registered for the player");
            var loaded = entMan.GetComponent<MindComponent>(loadedMind);
            loadedBody = loaded.OwnedEntity ?? default;

            Assert.Multiple(() =>
            {
                Assert.That(result!.Entities, Does.Contain(loadedMind));
                Assert.That(loaded.UserId, Is.EqualTo(userId));
                Assert.That(loaded.CharacterName, Is.EqualTo(characterName));
                Assert.That(loaded.MindRoleContainer.ContainedEntities, Has.Count.EqualTo(roles), "Job roles");
                Assert.That(result.Entities, Does.Contain(loadedBody), "Mind does not own the loaded body");
                Assert.That(entMan.GetComponent<MindContainerComponent>(loadedBody).Mind, Is.EqualTo(loadedMind));
                Assert.That(entMan.HasComponent<ActorComponent>(loadedBody), Is.False, "Loaded body claims a player");
                Assert.That(playerMan.GetPlayerData(userId).ContentData()?.Mind, Is.EqualTo(loadedMind),
                    "Player data does not point at the loaded mind");
            });
        });

        // Reconnect: the player must be put back into the loaded body.
        await Task.WhenAll(pair.Client.WaitIdleAsync(), pair.Server.WaitIdleAsync());
        pair.Client.SetConnectTarget(pair.Server);
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, username));
        await pair.RunTicksSync(10);

        var newSession = playerMan.Sessions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(newSession.Status, Is.EqualTo(SessionStatus.InGame));
            Assert.That(newSession.AttachedEntity, Is.EqualTo(loadedBody), "Player was not put back into their body");
            Assert.That(newSession.GetMind(), Is.EqualTo(loadedMind));
        });

        await pair.CleanReturnAsync();
    }

    private static void DeleteAll<T>(IEntityManager entMan) where T : IComponent
    {
        var uids = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out _))
            uids.Add(uid);
        foreach (var uid in uids)
            entMan.DeleteEntity(uid);
    }
}
