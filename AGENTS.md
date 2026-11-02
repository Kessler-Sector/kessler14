# Agent Guide for Wayfarer 14

This file orients AI coding agents (Claude Code, Codex, Cursor, Copilot, etc.) working in this repository. Human contributors should read [CONTRIBUTING.md](CONTRIBUTING.md) first; this guide summarizes it and adds the context an agent would otherwise have to guess.

Anyone submitting AI-assisted work is bound by [AI_POLICY.md](AI_POLICY.md): the contributor must review, test, and be able to explain every line. Write code a maintainer can review quickly.

## What this repository is

Wayfarer 14 is a fork of [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14), which is itself a fork of [Space Station 14](https://github.com/space-wizards/space-station-14). It is built on the RobustToolbox engine in C# (.NET 9).

- "Upstream" means Frontier. Frontier's own changes live in `_NF` folders.
- The repository also carries code ported from many other forks. Each lives in its own underscore-prefixed folder (`_DV`, `_Mono`, `_Goobstation`, `_EE`, `_Corvax`, and others), plus the older `Nyanotrasen`, `DeltaV`, and `Floof` folders. [LEGAL.md](LEGAL.md) lists every one with its source and license.
- Wayfarer's own content lives in `_WF` folders.

| Path | Contents |
|---|---|
| `Content.Shared/` | Components, events, and logic used by both client and server, including predicted code |
| `Content.Server/` | Server-authoritative game logic |
| `Content.Client/` | Rendering, UI (XAML), and client-only presentation |
| `Content.Server.Database/` | EF Core models and migrations (Postgres and SQLite) |
| `Content.Tests/`, `Content.IntegrationTests/` | NUnit unit and integration tests |
| `Resources/Prototypes/` | YAML prototypes (entities, reagents, roles, shuttles, ...) |
| `Resources/Locale/en-US/` | Fluent (`.ftl`) localization strings |
| `Resources/Textures/`, `Resources/Audio/` | RSI sprites and sounds |
| `Resources/Maps/` | Station, outpost, POI, and shuttle maps |
| `RobustToolbox/` | Engine, as a git submodule. Do not edit it. |

## Where new things go

Anything created from scratch goes under a `_WF` folder that mirrors the normal layout:

- `Content.Server/_WF/<Feature>/...`, `Content.Shared/_WF/<Feature>/...`, `Content.Client/_WF/<Feature>/...`
- `Resources/Prototypes/_WF/...`, `Resources/Locale/en-US/_WF/...`, `Resources/Textures/_WF/...`, `Resources/Audio/_WF/...`, `Resources/ServerInfo/_WF/...`
- Namespaces follow the folder: `namespace Content.Server._WF.Shipyard;`

If a feature already lives in another fork's folder (`_NF`, `_DV`, `_Mono`, ...), extend it in place rather than duplicating it under `_WF`. Search before creating anything: this codebase has many forks' versions of similar systems, and the right one to extend may not be the first one you find.

Ship and point-of-interest data uses Wayfarer-specific prototypes: `VesselPrototype` in `Resources/Prototypes/_WF/Shipyard` and `PointOfInterestPrototype` in `Resources/Prototypes/_WF/PointsOfInterest`. Copy an existing one when adding a new ship or POI.

## Editing files that are not ours

Any change to a C#, YAML, or FTL file outside `_WF` must be marked with comments, so the change survives the next upstream merge. Follow the exact style from CONTRIBUTING.md:

- Single changed or added line: `// Wayfarer` (C#) or `# Wayfarer` (YAML) at the end of the line.
- Changed value: record the old and new value, `fireRate: 4 # Wayfarer: 3<4`.
- Block of added code: open with a comment that says why, close with `End Wayfarer`:

  ```cs
  // Wayfarer: ensure signature colour is consistent
  if (TryComp<StampComponent>(uid, out var stamp))
      stamp.StampedColor = state.Color;
  // End Wayfarer
  ```

- Commented-out upstream code: `# - Bucket # Wayfarer`.
- FTL does not allow comments on the same line as a value. Put the comment on the line above: `# Wayfarer: "Job Whitelists"<"Role Whitelists"`.
- When porting a change from another repo, note the source PR in the comment.

Keep upstream edits as small as possible. For larger additions to an upstream C# class, prefer a `partial` class in a `_WF` folder over editing the original file. Never modify `RobustToolbox/`. If an MR shows `RobustToolbox` as changed, revert it.

## Coding conventions

Match the surrounding file. In particular:

- File-scoped namespaces, 4-space indentation for C#, 2 spaces for YAML (see `.editorconfig`).
- Components are data only: `[RegisterComponent]`, `sealed partial class`, fields marked `[DataField]`. Add `[NetworkedComponent]` and `[AutoGenerateComponentState]` / `[AutoNetworkedField]` when the client needs the data. All logic lives in an `EntitySystem`.
- Systems get dependencies with `[Dependency] private readonly FooSystem _foo = default!;` and subscribe to events in `Initialize()`.
- Prefer existing events and APIs (`TryComp`, `Resolve`, `Entity<T>`, `EntityQueryEnumerator`) over hand-rolled lookups. Avoid per-tick work when an event will do.
- If logic should feel instant to the player (interactions, popups, sounds, UI), put it in `Content.Shared` so it is predicted. Use the predicted APIs (`PopupPredicted`, `PlayPredicted`) and guard against repeated effects during re-prediction. Keep anything that grants money, spawns items, or touches the database on the server.
- Shared code must not reference `Content.Server` or `Content.Client`.
- UI uses XAML (`.xaml` + `.xaml.cs`) with a `BoundUserInterface`. Shared code defines the UI key, state, and messages, and the server validates every message it receives.

## Content conventions

- **No hardcoded player-facing text.** Every popup, examine string, UI label, and chat message goes through `Loc.GetString("key")`, with the key defined in `Resources/Locale/en-US/` (`_WF/` for our own). Other locales are maintained separately and are not required.
- **Prototype IDs** are PascalCase. Inherit from an existing parent rather than repeating components, and check whether an existing parent or abstract prototype already covers the case.
- **Renaming** a prototype ID, component, or `DataField` breaks maps, saved data, and other forks' YAML. Search every use (including `Resources/Maps/`) before renaming, and mention the rename under "Breaking changes" in the MR.
- **Sprites** are RSI folders with a `meta.json` that has the correct `license` and `copyright`. Sounds need an entry in the folder's `attributions.yml`.
- **AI-generated art, sprites, or audio are not allowed.** Agents may write code and YAML, but must not create, generate, or source image or sound assets.
- **Maps** are large generated YAML files. Do not hand-edit them except for trivial, targeted fixes, and never reformat them. Ask before touching a map: concurrent map edits cannot be merged.

## Licensing and porting

The project is AGPLv3 (see [LEGAL.md](LEGAL.md)). When bringing in code from another repository:

- Confirm the source license is compatible (AGPLv3, GPLv3, or MIT, as listed in LEGAL.md). If it is unclear or restrictive, stop and ask.
- Keep ported code in a folder named for its source fork (for example `_Mono`) and add a LEGAL.md row if the folder is new.
- Keep author credit: use `git cherry-pick` where possible so the original author is preserved, and cite the source PR in edit comments and the MR.
- Keep asset license metadata (`meta.json`, `attributions.yml`) intact.

## Database changes

Schema changes need a migration for both providers. From `Content.Server.Database/`, run `./add-migration.sh <Name>` (or `add-migration.ps1`), which generates both the Sqlite and Postgres migrations. Never edit an existing migration.

## Building and testing

```sh
git submodule update --init --recursive    # first time only
dotnet restore
dotnet build --configuration DebugOpt --no-restore /m
```

Run the smallest check that covers your change, and say which ones you ran:

| Change | Check |
|---|---|
| C# | Build, then `dotnet test --no-build --configuration DebugOpt Content.Tests/Content.Tests.csproj -- NUnit.ConsoleOut=0` |
| Gameplay systems, prototypes, or maps | `dotnet test --no-build --configuration DebugOpt Content.IntegrationTests/Content.IntegrationTests.csproj -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed` (slow; narrow it with `--filter` while iterating) |
| Prototype YAML | `dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj --configuration Release` |
| Sprites | `python RobustToolbox/Schemas/validate_rsis.py Resources/` (needs `pillow` and `jsonschema`) |
| Maps | `python Tools/validate_mapfiles.py` |

To try a change in-game, run `./runserver.sh` and `./runclient.sh` (or the `.bat` files on Windows).

## Merge requests

Development happens on GitLab. Use the template in `.gitlab/merge_request_templates/Default.md`:

- Explain what changed and why, how to test it, and list breaking changes.
- Add media for in-game changes.
- Include a changelog block for player-visible changes. It is validated in CI:

  ```
  :cl:
  - add: Added fun!
  - tweak: Changed fun!
  - fix: Fixed fun!
  - remove: Removed fun!
  ```

- Do not edit `Resources/Changelog/*.yml` by hand; CI generates it.
- Check the diff for unrelated whitespace, line-ending, or submodule changes before submitting.
- Keep each MR to one feature or fix.

## Skills

Task-specific guides live in `.agents/skills/` (`.claude/skills` links to the same folder). If your tool does not load them automatically, read the matching `SKILL.md` before starting:

| Skill | Use for |
|---|---|
| `wf-gameplay-feature` | New mechanics: components, systems, events, and where each piece goes |
| `wf-networking-prediction` | Networked state, predicted shared logic, client/server desyncs |
| `wf-ui` | Bound User Interfaces and XAML windows |
| `wf-prototypes-locale` | YAML prototypes and Fluent localization |
| `wf-upstream-edits` | Changing files outside `_WF`, partial classes, merge conflicts |
| `wf-porting` | Bringing code or assets in from other forks |
| `wf-tests` | Writing and running unit and integration tests |
| `wf-db-migrations` | Database schema changes |
| `wf-ships-pois` | Shipyard vessels and points of interest |
| `wf-sprites-audio` | RSI sprites, sounds, and asset licensing |

## Working style for agents

- Read the code you are about to change, and its callers, before editing. Follow the existing pattern in that area even if you would design it differently.
- Do not invent APIs. If you are not sure a method, component, or prototype exists, search for it.
- Make the smallest change that solves the task. Do not reformat, rename, or "clean up" unrelated code.
- Do not commit, push, or open MRs unless the contributor asks.
- When you finish, report what you changed, which checks you ran and their results, and anything you could not verify.
