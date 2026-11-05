# Long-term depth: combined plan

Status: design proposal, 2026-11-05. Nothing here is built. This page combines three design documents into one plan, settles where they overlap, and lists the decisions the owner needs to make. The detail, code references and numbers are in:

- [progression.md](progression.md): the framework every department plugs into: stages, reputation, licences, rarity tiers, wear, money bands, the new-player on-ramp.
- [medical-science.md](medical-science.md): doctors, surgeons, paramedics, chemists, research, anomalies, artifacts, blueprints.
- [departments.md](departments.md): atmos and gas mining, cargo and trade, security, salvage and mining, engineering, service and station jobs.

## The problem in one paragraph

Every SS14 job is one short loop with a solved answer, prices are fixed while demand never runs out, and nothing remembers yesterday. Money is the only ladder, and its top rung is about 300,000, which a competent player reaches in about a day. Ships sell back at 95%, and nothing wears out, so money spent is never really spent. Lowpop makes it worse: service jobs have no customers and no pay, security has nothing to do without a player criminal, and medical work depends on a salvager finding corpses first. On a persistent server all of this runs out within days.

## The answer in five parts

1. **More ladders, paced by the calendar.** Per-character reputation with eight NPC bodies (`Station`, `Freight`, `Expedition`, `Engineering`, `Medical`, `Science`, `Security`, `Outlaw`), at about 1 point per minute of useful work. A daily soft cap (180 points per track at full rate, then 25%) means a player who plays all day and one who plays evenings progress at a similar pace by the calendar. Reputation unlocks **licences**, and licences gate ship sizes, profession ships, expedition tiers, rich gas fields, paid contracts and restricted supplies. The fastest route to a Large ship takes 50 to 80 hours over at least 10 days. Top reputation in one track takes about two months, and there are eight tracks.
2. **Generated work that pays the worker.** A single **contract board** gives every department NPC work when nobody else is online: Dusk work orders, pharmacy orders, gas to spec, deliveries, patrols, escorts, investigations and maintenance. It pays the player directly from a `WorkOrders` account that grows with the number of players online, so the money coming in stays bounded. **Station needs** (food, air, medicine, power, cleanliness running down over real time) and **living NPC patients** keep the demand real and finite.
3. **Diminishing returns everywhere.** A **market ledger** makes prices fall at a station flooded with one commodity, recovering over a day. That ends "drain a rock, sell at Dusk forever" and creates trade routes. Gas fields rotate and run dry. Anomaly vessels stop earning after about two hours with nobody tending them.
4. **Rare stays rare.** Six tiers (`Common` to `Relic`) with targets in play hours: a `Rare` item every ~60 participant-hours, an `Exotic` every ~300. Rare items never come from mail, map fills or unlimited lathe recipes. Blueprints become limited-run copies (the EVE model), which also ends "one ship sells every blueprint". Use-based wear on Rare and Exotic gear removes items from the world over time.
5. **Departments need each other.** Upper tiers need inputs from other jobs: science needs biological data from medical and material data from miners; chemistry needs rare inputs from exploration; gas refining feeds medical and stations; risky, valuable work draws NPC pirates, which creates security, engineering and medical work.

## Where the three documents overlap, and how this plan settles it

