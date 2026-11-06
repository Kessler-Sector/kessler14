# Kessler Sector

Kessler Sector is a fully persistent Frontier-style SS14 server. This file adds the Kessler-specific context on top of [AGENTS.md](AGENTS.md), which is Wayfarer's agent guide and still applies except where this file overrides it.

## Repository

- **Upstream is Wayfarer** (`wayfarer` remote, `https://gitlab.com/projectwayfarer/wayfarer14.git`). Wayfarer forks Frontier, which forks Space Station 14, so changes from all three eventually arrive here.
- **Origin is GitHub** (`https://github.com/Kessler-Sector/kessler14`). Work happens in GitHub pull requests, not GitLab MRs: ignore the GitLab MR template and `.gitlab` references in AGENTS.md.
- **Upstream merges** go on a branch (`merge/wayfarer-YYYY-MM`) and land through a PR, so CI and save-compatibility checks run first. Never open a PR from a branch named `master`: `close-master-pr.yml` closes it automatically.
- CI was trimmed to build, tests, packaging and validators. There is no changelog bot yet, so the `:cl:` block is optional for now.
- Branding source files (logo) live in `Branding/`. They are not game resources.

## Where Kessler code goes

- New Kessler code goes in `_Kessler` folders that mirror the normal layout: `Content.Server/_Kessler/<Feature>/`, `Content.Shared/_Kessler/<Feature>/`, `Resources/Prototypes/_Kessler/`, `Resources/Locale/en-US/_Kessler/`, and so on. Namespaces follow the folder: `namespace Content.Server._Kessler.Persistence;`.
- Edits to files outside `_Kessler` (including `_WF` and `_NF`) are marked the same way AGENTS.md describes, but with `Kessler` instead of `Wayfarer`: `// Kessler`, `# Kessler: 3<4`, and `// Kessler: why` ... `// End Kessler` for blocks.
- Keep upstream edits minimal; prefer `partial` classes in `_Kessler` folders. Every edit to an upstream file is a future merge conflict.
- **`Content.Shared` and `Content.Client` run in the client sandbox.** Only types and members listed in `RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml` are allowed; anything else makes the client refuse to start. This has broken the client twice (`FormatException` in `GuidSerializer`, `StringComparer` in the damage serializer). Check new BCL types against that file, and run `SandboxTest` (part of the integration suite) before handing off any change to shared or client code.
- We don't send fixes upstream to Wayfarer for now; the focus is Kessler only. Generic fixes stay in this repo with Kessler markers, and upstreaming may be revisited someday.

## Skills

`.claude/skills` is a git symlink to `.agents/skills`. On Windows checkouts without `core.symlinks` it is a plain text file, so the `wf-*` skills may not load automatically: read `.agents/skills/<name>/SKILL.md` directly when a task matches one.

## Persistence goal

Everything in the world survives a server restart exactly as it was left: items on the floor, spilled puddles, battery charge, paper text and stamps, machine upgrades, container contents, ships, POIs, player bodies. Transient things (projectiles in flight, half-finished do-afters, explosions in progress) may be settled or dropped at save time.

Decisions made so far:

