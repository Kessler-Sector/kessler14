# Progression, economy and item rarity

Status: design proposal, 2026-11-05. Nothing here is built yet. Numbers quoted from the code are from `master` at `4beae8d797`.

Kessler is a persistent sector. Nothing resets, so anything a player can finish in a day is finished for good. This document looks at what the code gives players today, compares it with persistent games that kept players for years, and proposes a framework that lasts months: progression stages, reputation and licences, rarity tiers, wear and money sinks, automated station work for low population, and money targets per stage.

The medical/chemistry/science and atmos/cargo/security/service design documents plug into this framework. The short contract they use (track IDs, licence schema, tier targets, money bands) is in [section 7](#7-contract-for-the-department-designs).

## Top recommendations

1. **Gate by reputation and calendar time, not by money alone.** Money piles up quickly and cannot be the main gate. Per-character reputation with eight NPC bodies, earned at about 60 points per focused hour with a daily soft cap, unlocks licences. Licences gate ship sizes, profession ships, expedition difficulty and Rare gear. The fastest route to a Large ship becomes about 50 to 80 hours of play spread over at least 10 days; the top reputation level takes at least two months.
2. **Fix the faucets that a persistent world turns into exploits, before players arrive.** New characters start with 30,000 spesos and anyone with under 10 hours of playtime gets a 50,000 ship voucher on every spawn, so deleting and recreating characters prints money and free ships. Ships sell back at 95% of their value, so a ship is a bank account, not a purchase.
3. **Make Rare items rare at the source and let them wear out.** A bluespace matter bin is currently a lathe recipe that one expedition blueprint unlocks forever, plus about ten bluespace ore per cave expedition. Make those blueprints limited-run, cut bluespace ore to a Rare drop, remove T4 parts from mail and the merchant store, and add use-based wear with repairs that lower maximum condition, so the supply in the sector stays near a target.
4. **Give station jobs automated work that pays the worker.** Mail pays 7,500 per letter into the Frontier sector account, not to the carrier, and station jobs are only paid if a Station Representative pays them by hand. A work-order board on Dusk (and later POIs) generates NPC demand per job, pays the worker directly from a population-scaled `WorkOrders` account and awards reputation, so a janitor or bartender alone at 3 a.m. still has something worth doing.
5. **Measure before tuning.** The ledger (`LedgerEntryType`) and `WayfarerRoundSummary.SpesosFlowData` already record money flow. Add a per-character income log and run a test week before setting final rates; the targets in this document are the goal, not measured values.

---

## 1. What the code gives players today

### 1.1 Starting money and vouchers

| What | Value | Where |
|---|---|---|
| Starting balance per new character | **30,000** | `Content.Shared/Preferences/HumanoidCharacterProfile.cs:33` (`DefaultBalance`), forced on creation in `Content.Server/Preferences/Managers/ServerPreferencesManager.cs:132` |
| Character slots per account | 30 | `game.maxcharacterslots` (`CCVars.Game.cs:72`) |
| Rookie ship voucher | ships up to **50,000**, 99 redemptions, destroyed when empty | `ShipVoucherRookie` in `Resources/Prototypes/_NF/Entities/Objects/Devices/Misc/ship_vouchers.yml:161` |
| Who gets it | every spawn of a player with under **10 hours** overall playtime | `NFGreenhornLoadout` (`Resources/Prototypes/_NF/Loadouts/misc_loadouts.yml`), `nf14.greeting.loadout` and `nf14.greeting.max_playtime` (600 min) in `NFCCVars.cs`, applied in `Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs` |
| Station staff voucher | one ship from the staff shipyard (`Sr` console: Moppet 32,000, HermesMkII 43,000, Delivery 78,000 by access) | `ShipVoucherStationStaff`, `Resources/Prototypes/_WF/Roles/Jobs/Civilian/stationstaff.yml` |
| Voucher ships | pay no upkeep (Kessler decision); sell for nothing, contents included | `UpkeepSystem.cs:58`, `ShipyardSystem.Consoles.cs:472` |

So a brand new player has a free 50,000 ship and 30,000 cash in the first minute. On a round-based server that is a convenience. On a persistent one it has two problems:

- **Character recreation prints money.** Each new character starts at 30,000. A player can move cash to a ship or locker, delete the character, make a new one and collect again. Thirty slots alone is 900,000; recreating is unlimited.
- **Every spawn under 10 hours gets a voucher**, so an account can collect several upkeep-free ships in its first 10 hours.

### 1.2 Ship prices

177 vessel prototypes in `Resources/Prototypes/_NF/Shipyard`, `_WF/Shipyard` and `_Mono/Shipyard`. By size (`category`):

| Size | Count | Cheapest | Median | Most expensive |
|---|---|---|---|---|
| Micro | 8 | 4,000 (Zipper) | 9,250 | 13,000 (Broom) |
| Small | 65 | 7,210 (Diodeff) | 28,000 | 64,000 (Kestrel) |
| Medium | 66 | 24,000 (Backpacker) | 74,500 | 160,950 (Kite) |
| Large | 19 | 100,000 (Hammerhead) | 176,000 | 667,000 (Crescent) |

Groups (consoles): civilian `Shipyard` 7,210 to 667,000; `Expedition` 48,000 to 132,000; `Medical` 19,000 to 255,000 (Caladrius); `Security` (CGP) 20,595 to 177,755; `BlackMarket` 20,000 to 125,000; `Scrap` 4,000 to 52,250; `Sr` staff 7,700 to 78,000. Apart from the most expensive civilian ship, everything is under 300,000.

`VesselPrototype` (`Content.Shared/_NF/Shipyard/Prototypes/VesselPrototype.cs`) has a price, size, group, classes and an `Access` string, but no requirement besides money and console access.

Selling a ship returns **95%** of its appraisal (`shuttle.shipyard_base_sell_rate` 0.95, `NFCCVars.cs:105`), and the appraisal includes everything aboard. Trading up costs 5% per step, so ships hold their value almost perfectly.

### 1.3 Income sources

| Source | Numbers in the code | Notes |
|---|---|---|
| Salvage expeditions | 15 min each (`salvage.expedition_duration` 900), 5 min cooldown (`salvage.expedition_cooldown` 300), 20 min after a failure, at most 15 at once; difficulties `NFModerate`/`NFHazardous`/`NFExtreme` with loot budgets 30/40/50 (`Resources/Prototypes/_NF/Procedural/salvage_difficulties.yml`) | Paid in loot that is sold or used. The cash briefcases `ExpeditionRewardT1`..`T5` (5,000 to 70,000) in `_NF/Procedural/salvage_rewards.yml` are not referenced anywhere |
| Mining | an ore piece holds 100 material units; per unit: iron 0.05, quartz 0.075, silver 0.15, gold/plasma/uranium 0.2, diamond 0.5, bluespace 7.5 (`Reagents/Materials/ores.yml`, `_DV/Reagents/Materials/ores.yml`). Walls yield 1 to 5 common ore, 1 to 3 gold/silver, 1 to 2 plasma/uranium, 1 bluespace (`Resources/Prototypes/_NF/ore.yml`) | Iron ore is 5 spesos, gold 20, diamond 50, bluespace 750 |
| Trade crates | buy 1,000 at cargo (`_NF/Catalog/Cargo/cargo_cargo.yml`), worth 1,600 at the destination and 400 elsewhere (`_NF/Entities/Structures/Specific/TradingCrates/base.yml`) | 600 profit per crate delivered |
| Pallet and gas sales | sale consoles with market modifiers 0.5 to 1.5 (`_NF/Entities/Structures/Machines/Computers/computers.yml`); gas 0.15 to 8 per mole (`Atmospherics/gases.yml`) | |
| Mail | 7,500 per letter (`_DV/Mail/Components/MailComponent.cs:82`), +2,000 fragile, +5,000 large, +5,000 priority; a delivery every 10 minutes (`SectorMailComponent.cs`) | **Paid into the Frontier sector account, not to the carrier** (`MailSystem.cs:245`) |
| Sector accounts | Frontier 5/s (18,000/h), NFSD 20/s (72,000/h), Medical 6/s, Edison 5/s, each starting at 50,000 (`_NF/SectorServices/services.yml`) | This is the only "salary": a Station Representative pays staff from it by hand (`LedgerEntryType.StationWithdrawalPayroll`) |
| Cargo bounties | 75 prototypes, 500 to 32,500, median 6,000 (`Catalog/Bounties/*.yml`), 6 open per station | Upstream system; stations get the database from `NFBaseStationCargo` |
| Other | pirate and outlaw bounties, medical bounties (`Content.Server/_NF/Medical/MedicalBountySystem.cs`), community goals, the Wayfarer merchant token store | |

There is no automatic paycheck for any job. I could not measure credits per hour from the code alone: it depends on travel, tools and crew. The owner's experience (the most expensive ships affordable within about 6 hours) implies something like 40,000 to 50,000 per hour for a competent player, and that matches what a trade or expedition loop above can produce. **Measuring this is the first task** (section 6, phase 0).

### 1.4 Where high-tier items come from

Machine parts go up to rating 4 (`_NF/Entities/Objects/Misc/machine_parts.yml`): quadratic capacitor, femto manipulator, bluespace matter bin. Research unlocks rating 2 (`NFAdvancedParts`, 1,000 points) and rating 3 (`NFSuperParts`, 15,000 points) in `_NF/Research/techtree.yml`. **No technology unlocks rating 4.** It comes from:

| Source | Rate | Where |
|---|---|---|
| Rogue-silicon expedition blueprints | Unlock the rating-4 lathe recipe **permanently** in that lathe | `_NF/Entities/Objects/Tools/blueprints/blueprints_expedition_loot.yml` |
| The recipe itself | plasteel 150, plastic 150, uranium 75, gold 75, **bluespace 100** (one crystal) | `NFBaseBluespacePartRecipe`, `_NF/Recipes/Lathes/parts.yml` |
| Bluespace ore | **10 bluespace walls per cave expedition** (`_NF/Procedural/cave_vgroid.yml:130`, also `zombie_vgroid.yml`); weight 0.003 in the asteroid mineral spawner, more than diamond at 0.002 (`_NF/Entities/Markers/Spawners/Random/Salvage/minerals.yml`) | One ore refines to one crystal (`_NF/Recipes/Lathes/materials.yml`), so one cave expedition is up to ten rating-4 parts |
| Salvage reward crates | `CratePartsT3T4` (weight 0.5 in `SalvageRewardRare`), `CratePartsT4` (weight 1.0 in `SalvageRewardEpic`), each with 5 rolls | `Procedural/salvage_rewards.yml`, `Catalog/Fills/Crates/salvage.yml` |
| Dungeon loot | bluespace matter bin 5% of the matter-bin table | `TableDungeonLootEngineeringMatterBins`, `_NF/Entities/Markers/Spawners/Random/Items/engineering_tables.yml` |
| Mail | `MailNFT4ComponentBox` (an assorted T4 box) | `_NF/Mail/mail.yml:794` |
| Merchant uplink | `BoxT4ComponentsAssorted` for 100 merchant tokens | `_WF/Catalog/merchant_uplink_catalogue.yml:638` |

A bluespace matter bin sells for 500 (`StackPrice`). Other high tiers follow the same pattern: `SalvageRewardEpic` carries the antique power cell (weight 0.05), armory crates, omnizine and cognizine; research is per-lathe and permanent; tech disks still drop (`TechnologyDiskRare`, weight 0.5).

In a 3-day round all of this is fine. In a persistent sector, one blueprint plus one cave expedition supplies a crew with rating-4 parts forever.

### 1.5 Money sinks

| Sink | Size |
|---|---|
| Kessler upkeep | 1% of purchase price per real day (`kessler.upkeep.rate`), voucher ships exempt; a 176,000 Large ship costs 1,760 a day |
| Ship resale | 5% loss (sell rate 0.95), plus black-market tax |
| Market and shuttle records | market transaction cost 600 (`MarketConsoleComponent.TransactionCost`); shuttle record fees (`ShuttleRecordsSystem.Console.cs`) |
| Purchases | cargo orders, vending machines (some with price modifiers), ammunition, fuel |
| Mail penalties | set to 0 (`MailComponent.Penalty`) |

**There is no general wear or durability.** I found no system that degrades tools, armour, weapons or machine parts with use. Bodies are saved and nothing is lost on death. Persistent gear therefore only accumulates.

### 1.6 Hooks already in the code

- **Per-job playtime trackers and job requirements** (upstream role timers), for example `JobStationStaff`.
- **Ledger types**, including an unused `StationWithdrawalWorkOrder` (`Content.Shared/_NF/Bank/BUI/NFLedgerState.cs:45`), and the `SectorBank` accounts with a per-second income.
- **`WayfarerRoundSummary.SpesosFlowData`** (`Content.Server.Database/Model.cs:1569`): money flow per round in the database.
- **Roleplay levels** (`Content.Server/_WF/RoleplayLeveling/`): per-account XP from chat, emotes and commendations, stored in the database. Cosmetic today.
- **Corporations and community goals** (`Content.Server/_WF/Corporations/`, `Content.Server/_WF/CommunityGoals/`), both in the database; community goals already count kill orders and contributions.
- **Voucher fields** `ShipWhitelist` and `MaxValue` (`ShipyardVoucherComponent.Wayfarer.cs`), and console groups (`ShipyardConsoleUiKey`).
- **Kessler ownership, upkeep and storage**, keyed by account and profile ID (`Content.Server/_Kessler/Ownership/`).
- **The sector snapshot** (`sector.snapshot.json`, entity counts per prototype) written on each save, usable to count how many of an item exist.

### 1.7 What this means

Today's progression is one ladder, money, with ship price as the only rung, and the top rung is about 300,000. With a 95% buyback and no wear, money spent on ships and gear is never really spent. A persistent server needs more ladders (reputation, licences, rare items, property, shared projects), slower rungs measured in calendar time, and sinks that take value out of the world.

---

## 2. What other persistent games do

| Game | What keeps players for months | Take | Leave |
|---|---|---|---|
| **EVE Online** | Standings with NPC corporations unlock higher-level agents and better rewards; skills train in real time, so progress is calendar-bound; blueprint copies have limited runs while originals are rare, and Tech II copies come from invention that consumes inputs whether it succeeds or not; ship loss and trade fees remove value | Standings that unlock access, limited-run blueprints, transaction fees, calendar gates | Full-loot ship destruction (ships are homes here, and population is too low) |
| **Elite Dangerous** | Navy ranks lock the largest ships; engineers unlock after tasks for them; crafting materials have grades and a per-material storage cap tied to the commander | Rank-locked ships, NPC "patrons" with tasks, caps on hoarding exotic materials | Long RNG material grinds |
| **Star Wars Galaxies** | Resources spawn with changing quality stats; crafted items lose condition through use and damage, and repair demand kept crafters busy; player vendors trade while offline | Use-based wear and repair, quality inputs, offline vendors (already planned) | Time-based decay (the developers rejected it too) |
| **Rust, ARK, Conan** | Bases cost upkeep proportional to size and decay when unpaid | Upkeep scaling with property (Kessler has flat upkeep) | Destroying offline players' property; Kessler stores it instead |
| **Space Engineers servers** | Per-player block and grid limits, NPC trade stations, cleanup of abandoned grids | Fleet limits or scaled upkeep, NPC demand | Server wipes |
| **Albion Online, OSRS** | Albion destroys gear on death and gear has durability, so crafting always has customers; OSRS has degradable gear with repair costs and a sales tax on its exchange | Constant demand for crafters through wear, small trade tax | Full loot |

Common lessons that fit SS14 at about 50 players:

1. **Access, not stats.** The lasting games gate what you may use or where you may go (standings, ranks, licences). SS14 is about player knowledge, so numeric skills that make the same character better at the same click would feel foreign. Kessler should gate access and leave skill to the player.
2. **Calendar time is the fairest gate on a persistent server.** A daily cap on reputation makes the no-lifer and the evening player progress at similar calendar speed, and nothing can be bought or botted past it.
3. **Value must leave the world.** Every lasting economy destroys or consumes items. With PvE ship loss rare here, use-based wear and consumed inputs are our main destroyers.
4. **Rare must stay rare as the world ages.** Limited-run blueprints, rotating sources and sector-wide counts stop an item's supply from saturating.
5. **Low population needs NPC demand.** At 50 players many roles have one person on at a time; generated demand keeps every job worth doing alone.

---

## 3. Design principles

1. **Every activity feeds a ladder.** Station work, crewing, mining, medicine and science all earn reputation as well as money.
2. **Money buys, reputation permits.** Prices tell you what something costs; licences tell you whether you may have it yet.
3. **Calendar-bound, not grind-bound.** Daily soft caps on reputation, minimum days between licences.
4. **Solo-viable, group-faster.** Every rung can be reached alone; crews reach it sooner because reputation goes to everyone who took part.
5. **No time decay of owned things.** Offline players lose nothing (KESSLER.md: inactive property is stored, never deleted). Wear comes from use only.
6. **Everything is saved.** New state is a `[DataField]` on a saved entity or a database row; deadlines and cooldowns use real UTC time, never `CurTime`, which resets at restart.
7. **Tunable from config and prototypes.** Rates, thresholds and payouts live in cvars and YAML so they can follow the measurements.

---

## 4. The framework

### 4.1 Progression stages

Hours are hours of active play; days are real days.

| Stage | Reached at | Typical activity | Income target | Owns | Gate to next |
|---|---|---|---|---|---|
| **S0 Arrival** | start | Dusk station jobs (janitor, bar, kitchen, mail, library, medical), crewing for someone, a free Micro loaner | 6,000 to 10,000/h | personal kit, a loaner Micro ship | Pilot Licence I: `Station` L1 (or any track L1), fee 2,000 |
| **S1 Licensed spacer** | ~3 to 5 h | Own Small ship: mining, freight, small expeditions, small service ships | 12,000 to 20,000/h | one Small ship (20,000 to 50,000) | Pilot II: any track L2, Pilot I held 3 days, fee 15,000 |
| **S2 Professional** | ~15 to 25 h, ≥ 3 days | Medium ships in one profession; Hazardous expeditions | 25,000 to 40,000/h | one Medium (50,000 to 160,000), Superior gear | Pilot III: any track L3, Pilot II held 7 days, fee 50,000 |
| **S3 Captain** | ~50 to 80 h, ≥ 10 days | Large ships with a small crew; Extreme expeditions; Rare gear starts to appear | 40,000 to 60,000/h per person | a Large ship (100,000 to 285,000), crew | Capital licence: any track L4, Pilot III held 14 days, fee 200,000 |
| **S4 Magnate** | ~150 h+, ≥ 1 month | Several ships, a POI claim, a shop, a corporation, sector projects; collecting Exotics; L5 in a track | 60,000+/h, with heavy upkeep and wear | a fleet, capital ships (Crescent, 667,000), property | open ended |

Months of play come from the tracks: reaching L5 in one track takes at least ~67 days, and there are eight tracks, Exotic collections, property and sector projects after that.

### 4.2 Reputation

Per character (profile ID), stored in the database next to `BankBalance`, so it works while the character is offline or stored and can be shown on a web page later.

**Tracks** (prototype `reputationTrack`):

| ID | NPC body | Covers |
|---|---|---|
| `Station` | Dusk Station Authority | janitor, service, bar, kitchen, library, mail, general station work |
| `Freight` | Freight & Mining Guild | cargo, trade crates, mining, ore and gas sales |
| `Expedition` | Expeditionary Corps | salvage, expeditions, POI exploration |
| `Engineering` | Shipwrights' Union | atmos, power, ship building and repair, machine upgrades |
| `Medical` | Medical Guild | medical, chemistry, surgery |
| `Science` | Science Consortium | research, xenoarchaeology, anomalies |
| `Security` | Colonial Guard (CGP) | bounties, patrols, contraband |
| `Outlaw` | the underworld | black market, pirate bounties; gaining it costs `Security` and the reverse |

Eight tracks is about the most 50 players can fill; fewer would merge jobs that play very differently.

**Rate and levels.** The standard is about 1 point per minute of focused, useful work at the right tier (about 60 per hour). Awards are sized as "expected minutes of effort" so every department prices them the same way.

| Level | Name | Points | Focused hours | Minimum days with the daily cap |
|---|---|---|---|---|
| L0 | Unknown | 0 | 0 | 0 |
| L1 | Recognised | 120 | ~2 | 1 |
| L2 | Trusted | 600 | ~10 | 4 |
| L3 | Respected | 1,800 | ~30 | 10 |
| L4 | Honoured | 4,800 | ~80 | 27 |
| L5 | Exalted | 12,000 | ~200 | 67 |

**Daily soft cap.** The first 180 points per track per real day (about 3 hours of focused work) count in full, the rest at 25%. Players can always progress, but the calendar sets the pace. Days are counted in UTC from the database, like upkeep billing.

**Crew credit.** Awards for ship-based work (an expedition finished, a cargo run delivered) go to every character aboard who took part, not only the one at the console. This makes joining a crew a real on-ramp.

**Losing reputation.** Crimes recorded by the CGP and failed contracts can cost points, but levels never drop below the start of the current level from ordinary failures, so one bad night does not undo a month. No inactivity decay.

**What levels unlock.** Licences (4.3), ship groups (Medical ships need `Medical`, Expedition ships `Expedition`, black market `Outlaw`), expedition difficulty (Hazardous needs `Expedition` L2, Extreme L3), higher-paying work orders, guild vendor stock and cargo products of Superior or Rare tier, and recipes or job slots the department designs choose.

### 4.3 Licences

A `licence` prototype has requirements (track levels, a previous licence held for N real days, a fee) and shows on the character's ID card when examined. Licences are per character, stored in the database, and never expire (open decision 10).

| Licence | Allows | Requirements | Fee |
|---|---|---|---|
| `PilotI` | own Small ships | `Station` L1 or any track L1 | 2,000 |
| `PilotII` | Medium | any track L2, `PilotI` for 3 days | 15,000 |
| `PilotIII` | Large | any track L3, `PilotII` for 7 days | 50,000 |
| `PilotCapital` | ships over 300,000 | any track L4, `PilotIII` for 14 days | 200,000 |

Profession ships need the matching track as well: for example a Medium medical ship needs `PilotII` and `Medical` L2. CGP ships keep their existing job access. Micro ships need no licence.

Departments add their own licences with the same schema (a surgeon's licence, an atmos technician's licence) and use them to gate vendors, cargo products, recipes or job slots.

The shipyard check is a new `vesselRequirement` prototype keyed by vessel ID, so none of the 177 vessel files has to change and other forks' ships can be added from `_Kessler` YAML. Ships without an entry fall back to the size rule.

### 4.4 Rarity tiers

Targets are **expected participant-hours of eligible play per drop**: a crew of three playing one hour of suitable content is three participant-hours.

| Tier | Examples | Target | Sector inflow at ~50 players | Allowed sources |
|---|---|---|---|---|
| `Common` | T1 parts, basic tools and clothing | always available | unlimited | lathes, vendors, cargo |
| `Standard` | T2 parts, basic hardsuits, common chemicals | 1 to 3 h, or research tier 1 | unlimited | research, cargo, loot |
| `Superior` | T3 parts, good hardsuits, rarer chemicals | 10 to 20 h, or research plus guild L2 | plentiful | research behind licences, Hazardous loot, guild vendors |
| `Rare` | T4 parts and bluespace crystal, top hardsuits, cognizine, omnizine | **~60 h** | ~5 per week | Extreme loot, limited-run blueprints, guild L4 stores |
| `Exotic` | antique power cell, prototype weapons, best artifact effects | **~300 h** | ~1 per week, capped | rare loot rolls, deep POIs |
| `Relic` | named one-of-a-kind items | events only | one each | staff and story events |

Sector inflow assumes about 15 players doing eligible content for about 3 hours a day (45 participant-hours a day).

**Turning targets into drop chances.** For a source that gives `R` loot rolls per participant-hour, the chance per roll is `p = 1 / (R × H)` for a target of `H` hours. Example: an Extreme expedition with three players takes about 30 minutes (1.5 participant-hours) and has about 20 valuable spawners, so about 13 rolls per participant-hour; for a Rare at 60 hours, each roll should give a Rare about 1 time in 800 (0.13%).

**Rules for Rare and above:**

- Never from mail, fixed map fills, ship blueprints (a ship spawned from a blueprint must not contain them), or an unlimited lathe recipe.
- Recipes for Rare items come as limited-run blueprints (1 to 3 runs, a `RunsLeft` data field) or need a Rare input, such as refined bluespace crystal.
- Sale value reflects rarity, so a find is also a decision: a bluespace matter bin sells for about 15,000, not 500.
- The examine text shows the tier ("Rare", in its colour), from a `rarityTable` prototype listing entity IDs per tier. Departments add their items to that table instead of editing each entity.
- **Scarcity governor (Exotic tier, optional for Rare):** on load, count each Exotic in the sector (the snapshot already counts prototypes) and lower its drop weight as the count nears a target. The "I finally found it" moment survives hoarding.

**The bluespace matter bin, concretely:**

1. Cave and zombie expeditions place 0 to 2 bluespace walls (not 10), Extreme only; asteroid weight 0.003 down to about 0.0005.
2. Rogue-silicon T4 blueprints become 2-run blueprints.
3. Remove the T4 mail box from the mail pool, and the T4 box from the merchant store (or raise it from 100 to about 400 tokens).
4. `CratePartsT4` and `CratePartsT3T4` stay in the Epic and Rare reward tables at a weight computed from the target; the dungeon matter-bin table drops bluespace from 5% to about 1%.
5. Machines with T4 parts wear (4.5), so the parts in the sector are slowly consumed.

The result: a crew that runs Extreme expeditions finds a T4 part roughly every 20 hours together, and a crafter with a 2-run blueprint makes a few, not hundreds.

### 4.5 Wear, repair and other sinks

**Use-based wear** (`KesslerWearComponent`, data fields `Condition` and `MaxCondition`, 0 to 100):

- Condition drops with use: shots fired, damage absorbed by armour, hardsuit time in vacuum, tool uses, lathe jobs or powered hours for machine parts.
- At 0 the item is worn out: reduced effect (armour protection, part rating counted one lower, slower tool), never deleted.
- Repair at a repair bench or by an engineer with a repair kit, costing materials and time. Each repair lowers `MaxCondition` by a few points, so an item retires after many repairs. That is the permanent sink, and repair kits give engineering, science and crafting players steady customers.
- Wear rates by tier: Common items barely wear (no busywork), Rare and Exotic wear meaningfully, because those are the items whose supply must stay bounded.
- No wear while the item is stored or its owner is offline.

**Money sinks to add or change:**

| Sink | Proposal |
|---|---|
| Ship resale | 95% down to 75% (`shuttle.shipyard_base_sell_rate`), so a ship is a purchase |
| Licence fees | 2,000 to 200,000 (4.3) |
| Repairs | materials and service fees |
| Fleet upkeep | second ship at 1.25×, third at 1.5× and so on (later, after measuring) |
| Trade fee | a small fee on player trade and market use, once offline shops exist |
| Sector projects | community goals that consume credits and Rare items for sector-wide unlocks (a new POI, a shipyard upgrade): a sink and a shared long-term goal |
| Medical and cloning bills | for the medical design to decide |

**Starting money.** Lower the default balance from 30,000 to 10,000 and give it only to an account's first character (or the first few); later characters start at 2,500. Stop character recreation from refilling cash.

### 4.6 Automated station work for low population

**Work-order boards** on Dusk (and later POIs), a console plus a PDA cartridge, holding generated orders. Each order has a track, an objective, a payout, a reputation award and a real-time expiry.

- **Generation:** each track has a generator that keeps 2 or 3 orders open even with one player online, and adds more per online player in that job. Orders expire without penalty.
- **Objectives** use existing systems where possible: cleaning (station events that spill, litter or break things), service (an NPC patron "ticket" asking for a drink or dish at a counter pad within a time), mail (already exists; give the carrier a share), freight (deliver goods, trade crates), mining (deliver ore), engineering (repair a broken window, APC or pipe that an event caused), atmos (refill canisters, scrub a room), medical (medical bounties exist), science (analyse an artifact), security (patrol checkpoints, NPC raids).
- **Payout:** paid directly to the worker from a new `WorkOrders` sector account whose income per second scales with online players (for example 3,000 per hour per online player). When it runs dry, orders pay less, which bounds the faucet. Ledger type `StationWithdrawalWorkOrder` already exists.
- **Pricing:** payout = expected minutes × the band rate (S0 about 150 per minute, S2 about 500 per minute), reputation = expected minutes.
- **Stipend:** a small hourly base (about 2,000/h) for characters in a station job who completed at least one order in the last 30 minutes, so being on shift is worth something and AFK is not.
- **Mail:** the carrier gets 30% of each letter's value directly; the rest still goes to the Frontier account.
- **Crew payroll:** a ship console option where the owner sets an hourly wage paid from their bank to crew aboard. Crewing becomes a job, not a favour.

Department designs fill in the generators for their own jobs; this document defines the board, the account, the pricing rule and the persistence.

### 4.7 Economy targets

Rates per active hour, after tuning:

| Stage | Income | Next goal | Hours of saving | Upkeep as share of a 2-hour day |
|---|---|---|---|---|
| S0 | 8,000 | 30,000 Small ship | ~3 to 4 h (with 10,000 start) | 0 (loaner) |
| S1 | 16,000 | 75,000 Medium | ~5 h | 1% |
| S2 | 32,000 | 176,000 Large | ~5 to 6 h | 1 to 2% |
| S3 | 50,000 | second Large, POI claim, Rare gear | | 2 to 3% |
| S4 | 60,000+ | capital ship (667,000), property, projects | ~11 h | 5 to 10% with a fleet and wear |

Money alone is still fairly quick to save at each stage. That is intended: licences set the pace, and money is what you spend on the next rung, on repairs, licences and upkeep. If measurements show players drowning in cash at S3 and above, the levers are fleet upkeep scaling, wear rates and sector projects, not lower early income.

**Watch the money supply.** Log the sum of all bank balances and of the main item values daily. A healthy persistent economy grows slowly; if it doubles in a month, sinks are too weak.

### 4.8 The new-player on-ramp: the owner's idea, examined

The owner's idea is that new players spend some days working on Dusk or join a crew before owning a ship, and cannot afford the best ship in 6 hours.

- **Agreed:** no free 50,000 ship on arrival, and the top ships far away. Station work and crewing should be the fastest early path to reputation and money.
- **Not agreed: forcing it.** Players who joined a ship game to fly will leave if their first three days are mopping. Make station work the best start, not the only one.

Three on-ramps:

1. **Station job:** work orders, stipend, `Station` reputation; Pilot I in about 2 hours, a Small ship in 3 to 5.
2. **Join a crew:** crew credit for reputation, crew payroll for money; the captain gains a hand.
3. **Fly at once:** a free, upkeep-free **Micro loaner** (Scrap group, under about 12,000: Zipper, Ratrod, Canister, Rusty). Slow, with no resale value, but enough to mine and salvage alone. Returned when the player buys their own ship. This can start as config: a Kessler voucher with `ShipWhitelist` of those ships and `MaxValue` 12,000 in place of the rookie voucher, given once per account.

---

## 5. Proposals: effort, code, dependencies, persistence

| # | Proposal | Effort | Code touched | Depends on | Persistence |
|---|---|---|---|---|---|
| P1 | **Income and money-supply measurement** | S | `BankSystem` deposit hook in `_Kessler`, a daily summary written to the data folder or database | none | log only |
| P2 | **Faucet fixes**: starting balance and once per account, Micro loaner voucher, resale 0.75 | S to M | `Resources/Prototypes/_Kessler/` voucher and loadout, cvars `nf14.greeting.loadout` and `shuttle.shipyard_base_sell_rate`; the per-account start needs a DB flag and an edit in `ServerPreferencesManager` (marked `// Kessler`) | none | DB flag per account (migration); voucher state is on the saved entity |
| P3 | **T4 and bluespace supply cut** | S | YAML only: `cave_vgroid.yml`, `zombie_vgroid.yml`, `minerals.yml`, `engineering_tables.yml`, `_NF/Mail/mail.yml`, `merchant_uplink_catalogue.yml`, machine part `StackPrice` (each line marked `# Kessler`) | none | already-saved T4 parts stay; audit the live sector with the snapshot |
| P4 | **Reputation core**: tracks, levels, daily soft cap, crew credit, `TryAward` API, PDA cartridge | M | `Content.Server/_Kessler/Reputation/`, `Content.Shared/_Kessler/Reputation/` (sandbox-safe types only), DB table and migration, UI | P1 to calibrate | DB table (profile, track, points, last day, points today); loaded on spawn like bank balance; not part of world saves, so a world rollback does not roll it back (acceptable: reputation cannot be duplicated like items) |
| P5 | **Licences** and the shipyard and expedition gates | M | `_Kessler/Licences/`, `licence` and `vesselRequirement` prototypes; shipyard purchase check in `ShipyardSystem.Kessler.cs` with a one-line hook in `ShipyardSystem.Consoles.cs`; expedition console check | P4 | DB table (profile, licence, granted at); ID card examine reads it |
| P6 | **Work-order boards, `WorkOrders` account, stipend, mail share** | L (framework M, each generator S) | `Content.Server/_Kessler/WorkOrders/`, a console and cartridge, a sector bank account, a small edit in `MailSystem.cs` | P4 for reputation; departments for generators | open and accepted orders as data fields on the station entity; expiry in UTC `DateTime`; covered by a round-trip test |
| P7 | **Rarity table, examine labels, sale values** | S | `rarityTable` prototype, an examine system in `_Kessler/Rarity/` | none | none (prototype data) |
| P8 | **Limited-run blueprints** | S to M | `RunsLeft` on blueprints, via a `_Kessler` component and a lathe hook | P7 | `RunsLeft` data field; the recipe must be removed from the lathe when runs end, and that lathe state is saved too |
| P9 | **Scarcity governor** | M | `_Kessler/Rarity/`, counts from the snapshot on load, weight scaling in the loot spawners | P7 | counts rebuilt on load; no new saved state |
| P10 | **Wear and repair** | L | `KesslerWearComponent` and system, hooks into guns, armour, tools and machine parts, a repair bench and kits | P7; engineering design for repair | `Condition`/`MaxCondition` data fields; round-trip tests; must not be read-only |
| P11 | **Crew payroll** | M | ship console UI, payment from the owner's bank | ownership records (built) | payroll settings as data fields on the ship's ownership component; payments in the database |
| P12 | **Fleet upkeep scaling** | S | `UpkeepSystem.cs` | ownership (built), P1 | none new |
| P13 | **Sector projects** (community goals that consume credits and items) | M | extends `_WF/CommunityGoals` with consumption and unlock rewards | P7 | community goals are already in the database; unlocks that change the world (a new POI) are saved with the sector |

---

## 6. Build order

**Phase 0, before players arrive (small, mostly YAML and config).** P1 measurement, P2 faucet fixes, P3 T4 supply cut, P7 rarity labels. These are cheap now and painful later: once a player owns a stack of T4 parts or a million spesos from recreating characters, taking them back is a fight.

**Phase 1, the ladders.** P4 reputation, then P5 licences. Start with the `Station`, `Freight` and `Expedition` tracks and the Pilot licences, since those cover most players; add the other tracks as the department designs land.

**Phase 2, lowpop work.** P6 work-order framework with the `Station` generators (cleaning, service tickets, mail share) and the stipend; departments add their generators. P11 crew payroll.

**Phase 3, rarity that lasts.** P8 limited-run blueprints, retuned loot tables using the formula in 4.4, P9 governor for Exotics.

**Phase 4, sinks.** P10 wear and repair (start with Rare and Exotic items and machine parts), P12 fleet upkeep scaling, P13 sector projects.

Each phase is a set of PRs with round-trip tests for any new saved state, and each phase is tuned against the phase-0 measurements before the next starts.

---

## 7. Contract for the department designs

The department documents can rely on these, and should express their proposals in them:

- **Track IDs:** `Station`, `Freight`, `Expedition`, `Engineering`, `Medical`, `Science`, `Security`, `Outlaw`. Award reputation as expected minutes of effort through `KesslerReputationSystem.TryAward(actor, track, points, reason)`, to everyone who took part.
- **Levels:** L1 120, L2 600, L3 1,800, L4 4,800, L5 12,000 points; 180 points per track per day at full rate, then 25%.
- **Licences:** the `licence` prototype schema in 4.3; departments add their own and gate vendors, recipes, products or job slots with them.
- **Tiers:** `Common`, `Standard`, `Superior`, `Rare`, `Exotic`, `Relic` with the targets in 4.4; list items in the `rarityTable`; compute drop chances with `p = 1 / (R × H)`; Rare and above never from mail, map fills, ship blueprints or unlimited recipes.
- **Money bands:** S0 6,000 to 10,000/h, S1 12,000 to 20,000, S2 25,000 to 40,000, S3 40,000 to 60,000, S4 60,000+. Price work orders as expected minutes × band rate; pay them from the `WorkOrders` account.
- **Persistence:** new state is a data field or a database row, with UTC time for deadlines, and a round-trip test.

A copy of this contract is in the shared notes folder as `progression-framework.md`.

---

## 8. Open decisions

| # | Decision | Recommendation |
|---|---|---|
| 1 | Force new players to work on Dusk before owning a ship? | **No.** Make station work and crewing the fastest start, and give a slow free Micro loaner to those who want to fly at once. |
| 2 | What replaces the 50,000 rookie voucher? | **A Micro loaner** (Scrap ships under about 12,000), once per account, upkeep-free, no resale value. |
| 3 | Starting money | **10,000 for an account's first character, 2,500 for later ones**, so recreating characters no longer prints money. |
| 4 | Ship resale rate | **75%** instead of 95%. |
| 5 | Daily cap on reputation | **Yes, a soft cap** (full rate for 180 points a day, then 25%). A hard cap feels like a wall; no cap lets grinders outpace everyone by months. |
| 6 | Wear on items | **Use-based only**, strongest on Rare and Exotic, repairs lower maximum condition. No decay over time or while offline. |
| 7 | Fleet upkeep scaling | **Yes, but after measuring** (phase 4), starting at 1.25× for the second ship. |
| 8 | Insurance | **Not now.** PvE ship loss is rare; offer a paid recovery or tow-to-storage service for wrecks instead, and revisit if PvP grows. |
| 9 | Scarcity governor | **Yes for Exotics**, optional for Rare; never for Superior and below. |
| 10 | Licences per character or per account | **Per character**, matching ownership records; licences never expire. |
| 11 | Numeric skills (better aim, faster surgery with experience) | **No.** Gate access, not stats; SS14 skill stays with the player. |
| 12 | Announce Exotic finds sector-wide | **No.** Keep it to the finder's popup and the admin log; a sector-wide announcement would make finders pirate bait. |
| 13 | Retroactive cleanup of items already in a live save | **Not needed yet**, since the server is not open; do phase 0 before launch so there is nothing to claw back. |

---

## Sources

- EVE invention and blueprint copies: [EVE University wiki: Invention](https://wiki.eveuniversity.org/Invention), [EVE support: Invention](https://support.eveonline.com/hc/en-us/articles/203210642-Invention), [EVE University wiki: Datacore](https://wiki.eveuniversity.org/Datacore)
- Elite Dangerous engineers and materials: [Elite: Dangerous/Engineers (Alioth wiki)](https://wiki.alioth.net/index.php/Elite_4/Engineers), [Odyssey materials cap discussion (Frontier forums)](https://forums.frontier.co.uk/threads/odyssey-materials-cap-issues-and-how-to-solve-it.628924)
- Star Wars Galaxies crafting, decay and vendors: [SWG Restoration crafting wiki](https://swgr.org/wiki/crafting), [SWG developer chat on item decay (archive)](https://gamespy-archives.quaddicted.com/sites/www.rpgplanet.com/starwars/articles/devchat3.htm), [Mark Jacobs on SWG-inspired economy (Massively OP)](https://massivelyop.com/2015/12/22/camelot-unchaineds-mark-jacobs-on-economy-crafting-and-star-wars-galaxies-inspiration/)
- Rust, ARK, Space Engineers, Albion and OSRS points are general knowledge of those games, not checked against current versions.
