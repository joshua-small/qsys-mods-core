# Shared mod controls (0.1.1)

Core owns the binding engine, approved native-action replacements, and the `mods-core-controls` section of `preferences.json`. Better Shortcuts is an optional view/editor of that state. Feature mods must not attach competing replacement handlers or implement a second binding store.

## User flow

After successful initialization, new declarations are grouped into one **Review mod controls** window. **Apply selected** / **Apply all** plan the complete batch. When keys or an existing provider would be displaced, the view lists them and requires **Overwrite and apply**. Selecting two competing providers or two final actions for one key requires choosing one; enumeration/load order never decides. Key swaps are planned together. Unaffected key alternatives survive.

**Keep current** declines unreviewed proposals while keeping current choices and keys. **Review later** or closing the first-run window records delivery of the notification, without approving any change. The Tools > Q-SYS Mods > **Review Mod Controls… (N pending)** entry remains available. Decisions and notification delivery are keyed by declaration ID and revision, not assembly version. Increment revision only when the proposed behavior/keys materially change.

The **Mod Controls** Preferences page works without Better Shortcuts. Its changes share one draft with Better Shortcuts. Apply there stages a batch; Preferences OK saves the shared document and Cancel discards it. Unchecking a replacement disables it; unchecking a shortcut proposal leaves its current keys alone. To clear or reset an individual binding, use Better Shortcuts.

## Declaring replacements

Register during `Mods.Register(providerId, initialize)`. `ControlProposal.Id` must start with `providerId/`; duplicate IDs are rejected. Preserve the original native identity with `Commands.NativeId(native)`. Pass `Binding: null` for an implementation replacement. Core does not copy native keys to the mod's independent action IDs.

```csharp
var native = Commands.Native("PackLeft");
ModControls.RegisterReplacement(
    new("example/pack-left", "example", "Example Mod", "1",
        Commands.NativeId(native), "Pack Left", "Schematic; valid unlocked selection",
        Scope: ControlScope.Schematic),
    native, ReadContext, target => Pack(target));
```

`ReadContext` returns `OutsideScope` for a host surface retaining native behavior, `Blocked` for invalid focus/selection within the replacement's guarded domain, or `Ready` with the target object. The router reads context again at execution. It marks the event handled before mutation; exceptions cannot execute the native action afterward. UCI and schematic are known disjoint scopes; `Any` overlaps both. Scope declarations must agree with the predicates. If multiple accepted providers nevertheless match at runtime, execution fails closed.

The current choice applies to native keyboard, menu and context-menu entry points. It retains all current keys, including user overrides. An off, unloaded or failed provider falls back to Q-SYS; saved choices survive temporary absence. Core tracks successful initialization separately from registration. A registered command belonging to a failed provider cannot execute or retain an active key claim.

## Proposing shortcuts

Register the action first, then call `ModControls.RegisterShortcut` with a `ControlProposal` whose `Binding` is a canonical key string (`Ctrl+Alt+L`, semicolon-separated alternatives, or empty to clear). A proposed shortcut is not a `ModCommand.DefaultGesture`: leave the latter null if the user must review the initial assignment. Existing default-gesture registration remains supported for backward compatibility.

Use `ModCommand.When` for a readable execution condition; `CanExecute` remains the actual predicate. `ModControls.Describe` reports effective provider/context and separate availability details. `ModControls.Bindings.Display` resolves user assignments and original defaults. Provider names must not be inferred from arbitrary command labels.

## Persistence and migration

On first load, Core reads old Better Shortcuts `Overrides` and `EditorOverrides` into its shared state. It retains unknown/absent commands and explicit editor takeover consent. The old section stays untouched for rollback history but is no longer read once the shared section exists. Loading does not write; Preferences OK or a first-run decision/dismissal saves the shared section with the existing atomic settings store and stale-document check.

Better Packing migrates a saved legacy `ReplaceDefaultPackBehavior` choice for all four declarations. Its new `ControlsManagedByCore` marker prevents a later save from being misread as a legacy opt-in. Installations without saved Packing preferences are treated as undecided and receive review. A saved decision is never overwritten by migration, including after a declaration revision changes. No user preferences are modified by the build/install process itself.

Deploy Core, Better Packing and Better Shortcuts **0.1.1 together** for this test build. Core refuses to initialize older versions of the two feature mods because they own conflicting handlers. Other existing command registrations retain the original binary-compatible `ModCommand` constructor. Before a public release, the signed catalog must enforce the new feature modules' minimum Core version; current 0.1.x package policy predates this API. This branch does not publish a release or change the signed catalog.

## Validation

`tests/Controls` links production registry, planner, binding engine, router, review and storage code with a minimal fake host. It runs under Windows WPF without displaying windows or starting Designer. It covers migration, explicit transfer, review confirmation, swaps, competing/disjoint providers, failed/absent providers, original-key retention, editor takeover restoration, persistence, stale saves and exactly-once routing. Existing settings-store checks remain separate. This is offline evidence, not Designer acceptance.

Owner Designer testing should cover: first-run review and Review later; re-open from the pending menu; all four Pack actions through keys, Tools and right-click; a remapped Pack key; UCI passthrough; invalid selection/text focus; toggling replacement off; Better Shortcuts source/key updates across pages; conflict confirmation and Cancel; Preferences Cancel/OK/reopen; and running Core + Packing without Better Shortcuts. Use a separate disposable preferences fixture for genuine first-install behavior rather than clearing personal preferences. The native driver is adapted but has not been run for this change.