- **Saves stay on the server.** Never send ship or world files to clients. (Triad Sector stores ship saves client-side, which forces it to sanitize and strip saves against tampering. That is why items vanish there. We don't copy that design.)
- **Restarts every few days** for updates. Long load times at restart (tens of minutes) are acceptable. Runtime tick performance is the main concern.
- **Architecture:** start with a whole-sector snapshot at restart, because cross-entity references (deed to ship, ID to station records, mind to body) then just work. Plan a persistent-ID layer so per-grid saves become possible later (incremental autosave, single-grid restore).
- **Players still connected when a save loads** (a round restart rather than a process restart) are put straight back into their own bodies.
- **Offline players stay in the world** as SSD bodies. Cryo is optional. SSD bodies need stasis (no hunger or thirst while SSD). Start with a server rule against robbing SSD players; add a mechanic only if it's abused.
- **Inactive property is unloaded, never deleted.** Owners who haven't logged in for some days have their ships and bodies moved into storage and get them back on return. Scarce things (POI claims, docking spots) are released after a longer time, with contents returned to the owner's storage.
- **Upkeep fees: yes.** Insurance: undecided. Player-run shops on POIs with offline trading: planned for later.
- **Ownership and upkeep** (decided):
  - Every bought ship records its owner by account ID and the character's database profile ID, not by name. The deed stays the in-game key for selling and renaming.
  - Upkeep is **1% of the purchase price per real day**, billed straight to the owner's bank in the database, so it works while they're offline. Billing runs on real time, so restarts neither skip nor double-charge a day.
  - **Voucher (starter) ships pay no upkeep.**
  - Balances never go negative. Unpaid upkeep becomes debt on the ship; after **3 days** of debt the ship is impounded into storage until the debt is paid at a shipyard console.
  - Owners who haven't logged in for **7 days** have their ships moved into storage (their SSD body to cryo storage). **Storage is free.** Ships are recalled at any shipyard console.
  - Only the owner logging in counts as activity; crew using the ship do not.
  - Build order: owner records, then billing and debt, then storage. POI shops later reuse all three.
  - Storage as built: ships are saved to `kessler.storage.path` and listed on the sector map (`ShipStorageRegistryComponent`), so the list always matches the world. Stored files are never deleted, so restoring an older backup still works. A ship is only stored when nobody who might come back is aboard, the same rule as selling. Recall is a right-click option on any shipyard console with the owner's ID inserted.
  - Players away for 7 days have their SSD body put into cryo storage first (no ghost is made; they resume the character from the lobby), so their ship can then be stored.
  - **Cryo storage persists.** Frontier kept cryo'd bodies on a separate paused map and in memory, and deleted them after 3 days, so every cryo'd character would be lost at a restart. Bodies now sit in a paused container on a holder entity on the sector map, and the list of them is written onto it before each sector save (`BeforeSectorSaveEvent`). Expiry is off (`kessler.cryo.expire`).
  - Not done yet: crew who stay aboard an impounded ship can still block the impound.
- **Target population about 50.** Staff-run story arcs are a later addition, not part of the core loop.

## Persistence technical notes

- The engine (`RobustToolbox/Robust.Shared/EntitySerialization/`, `MapLoaderSystem`) saves and loads maps, grids and entities with all `[DataField]` state, containers included. `Content.Server/Administration/Commands/PersistenceSaveCommand.cs` (`persistencesave`) already saves a whole map.
- **Prototypes with `save: false` are silently skipped**, along with their children (see `EntitySerializer.IsSerializable`). `save` is inherited, and `BaseMob` sets it, so this covers every mob and player body plus mechs, vehicles and borgs (about 900 prototypes). Persistent saves go through `PersistenceSaveSystem` (`Content.Server/_Kessler/Persistence/`), which switches them to savable for the duration of the save, except transient ones (ghosts, `Audio`, effects with `TimedDespawn`, pointing arrows, AI eyes and holograms, jaunt forms). It also switches off a few savable prototypes that must not persist (`UnsavedPrototypes`, currently the FTL arrival effect). Map saves made by mappers are unchanged.
- **Map migrations** (`migration.yml`, `nf_migration.yml`, holiday replacements) rename or delete prototypes on every file read, and many target prototypes that still exist. Persistent saves are loaded through `PersistenceLoadSystem.TryLoadSave`, which only applies entries whose source prototype no longer exists.
- **Construction node `actions` re-run on every `ComponentStartup`**, including on load. Actions with side effects (emptying a solution, sounds, admin logs) must not be node actions on a node an entity rests on.
- **`ComponentInit` handlers that call `EnsureSolution` and cache the result** get a detached solution on load, because the entity is not flagged map-initialized yet. Look the solution up with `TryGetSolution` first (see the deep fryer fix).
- **On load, an entity starts up before the entities it contains are initialized.** The deserializer initializes and starts entities parent first, and `MetaDataFlags.InContainer` is not saved: the parent's `ContainerManagerComponent` sets it on its contents in its own `ComponentStartup`. Components start in the order they were added (prototype components first, then ones only in the save), so another component's `ComponentStartup` can see saved contents that are uninitialized and not flagged, and `Container.Contains` throws a debug assert (debug builds only). Components that add actions in `ComponentStartup` hit this (harpy singer and flight, night vision, ...); `ActionContainerSystem.EnsureAction` now accepts an uninitialized saved action. Covered by `HarpyKeepsSavedActions`.
- **The engine cannot serialize every type.** `Guid` had no serializer (`Content.Shared/_Kessler/Serialization/GuidSerializer.cs` adds one); classes stored in data fields must be `[DataDefinition]`. An entity whose component throws is dropped from the save, and the engine can still write its children, which then point at a missing parent and make the whole save fail to load. `PersistenceSaveSystem` re-serializes with that entity and everything inside it left out (references to it become invalid), logs which component threw, and `sectorsave`/round-end saves announce it to admins. A player body is still lost that way, so fix the component.
- **A broken save must never reach the map loader.** It creates the entities first and fails while starting them, and its cleanup of a failed load can itself throw and leave half the save running (stations twice, entities that never started). `PersistenceLoadSystem.TryValidateSave` checks every parent and container reference before loading, and `PersistentSectorSystem` deletes whatever a failed load left behind before generating a new sector.
- **Deleting an entity while a save is loading** (from a `ComponentInit` or `ComponentStartup` handler) makes the map loader abort the whole load, because it still holds that entity. Queue the deletion instead (see `MobStateActionsSystem`).
- **Loaded minds keep their player's user id**, but that player's data only exists once they connect. Code that looks up player data for a mind's user must tolerate it missing (see `MindSystem.SetUserId`).
- **A non-nullable `EntityUid` data field that the prototype leaves unset** logs `Encountered an invalid entityUid reference` on every save, because the engine writes each saved prototype's defaults to compare against. Integration tests fail on it; a real server only logs it. Make such fields nullable (see `WorldChunkComponent.Map`).
- **Auto-included nullspace entities** (minds, stations, the sector service entity) are written by `EntitySerializer.ProcessAutoInclude` without reserving YAML ids, so references to their children (a mind's job roles) break depending on hash order. `PersistenceSaveSystem` passes minds and stations as explicit roots and re-serializes if it still finds an auto-included entity with children.
- **Runtime-only links saved by accident:** `ActorComponent` (body to connected player) is stripped from persistent saves. NPC blackboards are not saved and lose their `Owner`; the sector service entity (sector bank, shuttle records, bounties, mail, ...) was referenced by nothing saved. `PersistenceLoadSystem` restores what has to be rebuilt after a load (NPC owners, bus stops, player data to mind links).
- **Returning players:** a loaded mind keeps its `UserId` and `OwnedEntity`, and upstream's reconnect path (`GameTicker.PlayerStatusChanged`) puts the player back into that body. Covered by `MindRoundTripTest`.
- Only `[DataField]` fields are saved. Runtime-only state resets on load and has to be found by testing.
  - **"Done once" flags must be data fields.** A `ComponentStartup` handler guarded by an unsaved flag runs again on every load. Medical bounty corpses got their damage and reagents a second time and rolled a new value; `BountyInitialized` and `MaxBountyValue` are now saved, and `Bounty` is a `ProtoId` (saves used to hold a full copy of the prototype). Covered by `MedicalBountyRoundTripTest`.
  - **Links between entities must be data fields.** The anomaly vessel to anomaly link (`AnomalyVesselComponent.Anomaly`, `AnomalyComponent.ConnectedVessel`) was lost, so vessels stopped earning points. Both are saved now, and the vessel refreshes its appearance on startup. Covered by `ScienceRoundTripTest`.
- **`AppearanceComponent` data is not saved** (only `appearanceDataInit`), and map-init does not run on load. A system that sets appearance only in `MapInitEvent` or in response to changes shows the prototype's default look after a load until something changes; refresh it in `ComponentStartup` (see the anomaly vessel).
- **`NetEntity` data fields are fine in persistent saves.** The engine's entity serializer writes them as save ids and maps them back to the loaded entities (`EntitySerializer`/`EntityDeserializer` implement `ITypeSerializer<NetEntity, ValueDataNode>`). Xeno artifact node graphs (`XenoArtifactComponent.NodeVertices`, `CachedActiveNodes`, `CachedSegments`, `XenoArtifactNodeComponent.Attached`) load intact. Covered by `ScienceRoundTripTest`.
- **Read-only data fields (`readOnly: true`) and custom serializers without a writer are skipped when writing**, so their state is lost on load. Damage was one (`DamageableComponent.Damage`); `DamageSpecifierDictionarySerializer` now writes non-zero entries under `types`, and `DamageSpecifier`'s wiki-only `types`/`groups` fields are read-only so they can't write stale or double-counted damage. Covered by `DamageRoundTripTest`.
- **Game time starts again at zero after every load.** A `CurTime` deadline saved as a plain `TimeSpan` lands hours in the future (dead drops) and `Timer.Spawn` callbacks are lost (express trade crates). In-world timers use `TimeOffsetSerializer`, which saves time remaining; real-world deadlines (upkeep, licences) use UTC `DateTime`. One-time setup in `ComponentInit` or `ComponentStartup` also runs on load, so it needs a saved flag (trade crates rolled a new destination on every load; bounty contracts were wiped). Covered by `RestartStateRoundTripTest`.
- Loading must not re-run map-init on already-initialized entities, or fills and spawners run again and duplicate items.
- `persistenceroundtrip` (Stage A) saves a map, loads it into a new paused map and writes a diff report to the server data folder. `Content.IntegrationTests/Tests/_Kessler/Persistence/RoundTripTest.cs` covers single entities through the same save and load path.
- **Stage B persistent sector** (`PersistentSectorSystem`), off by default: `kessler.persistence.enabled true`. Round start loads `<data>/kessler/persistence/sector.yml` in place of generating the game map (the adventure rule then only spawns POIs that live on their own maps); without a save it generates as usual. The sector is saved at round end, at shutdown, with `sectorsave`, and every `kessler.persistence.autosave_minutes` (default off). Each save keeps the previous one under `backups/` (`kessler.persistence.backups`, default 5) and writes `sector.meta.json` (format version, build) and `sector.snapshot.json`, which the next load is checked against. A save that fails to load is moved to `failed/` and a new sector is generated. Ships in FTL are landed before saving.
- **Measuring:** an empty server pauses itself (`game.auto_pause_empty`, default true) and runs no game logic, so measure with it off or a client connected. `tickstats [seconds]` prints entity-system tick time, `kessler.diagnostics.tick_log_seconds` logs it periodically, `persistenceactivity <mobs> <fires>` adds load on a dev server, and `Tools/_Kessler/soak.py` runs a long soak and writes a CSV.
- Without persistence each round builds the sector from scratch: `NfAdventureRuleSystem.Started` (`Content.Server/_NF/GameRule/`) spawns POIs, and the shipyard spawns ships from blueprints. Purchased ships become stations through `_station.InitializeNewStation` in `ShipyardSystem.Consoles.cs`.
- Already persistent in the database: bank balance (`Profile.BankBalance`), safety deposit boxes, corporations, community goals, roleplay levels. Safety deposit boxes serialize hand-picked fields only, so don't use them as the model for world persistence.
- Bank changes are written to the database immediately while the world saves periodically. A crash rollback must not let players keep both the money and the items.

## Update strategy

- Version every save (build/commit plus save-format version).
- Renamed or removed prototypes go in `Resources/migration.yml`. Changed component data gets a migration pass over the save YAML before loading.
- Loading should skip and log unknown entities rather than abort.
- CI should run a per-prototype save/load round-trip test and load a recent production save on every PR.
- Deploy procedure: back up the save → stop and save → update → load → automatic checks (entity counts per prototype, totals of key values) → roll back the build and save together on failure.

## Roadmap

1. **Stage A: in-process save/load diff.** A dev command snapshots the sector (entity counts per prototype; totals for battery charge, solution volume, stacks, cash; paper content), saves the map, loads it into a fresh map, snapshots again and diffs. No restart, no round-flow changes.
2. **Stage B: across a real restart.** A config option makes the round rule load the save instead of generating the sector. Check power, atmos, doors, shuttles and a player's body afterwards, and measure save time, load time and tick time with a full sector.
3. Character persistence, update infrastructure, ownership and upkeep, then the persistent-ID layer and per-grid saves.

## Progression and economy

The plan is in `docs/kessler/design/` (`README.md` combines the three design documents). Decisions made on 2026-11-06, numbered as in that plan:

- **Starting money: 10,000 for every character** (#2). Characters can't transfer money between each other; making a new character to try a different role must not cost anything.
- **No daily reputation cap for now** (#4). A soft cap was proposed; players dislike anything that feels like a wall.
- Everything else as the plan recommends:
  - no forced station work; a free Micro loaner from the scrapyard replaces the 50,000 rookie voucher;
  - ship resale 75%;
  - licences per character, never expiring, CGP may suspend;
  - wear from use only;
  - no numeric skills, no sector-wide announcement of Exotic finds;
  - raw gas cut now, saturation per station;
  - contracts pay the worker, with a 10–20% cut to the department;
  - NPC raids only on crewed ships outside safe zones, never destroying them;
  - outlaw payouts cut sharply;
  - gas deposits don't refill, fields rotate;
  - ship owners can take Dusk work orders at lower pay;
  - security contracts open to licensed non-CGP players;
  - living NPC patients; medical licences gate contracts and supplies, not actions;
  - authored conditions instead of Woundmed;
  - upper-tier chemistry recipes seeded per sector, never re-rolled;
  - a tech tree of 3–5 weeks per discipline;
  - rare, non-lethal outbreaks;
  - combat events at about 5 players.
- **Phase 0 as built:**
  - starting balance 10,000 (`HumanoidCharacterProfile.DefaultBalance`);
  - resale 0.75 (`shuttle.shipyard_base_sell_rate`);
  - `ShipVoucherKesslerLoaner` (Zipper, Ratrod, Canister, Rusty, Construct at the scrapyard console) replaces the rookie voucher;
  - raw gas prices cut, no 1.25 sale console bonus, gas sold by purity;
  - outlaw spesos rewards a fifth of what they were;
  - combat events from 5 players;
  - bluespace ore about a sixth as common (1 wall per cave expedition instead of 10);
  - T4 parts out of mail, 400 merchant tokens instead of 100, a fifth as common in dungeon loot;
  - mail carriers get 30% of a parcel's value (`kessler.mail.carrier_share`).
- **Reputation (phase 1, started):**
  - stored per character and track in the `kessler_reputation` table, so it counts while the character is offline, in cryo or stored;
  - levels at 120, 600, 1,800, 4,800 and 12,000 points;
  - no daily cap;
  - eight tracks (`Resources/Prototypes/_Kessler/Reputation/tracks.yml`);
  - awarded through `KesslerReputationSystem.TryAward`, at about 1 point per minute of useful work;
  - earned so far from delivering mail (Station, 5), medical bounties (Medical, 20) and completed expeditions (Expedition, 20, for everyone on the expedition);
  - `reputation` shows your own standing, and `reputationaward` lets admins give it;
  - licences come next.
- **Work-order board (phase 1):**
  - `ComputerWorkOrderBoard` keeps 3 or more generated jobs open (one more per 5 players online, up to 8);
  - cleaning jobs spill vomit, blood or oil around the station, and whoever is standing at a mess when it's cleaned is paid their share on the spot;
  - kitchen and bar jobs ask for a dish or drink, handed in by using it on the board;
  - pay is expected minutes × `kessler.workorders.rate` (150), straight to the worker, plus Station reputation;
  - orders expire after an hour in real time and are saved with the sector;
  - the board isn't placed on Dusk's map yet.
- **Pilot licences (phase 1):**
  - stored per character in the `kessler_licence` table, never expiring;
  - Pilot I (any track level 1, fee 2,000) allows Small ships;
  - Pilot II (level 2, Pilot I held 3 days, 15,000) allows Medium;
  - Pilot III (level 3, Pilot II held 7 days, 50,000) allows Large;
  - Capital (level 4, Pilot III held 14 days, 200,000) is needed for ships of 300,000 and more;
  - Micro ships need none; vouchers and the CGP, staff, Centcomm, Syndicate and custom shipyards are exempt;
  - `licence` lists them with what's missing, and `licence buy <id>` buys one;
  - shipyards refuse an unlicensed purchase (`kessler.licences.enforced`).
- **Gas market saturation:**
  - each station tracks the moles of each gas it has bought (`StationGasMarketComponent`, saved), decaying with a 24-hour half-life in real time;
  - the next mole sells for price / (1 + saturation / 50,000), so a whole very large plasma deposit sold at once pays about 70%, and a second one the same day pays less;
  - other stations pay full price, so hauling gas elsewhere pays;
  - cvars `kessler.market.gas_enabled`, `kessler.market.gas_depth` and `kessler.market.gas_half_life_hours`.
- **Cargo market ledger:**
  - pallet sales are priced per category of goods (`Resources/Prototypes/_Kessler/Market/categories.yml`: ore, refined materials, produce, food and drink); anything else sells at its normal price;
  - each station's price for a category falls the same way as gas (halving after the category's `depth` in base spesos) and recovers with a half-life of `kessler.market.cargo_half_life_hours` (24);
  - each station's demand per category drifts between 70% and 130% in small steps every six real hours, so the same goods pay more elsewhere;
  - goods in a crate are priced as those goods; a sale point outside any station uses its grid's market;
  - examining a pallet console shows what that station pays now; `kessler.market.cargo_enabled` turns it off.
  - the **Market board** PDA program (preinstalled on every PDA, `MarketBoardCartridge`) lists every station with a pallet console and its current rates; PDAs saved before it existed don't get it automatically.
- **Contract board (first slice of 5.2, procurement only):**
  - every station with a pallet console posts procurement contracts (`Resources/Prototypes/_Kessler/Contracts/procurement.yml`): steel, glass, plastic, plasteel, gold, uranium, HV cable, ore, produce, bruise packs, ointment;
  - a station keeps 2 open, plus 1 per 5 players online (up to 5), posting at most one every 10 minutes; open contracts are withdrawn after 3 real hours;
  - the **Contracts** PDA program (preinstalled) lists open contracts at every station and your own; a character holds at most 2, with 2 real hours to deliver;
  - deliver by selling the goods on a pallet at the issuing station (goods inside crates count); the pallet pays as usual, and the last delivery pays minutes x `kessler.contracts.rate` (150) into the holder's bank plus a point of reputation a minute on the contract's track;
  - abandoning or running out of time drops the contract with no penalty; contracts are saved on the station with real UTC times;
  - `kessler.contracts.enabled` turns posting off. Not yet: issuer budgets, per-character daily limits, licence gating, other contract types.
- **Not yet:**
  - expedition T4 blueprints are still permanent (limited-run blueprints come in phase 3);
  - the loaner is given on every spawn under 10 hours of playtime, not once per account;
  - combat events at low population are the full-size ones.

## Medical

Surgery comes from **Monolith's Shitmed** (`_Shitmed` folders), ported onto our older engine:

- **Shitmed (done):** body-part targeting, limb and organ surgery, amputation and reattachment, cybernetics, the medical biofabricator, and per body part damage in the health analyzer and MedTek cartridge.
- **Goob's injury layer ("Woundmed"): shelved for now.** Wounds, bones, traumas, pain and consciousness on top of Shitmed. It is cheapest to add before the server opens to players; afterwards every saved body would need converting. Most of its runtime state isn't saved yet, so porting it means adding `[DataField]`s, relinking after load and round-trip tests.
- **Biofabricator board:** found as medical loot; that is intended.
- **Medical bounties use surgery (first slice of M6):**
  - half of bounty corpses arrive with 1–2 arms or legs detached; the limbs go into the pod with the corpse;
  - each missing limb adds 6,000 to the bounty, lost again for every limb not attached at redemption;
  - cvars `kessler.medbounty.limb_chance`, `kessler.medbounty.max_limbs` and `kessler.medbounty.limb_bonus`;
  - base rewards are halved from Coyote's tripled values;
  - redeeming a bounty gives 20 Medical reputation.
- **Living NPC patients (first slice of M1 and M2):**
  - some wreck emergency medical pods (`MedicalPodPatient`) hold a living patient with a condition: poisoning, internal bleeding, liver failure or a foreign body (`Resources/Prototypes/_Kessler/Medical/conditions.yml`);
  - the pod is a stasis container (`PatientStasis`): nothing progresses and the patient doesn't need air until it's opened; a stasis bed slows conditions to a tenth;
  - each condition has stages that only run down while the server runs, stored as time left in the stage (no game time);
  - cures: poisoning needs the toxin's specific antidote (the toxins are inert marker reagents, so a blood sample names them); internal bleeding needs the new `Repair Internal Bleeding` surgery (clamp internal bleeders); liver failure needs a new liver (biofabricator BioSynth or a donor); a foreign body (a glass shard in the torso cavity) needs Cavity Implant surgery to take it out;
  - a cured patient is discharged at the medical bounty redemption machine for minutes x rate (less for damage left), plus one Medical reputation per minute;
  - admins can test with `spawnpatient [condition]`; guidebook page under Medical, "Living Patients".
  - **distress beacons** (station event `PatientDistressBeacon`, every 45 minutes at most, 1+ player): a scrap of wreck 800 to 2,500 tiles out with a patient's stasis pod, shown on radar in orange; medical radio gets its coordinates; the patient pays a 3,000 rescue bonus on discharge; the wreck is deleted after 90 real minutes unless someone is aboard.
- **Energy surgical tools** (energy scalpel, searing tool, mechanical pinches, `SMSurgery` pack): should be locked behind science. Wire them up during the science rework.

Port notes:
- Monolith's version of a file with `// Shitmed Change` markers was merged by hand; everything else of ours stays. Monolith-only features we don't have (cortical borer, abductors, goliath organ action, item tax) are left out.
- Our engine rejects `[AlwaysPushInheritance]` on plain text and number fields when parent and child both set them, so those flags are removed in `OrganComponent` and `BodyPartComponent`.
- Body slot names use spaces (`left arm`), not underscores, and the torso connects to the head. New species bodies must follow that.
- Missing prototypes Monolith keeps outside `_Shitmed` live in `Resources/Prototypes/_Kessler/Shitmed/ported.yml`.
- `surgery.can_operate_on_self` (default true) allows self-surgery, which matters at low population.
