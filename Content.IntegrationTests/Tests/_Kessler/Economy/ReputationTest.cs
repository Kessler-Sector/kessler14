#pragma warning disable RA0002 // Tests set component state directly.
using System.Collections.Generic;
using Content.Server._Kessler.Characters;
using Content.Server._Kessler.Reputation;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Reputation is awarded to the character a player is playing and kept in the database per track.
/// </summary>
[TestFixture]
public sealed class ReputationTest
{
    [Test]
    public void Levels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KesslerReputationSystem.GetLevel(0), Is.EqualTo(0));
            Assert.That(KesslerReputationSystem.GetLevel(119), Is.EqualTo(0));
            Assert.That(KesslerReputationSystem.GetLevel(120), Is.EqualTo(1));
            Assert.That(KesslerReputationSystem.GetLevel(4800), Is.EqualTo(4));
            Assert.That(KesslerReputationSystem.GetLevel(1_000_000), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task AwardIsSavedPerCharacterAndTrack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
        var reputation = entMan.System<KesslerReputationSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid body = default;
        var slot = 0;
        Dictionary<string, int> before = null;
        await server.WaitPost(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            slot = prefs!.SelectedCharacterIndex;

            body = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<BankAccountComponent>(body).CharacterSlot = slot;
            var mindId = mind.CreateMind(session.UserId, "Reputable");
            mind.TransferTo(mindId, body);
        });
        await pair.RunTicksSync(5);

        Task<Dictionary<string, int>> read = null;
        await server.WaitPost(() => read = reputation.GetReputationAsync(session.UserId, slot)!);
        await PoolManager.WaitUntil(server, () => read.IsCompleted);
        before = await read;
        var stationBefore = before.GetValueOrDefault("Station");
        var medicalBefore = before.GetValueOrDefault("Medical");

        await server.WaitPost(() =>
        {
            reputation.TryAward(body, "Station", 130, "test");
            reputation.TryAward(body, "Medical", 7, "test");
            reputation.TryAward(body, "NotATrack", 50, "test");
        });

        // Awards are written in the background; read until they show up.
        Dictionary<string, int> after = null;
        for (var i = 0; i < 20; i++)
        {
            Task<Dictionary<string, int>> check = null;
            await server.WaitPost(() => check = reputation.GetReputationAsync(session.UserId, slot)!);
            await PoolManager.WaitUntil(server, () => check.IsCompleted);
            after = await check;
            if (after.GetValueOrDefault("Station") == stationBefore + 130 && after.GetValueOrDefault("Medical") == medicalBefore + 7)
                break;
            await pair.RunTicksSync(10);
        }

        Assert.Multiple(() =>
        {
            Assert.That(after.GetValueOrDefault("Station"), Is.EqualTo(stationBefore + 130));
            Assert.That(after.GetValueOrDefault("Medical"), Is.EqualTo(medicalBefore + 7));
            Assert.That(after.ContainsKey("NotATrack"), Is.False, "unknown tracks are ignored");
        });

        await server.WaitPost(() => entMan.DeleteEntity(body));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body whose spawn didn't record its character slot still counts as the selected character (as it does for the
    /// bank), and a player who is ghosted is still playing the body their mind owns.
    /// </summary>
    [Test]
    public async Task CharacterFoundWithoutRecordedSlotAndWhileGhosted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
        var characters = entMan.System<KesslerCharacterSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid body = default;
        EntityUid ghost = default;
        EntityUid mindId = default;
        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<BankAccountComponent>(body); // CharacterSlot left at -1, as a spawn can leave it.
            mindId = mind.CreateMind(session.UserId, "Unrecorded");
            mind.TransferTo(mindId, body);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            var selected = prefs!.SelectedCharacterIndex;

            Assert.That(characters.TryGetSlot(body, out var user, out var slot), "the selected character is used");
            Assert.That(user, Is.EqualTo(session.UserId));
            Assert.That(slot, Is.EqualTo(selected));
            Assert.That(entMan.GetComponent<BankAccountComponent>(body).CharacterSlot, Is.EqualTo(selected),
                "and recorded on the body");

            ghost = entMan.SpawnEntity("AdminObserver", testMap.GridCoords);
            mind.Visit(mindId, ghost);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(ghost));
            Assert.That(characters.TryGetCharacter(session, out var found, out _), "a ghosted player still has their character");
            Assert.That(found, Is.EqualTo(body));

            mind.UnVisit(session);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissingProfileGetsNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();

        Task<int?> add = null;
        await server.WaitPost(() => add = db.AddReputationAsync(int.MaxValue, "Station", 10));
        await PoolManager.WaitUntil(server, () => add.IsCompleted);
        Assert.That(await add, Is.Null);

        await pair.CleanReturnAsync();
    }
}
