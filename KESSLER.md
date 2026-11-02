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
- Fixes that would help any fork (making an entity saveable, saving state that was runtime-only, round-trip tests) are good candidates to send back upstream to Wayfarer, which shrinks our diff.

## Skills

`.claude/skills` is a git symlink to `.agents/skills`. On Windows checkouts without `core.symlinks` it is a plain text file, so the `wf-*` skills may not load automatically: read `.agents/skills/<name>/SKILL.md` directly when a task matches one.

## Persistence goal

Everything in the world survives a server restart exactly as it was left: items on the floor, spilled puddles, battery charge, paper text and stamps, machine upgrades, container contents, ships, POIs, player bodies. Transient things (projectiles in flight, half-finished do-afters, explosions in progress) may be settled or dropped at save time.

Decisions made so far:

- **Saves stay on the server.** Never send ship or world files to clients. (Triad Sector stores ship saves client-side, which forces it to sanitize and strip saves against tampering. That is why items vanish there. We don't copy that design.)
- **Restarts every few days** for updates. Long load times at restart (tens of minutes) are acceptable. Runtime tick performance is the main concern.
- **Architecture:** start with a whole-sector snapshot at restart, because cross-entity references (deed to ship, ID to station records, mind to body) then just work. Plan a persistent-ID layer so per-grid saves become possible later (incremental autosave, single-grid restore).
- **Offline players stay in the world** as SSD bodies. Cryo is optional. SSD bodies need stasis (no hunger or thirst while SSD). Start with a server rule against robbing SSD players; add a mechanic only if it's abused.
- **Inactive property is unloaded, never deleted.** Owners who haven't logged in for some days have their ships and bodies moved into storage and get them back on return. Scarce things (POI claims, docking spots) are released after a longer time, with contents returned to the owner's storage.
- **Upkeep fees: yes.** Insurance: undecided. Player-run shops on POIs with offline trading: planned for later.
- **Target population about 50.** Staff-run story arcs are a later addition, not part of the core loop.

## Persistence technical notes

- The engine (`RobustToolbox/Robust.Shared/EntitySerialization/`, `MapLoaderSystem`) saves and loads maps, grids and entities with all `[DataField]` state, containers included. `Content.Server/Administration/Commands/PersistenceSaveCommand.cs` (`persistencesave`) already saves a whole map.
- **Prototypes with `save: false` are silently skipped**, along with their children (see `EntitySerializer.IsSerializable`). This includes the player species `BaseMobFeroxi` and `MobShadekin`, `BaseMech`, `BaseVehicle`, borg chassis bases and `SpaceCashRandom*`. Each one needs review.
- Only `[DataField]` fields are saved. Runtime-only state resets on load and has to be found by testing.
- Loading must not re-run map-init on already-initialized entities, or fills and spawners run again and duplicate items.
- Each round currently builds the sector from scratch: `NfAdventureRuleSystem.Started` (`Content.Server/_NF/GameRule/`) spawns POIs, and the shipyard spawns ships from blueprints. Purchased ships become stations through `_station.InitializeNewStation` in `ShipyardSystem.Consoles.cs`.
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