| Overlap | Settled as |
|---|---|
| Progression's work-order board (P6), departments' contract board (5.2), med-sci's pharmacy board (C4) | **One contract board system.** Dusk work orders, pharmacy orders and patient referrals are contract types on it, with one UI (console plus PDA cartridge) and one payout path from `WorkOrders`. |
| Section letters collide: med-sci uses M/C/S for medical/chemistry/science; departments uses A/C/S/M/E/V | This page prefixes them: `med-M1`, `chem-C1`, `sci-S3`, `dep-A1` (atmos), `dep-C1` (cargo), `dep-S1` (security), `dep-M1` (salvage), `dep-E1`, `dep-V1`. |
| Ship or organisation reputation (clinic ratings, trusted hauler companies, lab discovery credit) | One mechanism, added after per-character reputation: reputation on a ship's ownership record. Not needed for phase 1. |
| Starting money versus "new players work the station first" | The `PilotI` licence (about two hours of `Station` work) is the gate, not money. Starting money still drops (10,000 for an account's first character, 2,500 after), because character recreation currently prints 30,000 each time. |
| T4 parts and bluespace | Progression cuts the supply now (YAML). Med-sci adds a tier-4 science tech that prints limited-run copies. Both agree: the expedition blueprint becomes a 2-run copy. |
| Medical bounty pay | Reprice to the money bands when `med-M6` lands. Today a corpse pays 30k to 60k for 20 to 30 minutes, 3 to 6 times the band. |
| Account ticks (CGP 72k/h and the others) | Cut to a small base; departments get 10 to 20% of contract payouts instead, so their money follows work done. |

## Build order

Each phase is a set of PRs with round-trip tests for any new saved state. Phases 0 and 1 are best done before the server opens: taking money or items back from players later is a fight.

### Phase 0: fixes and rebalance (small, mostly YAML; before opening)

Persistence bugs found during the survey (each needs a round-trip test to confirm):
- Medical bounty corpses take their damage again on every load (`MedicalBountyComponent.BountyInitialized` isn't saved).
- Artifact graphs store `NetEntity` ids in data fields.
- Anomaly vessels forget their anomaly on load.
- Gas sitting in a sale point is lost at restart (`GasSalePointComponent.GasStorage`).
- Express trade crates lose their deadline (`Timer.Spawn`).
- Outlaw objective state is runtime-only.
- Dead drop timers use `CurTime`.
- Bounty contracts and botany seeds need a round-trip test.

Faucets and supply:
- Starting money cut, and only once per account.
- Replace the 50,000 rookie voucher with a slow, upkeep-free Micro loaner, once per account.
- Ship resale from 95% to 75%.
- Cut raw gas prices (plasma 1 → about 0.4), drop the 1.25 console bonus, and pay by purity.
- Cut T4 and bluespace supply: bluespace walls in cave expeditions from 10 to 0–2, no T4 parts in mail or the merchant store, 2-run expedition blueprints.
- Mail pays the carrier; power sales pay the seller.
- Lower the 15-player minimum on combat events to about 5.
- Rarity labels on examine.
- Income and money-supply logging, so later tuning is measured, not guessed.

### Phase 1: the ladders and the backbone (medium)

- Reputation core (database per character, daily cap, crew credit) and licences, starting with `Station`, `Freight`, `Expedition` and the Pilot licences.
- Contract board with procurement, delivery and station work-order types; the Dusk board with janitor, chef, bartender and mail orders; small stipend for active station staff. This is the new-player path.
- Market ledger, applied to gas and cargo pallets.
- Quick depth that needs no framework: Shitmed requirements in medical bounties (missing limbs, failed organs, cybernetics), chemistry tiers in YAML, capped anomaly idle income, re-tiering the tech tree using the engine's existing tier and lockout settings, energy surgical tools behind a science tech.

### Phase 2: demand and generated work (medium to large)

- Station needs at Dusk, Theseus, the CGP outpost and the depots; drifting station markets and commodity crates; supply-chain and gas contracts; station maintenance orders; food and drink worth something.
- Medical conditions (first 3 or 4: internal bleeding, organ failure, unknown poison, foreign body) and the living-patient generator.
- Blueprint copies with limited runs; pharmacy contracts.
- Crew payroll from a ship console.

### Phase 3: risk, security and the rest of the framework (large)

- Threat level and NPC director: smuggler interception, pirate raids (boarding parties and turreted ships first), NPC freighters with escort contracts, patrols and inspections, courier collateral.
- Medical and science licences, clinics, search and rescue; research data as a second currency for upper tiers; per-sector chemistry formulas.
- Limited-run blueprints everywhere, retuned loot tables, scarcity governor for Exotics.

### Phase 4: long tail and sinks (large, as content)

- Wear and repair; fleet upkeep scaling; sector projects that consume credits and Rare items.
- Anomaly traits, wild anomalies and containment; artifact origins, named artifacts and collectors; a shared discovery codex.
- Refined-gas economy and rotating gas fields with hazards; investigations; wrecks and salvage claims; botany genetics; ship refits; ship-side service jobs.
- Organ and cybernetic goods, invention, field research, outbreaks; chemistry purity if still wanted.
- Mining outposts together with POI shops.

## Decisions for the owner

Recommendations are from the three documents; where they disagreed, this page picks one.

**Progression and economy**
1. **Force new players to work Dusk before owning a ship?** No: make station work and crewing the fastest start. The `PilotI` licence (about two hours of work) is a soft gate, and a slow free Micro loaner lets people fly at once.
2. **Starting money.** 10,000 for an account's first character, 2,500 for later ones.
3. **Ship resale.** 75% instead of 95%.
4. **Reputation daily cap.** Soft cap: full rate for 180 points a day per track, then 25%.
5. **Licences per character or per account.** Per character, never expiring (CGP may suspend one for a few days after a violation).
6. **Wear.** Use-based only, strongest on Rare and Exotic, repairs lower maximum condition; nothing decays over time or while offline.
7. **Numeric skills (better at the same click with experience).** No: gate access, not stats.
8. **Announce Exotic finds sector-wide.** No.

**Departments**

9. **Rebalance gas now?** Yes, before opening (YAML only).
10. **Market saturation per station or sector-wide?** Per station, recovering over about a day.
11. **Pay the worker or the department?** The worker, with a 10 to 20% cut to the department account.
12. **NPC raids on player ships?** Yes, but only on ships with crew aboard, outside station safe zones, never stored or docked ships or SSD bodies; they damage and steal, not destroy.
13. **Outlaw objective payouts (100k per kill, 400k per stolen ship).** Cut sharply; give outlaws PvE contracts as their main income.
14. **Depleted gas deposits come back?** No: new fields appear on a rotation and old ones fade.
15. **Ship owners take Dusk work orders?** Yes, at lower pay with a tight daily cap.
16. **Security work open to non-CGP players?** Yes, for licensed armed players; at lowpop nobody may be in a CGP job.

**Medical and science**

17. **Living NPC patients?** Yes, starting with clinic referrals, then distress beacons.
18. **Licences gate mechanics or only contracts and supplies?** Only contracts and supplies: anyone can operate or mix anything.
19. **Woundmed or authored conditions?** Authored conditions. If Woundmed is wanted after all, it must land before opening.
20. **Chemistry recipes.** Basic and advanced stay public; upper tiers are seeded per sector and never re-rolled during the sector's life.
21. **Tech tree length.** One discipline to the top in 3 to 5 weeks at about 2 hours a day; useful mid-tier in a week; no single ship can do every discipline.
22. **Existing permanent blueprints when copies arrive.** Not an issue if copies land before opening; otherwise convert them to copies with five times their tier's runs.
23. **Outbreaks.** At most every one or two weeks, starting away from player ships, debilitating rather than lethal at lowpop, with admin controls.

## Next step

Phase 0 is small and mostly YAML, and it is cheapest now. The persistence bugs come first, because the medical and science work builds on those systems.
