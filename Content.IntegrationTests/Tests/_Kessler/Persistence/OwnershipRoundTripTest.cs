using System.Linq;
using Content.Server._Kessler.Ownership;
using Content.Server._Kessler.Persistence;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// A ship's owner and a body's bank character slot must survive a persistent save and load, or upkeep would bill
/// nobody and a returning player could pay from the wrong character.
/// </summary>
[TestFixture]
public sealed class OwnershipRoundTripTest
{
    [Test]
    public async Task OwnerAndBankSlotPersist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var ownership = entMan.System<ShipOwnershipSystem>();

        var testMap = await pair.CreateTestMap();
        var owner = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid.Owner;
            // Slot -1 skips the background profile lookup, which would outlive the test.
            ownership.SetOwner(grid, owner, -1, "Test Captain", 50000, purchasedWithVoucher: false);
            Assert.That(ownership.TryGetOwnership(grid, out var comp));
            ownership.SetOwnerProfileId((grid, comp!), 42);

            var human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var bank = entMan.EnsureComponent<BankAccountComponent>(human);
#pragma warning disable RA0002
            bank.CharacterSlot = 3;
#pragma warning restore RA0002

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-ownership.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();

            var loadedGrid = tree.Single(uid => entMan.HasComponent<MapGridComponent>(uid));
            Assert.That(ownership.TryGetOwnership(loadedGrid, out var loaded), "the ship still has an owner");
            Assert.Multiple(() =>
            {
                Assert.That(loaded!.OwnerUserId, Is.EqualTo(owner.UserId));
                Assert.That(loaded.OwnerProfileId, Is.EqualTo(42));
                Assert.That(loaded.OwnerName, Is.EqualTo("Test Captain"));
                Assert.That(loaded.PurchasePrice, Is.EqualTo(50000));
                Assert.That(loaded.PurchasedWithVoucher, Is.False);
                Assert.That(loaded.PurchasedAt, Is.EqualTo(comp!.PurchasedAt).Within(TimeSpan.FromSeconds(1)));
            });

            var loadedHuman = tree.Single(uid => entMan.HasComponent<BankAccountComponent>(uid));
            Assert.That(entMan.GetComponent<BankAccountComponent>(loadedHuman).CharacterSlot, Is.EqualTo(3),
                "the body still knows which character's account it uses");
        });

        await pair.CleanReturnAsync();
    }
}
