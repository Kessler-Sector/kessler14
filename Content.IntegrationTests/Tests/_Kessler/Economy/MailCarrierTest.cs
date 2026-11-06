#pragma warning disable RA0002 // Tests set component state directly.
using Content.Server._DV.Mail.Components;
using Content.Server._Kessler.Economy;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Whoever carries a parcel to its recipient gets a share of its value; the recipient picking up their own mail does
/// not.
/// </summary>
[TestFixture]
public sealed class MailCarrierTest
{
    [Test]
    public async Task CarrierGetsAShare()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
        var mailCarrier = entMan.System<MailCarrierSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid carrier = default;
        EntityUid recipient = default;
        EntityUid mail = default;
        var slot = 0;
        var before = 0;
        await server.WaitPost(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            slot = prefs!.SelectedCharacterIndex;
            before = ((HumanoidCharacterProfile) prefs.Characters[slot]).BankBalance;

            carrier = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(carrier, "Mail Carrier");
            entMan.AddComponent<BankAccountComponent>(carrier).CharacterSlot = slot;
            var mindId = mind.CreateMind(session.UserId, "Mail Carrier");
            mind.TransferTo(mindId, carrier);

            recipient = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(recipient, "Parcel Recipient");

            mail = entMan.SpawnEntity("MailAdminFun", testMap.GridCoords);
            var mailComp = entMan.GetComponent<MailComponent>(mail);
            mailComp.Recipient = "Parcel Recipient";
            mailComp.IsProfitable = true;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(hands.TryPickupAnyHand(carrier, mail));
            Assert.That(entMan.GetComponent<MailCarrierComponent>(mail).Carrier, Is.EqualTo(carrier));

            var paid = mailCarrier.PayCarrier(mail, 1000, recipient);
            Assert.That(paid, Is.EqualTo(300), "the default share is 30%");

            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            Assert.That(((HumanoidCharacterProfile) prefs!.Characters[slot]).BankBalance, Is.EqualTo(before + 300));

            Assert.That(mailCarrier.PayCarrier(mail, 1000, carrier), Is.EqualTo(0), "carrying your own mail pays nothing");
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(carrier);
            entMan.DeleteEntity(recipient);
            entMan.DeleteEntity(mail);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RecipientIsNotACarrier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var recipient = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(recipient, "Parcel Recipient");
            entMan.AddComponent<BankAccountComponent>(recipient);

            var mail = entMan.SpawnEntity("MailAdminFun", testMap.GridCoords);
            var mailComp = entMan.GetComponent<MailComponent>(mail);
            mailComp.Recipient = "Parcel Recipient";
            mailComp.IsProfitable = true;

            Assert.That(hands.TryPickupAnyHand(recipient, mail));
            Assert.That(entMan.HasComponent<MailCarrierComponent>(mail), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
