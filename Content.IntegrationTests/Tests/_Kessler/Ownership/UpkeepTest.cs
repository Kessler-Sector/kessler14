using System.Threading;
using System.Threading.Tasks;
using Content.Server._Kessler.Ownership;
using Content.Server._NF.Bank;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Kessler.Ownership;

/// <summary>
/// Daily upkeep is billed to an offline owner straight from the database. When they can't pay it becomes debt, and
/// debt older than the grace period flags the ship for impounding. Paying everything clears both.
/// </summary>
[TestFixture]
public sealed class UpkeepTest
{
    [Test]
    public async Task BillsOfflineOwnerAndTracksDebt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var db = server.ResolveDependency<IServerDbManager>();
        var ownership = entMan.System<ShipOwnershipSystem>();
        var upkeep = entMan.System<UpkeepSystem>();

        var testMap = await pair.CreateTestMap();

        // Database calls need the server thread's IoC context; run them there and tick until they finish.
        async Task<T> OnServer<T>(Func<Task<T>> work)
        {
            Task<T> task = null!;
            await server.WaitPost(() => task = work());
            await PoolManager.WaitUntil(server, () => task.IsCompleted);
            return await task;
        }

        // An offline owner with 1000 spesos.
        var owner = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile profile = null!;
        await server.WaitPost(() => profile = HumanoidCharacterProfile.Random().WithBankBalance(1000));
        await OnServer(() => db.InitPrefsAsync(owner, profile, CancellationToken.None));
        var profileId = await OnServer(() => db.GetProfileIdAsync(owner, 0));
        Assert.That(profileId, Is.Not.Null);

        var grid = testMap.Grid.Owner;
        ShipOwnershipComponent comp = null!;
        DateTime start = default;
        await server.WaitPost(() =>
        {
            // A 50,000 ship costs 500 a day.
            ownership.SetOwner(grid, owner, -1, "Test Captain", 50000, purchasedWithVoucher: false);
            Assert.That(ownership.TryGetOwnership(grid, out comp!));
            ownership.SetOwnerProfileId((grid, comp), profileId);
            Assert.That(upkeep.GetDailyUpkeep(comp), Is.EqualTo(500));
            start = comp.NextUpkeep;
        });

        async Task Bill(DateTime now)
        {
            Task task = null!;
            await server.WaitPost(() => task = upkeep.RunBilling(now));
            await PoolManager.WaitUntil(server, () => task.IsCompleted);
        }

        async Task<int> Balance()
        {
            var prefs = await OnServer(() => db.GetPlayerPreferencesAsync(owner, CancellationToken.None));
            return ((HumanoidCharacterProfile) prefs!.Characters[0]).BankBalance;
        }

        // Day 1 and day 2 are paid.
        await Bill(start);
        Assert.That(await Balance(), Is.EqualTo(500));
        await Bill(start + TimeSpan.FromDays(1));
        Assert.That(await Balance(), Is.EqualTo(0));
        Assert.That(comp.UpkeepDebt, Is.EqualTo(0));

        // Day 3 can't be paid: it becomes debt, and the balance doesn't go negative.
        await Bill(start + TimeSpan.FromDays(2));
        var balance = await Balance();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(0));
            Assert.That(comp.UpkeepDebt, Is.EqualTo(500));
            Assert.That(comp.DebtSince, Is.EqualTo(start + TimeSpan.FromDays(2)));
            Assert.That(comp.ImpoundDue, Is.False);
        });

        // Billing the same moment again charges nothing more.
        await Bill(start + TimeSpan.FromDays(2));
        Assert.That(comp.UpkeepDebt, Is.EqualTo(500));

        // Three days later (server down in between): days 4 to 6 are added, and the ship is due for impounding.
        await Bill(start + TimeSpan.FromDays(5));
        Assert.Multiple(() =>
        {
            Assert.That(comp.UpkeepDebt, Is.EqualTo(2000));
            Assert.That(comp.ImpoundDue, Is.True);
        });

        // The owner gets money; the next bill pays the debt and day 7, clearing everything.
        var prefs = await OnServer(() => db.GetPlayerPreferencesAsync(owner, CancellationToken.None));
        var character = (HumanoidCharacterProfile) prefs!.Characters[0];
        await OnServer(async () =>
        {
            await db.SaveCharacterSlotAsync(owner, character.WithBankBalance(10000), 0);
            return true;
        });

        await Bill(start + TimeSpan.FromDays(6));
        balance = await Balance();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(10000 - 2500));
            Assert.That(comp.UpkeepDebt, Is.EqualTo(0));
            Assert.That(comp.DebtSince, Is.Null);
            Assert.That(comp.ImpoundDue, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BillsOnlineOwnerThroughTheirSession()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
        var bank = entMan.System<BankSystem>();
        var ownership = entMan.System<ShipOwnershipSystem>();
        var upkeep = entMan.System<UpkeepSystem>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        int slot = default;
        int before = default;
        ShipOwnershipComponent comp = null!;
        await server.WaitPost(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            slot = prefs!.SelectedCharacterIndex;
            var profile = (HumanoidCharacterProfile) prefs.Characters[slot];
            before = profile.BankBalance;
            if (before < 500)
            {
                Assert.That(bank.TryBankDeposit(session, prefs, profile, 1000, out var newBalance));
                before = newBalance!.Value;
            }

            var grid = testMap.Grid.Owner;
            ownership.SetOwner(grid, session.UserId, slot, "Online Captain", 50000, purchasedWithVoucher: false);
            Assert.That(ownership.TryGetOwnership(grid, out comp!));
        });

        // Let the background profile lookup finish before billing.
        await PoolManager.WaitUntil(server, () => comp.OwnerProfileId != null);

        await server.WaitAssertion(() =>
        {
            var task = upkeep.RunBilling(comp.NextUpkeep);
            Assert.That(task.IsCompleted, "online owners are charged right away, not through the database");
            Assert.That(comp.UpkeepDebt, Is.EqualTo(0));

            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            var after = ((HumanoidCharacterProfile) prefs!.Characters[slot]).BankBalance;
            Assert.That(after, Is.EqualTo(before - 500), "the cached profile was charged");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VoucherShipsPayNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ownership = entMan.System<ShipOwnershipSystem>();
        var upkeep = entMan.System<UpkeepSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid.Owner;
            ownership.SetOwner(grid, new NetUserId(Guid.NewGuid()), -1, "Starter", 30000, purchasedWithVoucher: true);
            Assert.That(ownership.TryGetOwnership(grid, out var comp));
            Assert.That(upkeep.GetDailyUpkeep(comp!), Is.EqualTo(0));

            var task = upkeep.RunBilling(comp!.NextUpkeep + TimeSpan.FromDays(10));
            Assert.That(task.IsCompleted, "nothing to charge, so nothing waits on the database");
            Assert.That(comp.UpkeepDebt, Is.EqualTo(0));
            Assert.That(comp.ImpoundDue, Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
