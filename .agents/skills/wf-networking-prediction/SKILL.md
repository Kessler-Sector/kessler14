---
name: wf-networking-prediction
description: Network component state and write predicted shared logic correctly in Wayfarer. Use when a client needs server data, when an interaction should feel instant, when adding network events, or when debugging mispredicts, doubled popups or sounds, and client/server desync.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Networking and prediction

The server is always the authority. The client sees server state through networked components and, for predicted code, runs the same shared logic ahead of the server so actions feel instant.

## Getting data to the client

- Mark the component `[NetworkedComponent, AutoGenerateComponentState]` and each synced field `[AutoNetworkedField]`.
- After changing a networked field, call `Dirty(uid, comp)` (or `Dirty(ent)`). Forgetting this is the most common reason the client shows stale data.
- Only network what the client actually uses. Server-only data (owner IDs, database keys, timers nobody sees) stays unnetworked or in a server-only component.
- Use `NetEntity` (via `GetNetEntity` / `GetEntity`) when sending entity references in network messages; never send a raw `EntityUid`.

## Network events

- Define them in Shared: `[Serializable, NetSerializable] public sealed class FooEvent : EntityEventArgs`.
- Server sends with `RaiseNetworkEvent`; receivers subscribe with `SubscribeNetworkEvent`.
- Treat every message from a client as untrusted. Re-check range, access, ownership, and costs on the server.

## Predicted logic

Put logic in `Content.Shared` when the player who triggers it should see the result immediately (using an item, toggling something, opening/closing). Rules:

- Use the predicted feedback APIs so the acting player does not get the effect twice:
  - `_popup.PopupPredicted(message, uid, user)` / `_popup.PopupClient(...)`
  - `_audio.PlayPredicted(sound, uid, user)`
- Code that must only happen once (spawning, deleting, logging, randomness that changes state) should run on the server or be guarded with `_timing.IsFirstTimePredicted` / `_net.IsServer`.
- Randomness in predicted code must be deterministic on both sides; do not call `IRobustRandom` from predicted paths without an established seeded pattern.
- Keep timers as `TimeSpan` from `IGameTiming.CurTime` stored in a networked field, not accumulated per tick.

## Things that must stay on the server

Money (`_NF` bank system), spawning and deleting entities, access changes, admin logs, database writes, round rules, and anything that grants an item or advantage.

## Checking your work

- Test with a real client and server (`./runserver.sh`, `./runclient.sh`). Watch for duplicated popups or sounds, UI that snaps back, and values that flicker.
- If something only works on the server, check that the component is networked, the field is `AutoNetworkedField`, and `Dirty` is called.
