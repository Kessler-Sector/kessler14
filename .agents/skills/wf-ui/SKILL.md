---
name: wf-ui
description: Build or change entity-bound UIs (Bound User Interfaces) and XAML windows in Wayfarer — UI keys, state, messages, the client BoundUserInterface, and the server handler. Use for consoles, machines, PDA-style windows, and any panel opened by interacting with an entity.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Bound User Interfaces and XAML

A BUI has five parts. `Content.*/_WF/SafetyDepositBox/` is a complete, current example of all of them.

| Part | Location | Example |
|---|---|---|
| UI key enum | Shared | `Content.Shared/_WF/SafetyDepositBox/BUI/SafetyDepositConsoleUiKey.cs` |
| State and messages | Shared | `.../BUI/SafetyDepositConsoleState.cs`, `.../Events/SafetyDepositMessages.cs` |
| Server handler | Server system | `Content.Server/_WF/SafetyDepositBox/SafetyDepositBoxSystem.cs` |
| Client BUI class | Client | `Content.Client/_WF/SafetyDepositBox/SafetyDepositConsoleBoundUserInterface.cs` |
| Window | Client | `SafetyDepositConsoleWindow.xaml` + `.xaml.cs` |

## Shared contract

```cs
[Serializable, NetSerializable]
public enum ExampleUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class ExampleState(int charges) : BoundUserInterfaceState
{
    public readonly int Charges = charges;
}

[Serializable, NetSerializable]
public sealed class ExampleUseMessage : BoundUserInterfaceMessage;
```

## Prototype wiring

```yaml
  - type: ActivatableUI
    key: enum.ExampleUiKey.Key
  - type: UserInterface
    interfaces:
      enum.ExampleUiKey.Key:
        type: ExampleBoundUserInterface
```

## Server

- Subscribe to `BoundUIOpenedEvent` and to each message, either with `SubscribeLocalEvent<Comp, Message>` or grouped with `Subs.BuiEvents<Comp>(ExampleUiKey.Key, subs => { subs.Event<...>(...); })`.
- Push state with `_ui.SetUiState(uid, ExampleUiKey.Key, state)` whenever the underlying data changes, not only on open.
- Validate every message: the actor (`args.Actor`), range, access, and cost. Clients can send any message at any time.

## Client

- In `Open()`, create the window with `this.CreateWindow<ExampleWindow>()` (it closes with the BUI automatically) and hook window events to `SendMessage(new ...Message())`.
- In `UpdateState`, cast to your state type and forward it to the window.
- Keep the window dumb: it displays state and raises events; it does not decide game outcomes.

## XAML

- Use `FancyWindow` (`Content.Client.UserInterface.Controls`) for consistency with existing windows.
- All visible text uses `{Loc 'key'}` in XAML or `Loc.GetString` in code, with keys in `Resources/Locale/en-US/_WF/`.
- Reuse existing `StyleClasses` (`LabelHeading`, `LabelSubHeading`, `AngleRect`, ...) instead of hardcoding colours or fonts.
- The `.xaml.cs` file is a `partial class` with `[GenerateTypedNameReferences]`; reference controls by their `Name`.

## Checking your work

Open the UI in-game. Check it at different window sizes, with missing or empty data, after the state changes while open, and with two players using it at once.
