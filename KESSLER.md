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
- **Read-only data fields (`readOnly: true`) and custom serializers without a writer are skipped when writing**, so their state is lost on load. Damage was one (`DamageableComponent.Damage`); `DamageSpecifierDictionarySerializer` now writes non-zero entries under `types`, and `DamageSpecifier`'s wiki-only `types`/`groups` fields are read-only so they can't write stale or double-counted damage. Covered by `DamageRoundTripTest`.
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
