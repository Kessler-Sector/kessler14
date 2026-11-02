---
name: wf-db-migrations
description: Change the Wayfarer database schema — EF Core models and paired Postgres/SQLite migrations in Content.Server.Database. Use when adding persistent data (per-player, per-character, or server-wide) or changing existing tables.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Database changes

Persistent data lives in `Content.Server.Database/` using Entity Framework Core. Servers run either Postgres or SQLite, so **every schema change needs a migration for both**.

## Before adding a table

Check whether existing persistence already fits: character profiles, preferences, or an existing `_WF` table. Wayfarer features with their own tables include safety deposit boxes and roleplay leveling; look at how they are modelled and queried before designing a new one.

## Steps

1. Edit the model in `Content.Server.Database/Model.cs` (shared entities). Provider-specific configuration goes in `ModelPostgres.cs` / `ModelSqlite.cs` only when really needed.
2. Generate both migrations from inside `Content.Server.Database/`:

   ```sh
   ./add-migration.sh WayfarerExampleFeature      # or add-migration.ps1 on Windows
   ```

   This runs `dotnet ef migrations add` for `SqliteServerDbContext` and `PostgresServerDbContext`. You need the `dotnet-ef` tool installed.
3. Prefix Wayfarer migration names with `Wayfarer` (for example `WayfarerSafetyDepositBox`, `WayfarerRoleplayLeveling`) so they are easy to tell apart from upstream ones.
4. Review both generated migrations and the updated model snapshots. Commit the `.cs`, `.Designer.cs`, and snapshot changes together.
5. Add the data access methods in the server DB manager layer, following how existing features read and write, and call them asynchronously from game systems.

## Rules

- Never edit or delete a migration that has already been merged; add a new one instead.
- Avoid destructive changes (dropping columns or tables with player data). If one is required, call it out clearly in the MR.
- New columns on existing tables need a sensible default so existing rows remain valid.
- Do not do database work on the game tick thread synchronously; use the async APIs.
- Upstream merges may also add migrations. If both sides added one, regenerate ours on top rather than hand-merging snapshots.
