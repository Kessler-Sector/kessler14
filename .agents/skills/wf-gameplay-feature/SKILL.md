---
name: wf-gameplay-feature
description: Plan and build a Wayfarer gameplay feature end to end — components, entity systems, events, prototypes, and locale — in the right assembly and folder. Use when adding a new mechanic, item behaviour, machine, or system, or when unsure where a piece of game logic belongs.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Building a gameplay feature

A typical feature touches four places: a component (data), a system (logic), a prototype (YAML that puts the component on an entity), and locale (player-facing text). Plan all four before writing code.

## 1. Find what already exists

This repo contains upstream SS14, Frontier (`_NF`), and code from many other forks. Before creating anything, search for the mechanic by name and by likely component names across `Content.*` and `Resources/Prototypes`. If a fork folder (`_NF`, `_DV`, `_Mono`, ...) already implements something close, extend it there instead of building a parallel version in `_WF`.

## 2. Decide which assembly owns each piece

| Put it in | When |
|---|---|
| `Content.Shared` | Components the client needs, events and messages crossing the network, BUI keys/state/messages, and any logic that should be predicted (feels instant for the player) |
| `Content.Server` | Anything authoritative: spawning, money, access, round flow, admin logs, database, server-only components |
| `Content.Client` | Visuals, overlays, XAML windows, `BoundUserInterface` classes |

Shared code must never reference server or client types. If a server system needs a shared base, follow the existing `SharedFooSystem` (abstract, in Shared) + `FooSystem` (in Server/Client) pattern.

New code goes in `_WF/<Feature>/` inside each assembly, with namespaces matching the folder, for example `Content.Shared._WF.SafetyDepositBox.Components`.

## 3. Components hold data only

```cs
namespace Content.Shared._WF.Example;

/// <summary>
/// One-line description of what this component marks or stores.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExampleComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Charges = 3;

    [DataField]
    public SoundSpecifier? UseSound;
}
```

- Use `[DataField]` for anything set from YAML. Use `ProtoId<T>` / `EntProtoId` for prototype references, not raw strings.
- Only add `NetworkedComponent` and `AutoNetworkedField` when the client actually reads the field. After changing a networked field on the server, call `Dirty(uid, comp)`.
- No methods with game logic in components.

## 4. Systems hold the logic

```cs
public sealed class ExampleSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExampleComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<ExampleComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ...
    }
}
```

- React to existing events (`UseInHandEvent`, `InteractUsingEvent`, `ExaminedEvent`, `ComponentStartup`, `MapInitEvent`, ...) rather than polling in `Update`.
- If `Update` is genuinely needed, iterate with `EntityQueryEnumerator<T>()` and track the next fire time instead of doing work every tick.
- Respect `args.Handled` and set it when you consume an event.
- For your own events, define them in Shared as a `[ByRefEvent] record struct` (local) or a `[Serializable, NetSerializable]` class (network).

## 5. Prototype and locale

Add the component to an entity under `Resources/Prototypes/_WF/`, and any player-facing strings to `Resources/Locale/en-US/_WF/`. See the `wf-prototypes-locale` skill.

## 6. Editing existing systems

If the feature needs hooks into an existing upstream system, follow the `wf-upstream-edits` skill: prefer a `.Wayfarer.cs` partial file, and mark any in-place edits.

## 7. Verify

Build, run `Content.Tests`, and run the integration tests or YAML linter if you touched prototypes. See the build table in `AGENTS.md`, and the `wf-tests` skill if the feature deserves a test.
