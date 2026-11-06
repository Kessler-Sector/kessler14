using Content.Server._Kessler.Market;

namespace Content.Server._NF.Cargo.Systems;

/// <summary>
/// Kessler: pallet sales go through the market ledger (<see cref="MarketLedgerSystem"/>).
/// </summary>
public sealed partial class NFCargoSystem
{
    [Dependency] private readonly MarketLedgerSystem _marketLedger = default!;
}
