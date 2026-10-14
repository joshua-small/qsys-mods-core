# Shared mod controls (0.1.4)

Core owns the binding engine, approved native-action replacements, and the `mods-core-controls` section of `preferences.json`. Better Shortcuts is an optional view/editor of that state. Feature mods must not attach competing replacement handlers or implement a second binding store.

## User flow

Better Packing's four native replacements are one group, enabled by default. **Preferences > Better Packing > Disable default Q-SYS control overrides** turns the entire group off. There is no per-action checklist or first-run approval for Packing. The feature owns this setting's UI; Core owns persistence and routing. Existing keys stay assigned, and UCI packing stays native.

General review remains available for other mods that propose optional key assignments or individually reviewed actions. After successful initialization, those new declarations are grouped into one **Review mod controls** window. **Apply selected** / **Apply all** plan the complete batch. When keys or an existing provider would be displaced, the view lists them and requires **Overwrite and apply**. Selecting two competing providers or two final actions for one key requires choosing one; enumeration/load order never decides. Key swaps are planned together. Unaffected key alternatives survive.

**Keep current** declines unreviewed proposals while keeping current choices and keys. **Review later** or closing the first-run window records delivery of the notification, without approving any change. The Tools > Q-SYS Mods > **Review Mod Controls… (N pending)** entry remains available. Decisions and notification delivery are keyed by declaration ID and revision, not assembly version. Increment revision only when the proposed behavior/keys materially change.

The **Mod Controls** Preferences page works without Better Shortcuts. Its page, menu entry and Better Shortcuts link are hidden unless a ready mod has reviewable proposals; feature-owned groups do not create an empty page. All pages share one draft. Preferences OK saves the shared document and Cancel discards it. To clear or reset an individual binding, use Better Shortcuts.

## Feature-owned replacement groups

During initialization call `ModControls.RegisterReplacementGroup("provider/default-controls", store => true)`, then set that ID on each replacement's `ControlProposal.ReplacementGroupId`. The callback supplies the default or reads a legacy feature preference; a saved group choice takes precedence. Only the owning mod can register group members. Groups may contain replacements, not proposed key changes.

Use `Preferences.RegisterWithControls` for a typed feature page that needs its shared `ControlDraft`. Add `ControlGroupPreference.Create(draft, groupId, "Disable default Q-SYS control overrides")` to render one opt-out checkbox. It stages the entire group and updates Better Shortcuts immediately. An overlapping provider produces an inline **Overwrite controls** / **Keep current** choice before staging. Overwriting any member of another group disables that entire group. No key assignment is changed by a provider toggle.

`ControlSettings.ReplacementGroups` is the sole active group store. `ModControls.GroupEnabled` and `PrepareGroup` expose it to custom UIs. Existing constructors remain compatible. Unknown groups survive save/reopen. Generic review excludes grouped declarations; group changes do not require four individual decisions.

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

When replacing an action, expose its native identity as the single binding target. Do not register an identically named standalone command for the same operation. To retire an old mod action, call `Commands.RegisterNativeAlias("provider/old-action", native)` during that provider's initialization. The alias creates no routed command or shortcut row. Only aliases from ready providers with discovered native targets are migrated.

On load, saved alias keys merge with the native action's effective keys, deduplicated by physical key/modifiers. This includes restoring an explicitly cleared native action after the user assigned its key to the old duplicate. Empty aliases do not clear native defaults; explicit native clears remain clears if there are no transferred keys. Distinct native/user alternatives and matching editor overwrite consent survive. Unregistered or failed-provider aliases remain untouched. Migration changes the in-memory draft only; the normal atomic save persists it, and repeat loads are idempotent.

Register the action first, then call `ModControls.RegisterShortcut` with a `ControlProposal` whose `Binding` is a canonical key string (`Ctrl+Alt+L`, semicolon-separated alternatives, or empty to clear). A proposed shortcut is not a `ModCommand.DefaultGesture`: leave the latter null if the user must review the initial assignment. Existing default-gesture registration remains supported for backward compatibility.

Use `ModCommand.When` for a readable execution condition; `CanExecute` remains the actual predicate. `ModControls.Describe` reports effective provider/context and separate availability details. `ModControls.Bindings.Display` resolves user assignments and original defaults. Provider names must not be inferred from arbitrary command labels.

Menus consume the same effective bindings after a save. Registered command menu items receive hints automatically. For a native click handler or another native action that represents the same operation, call `Commands.RegisterMenuHint(menuItem, modCommandId)` during initialization. It accepts only the declaring mod's registered actions, preserves the host command and click behavior, and combines applicable native/mod alternatives without duplicates. Clear/Reset/remap update the hint; unavailable providers restore the host hint. Display labels such as `Ctrl+,` never replace serialized WPF identities such as `Ctrl+OemComma` in settings.

## Persistence and migration

On first load, Core reads old Better Shortcuts `Overrides` and `EditorOverrides` into its shared state. It retains unknown/absent commands and explicit editor takeover consent. The old section stays untouched for rollback history but is no longer read once the shared section exists. Loading does not write; Preferences OK or a first-run decision/dismissal saves the shared section with the existing atomic settings store and stale-document check.

Better Packing defaults on, including fresh installs. A saved legacy `ReplaceDefaultPackBehavior=false` is preserved when `ControlsManagedByCore` is false. Four previous explicit off decisions migrate to one off group; partial selections become the default on group. Once saved, the group choice wins and old per-action records remain historical. A new default cannot silently displace an already selected provider. Conflicting new default groups both stay off until chosen in their mod preferences. No user preferences are modified by the build/install process itself.

Deploy Core and Better Shortcuts **0.1.4 together**, with Better Packing **0.1.3**. Core refuses pre-0.1.1 versions of the two feature mods because they own conflicting handlers. Other existing command registrations retain the original binary-compatible `ModCommand` constructor. Before a public release, the signed catalog must enforce minimum Core **0.1.4** for Shortcuts and **0.1.3** for Packing; current 0.1.x package policy predates these APIs. This branch does not publish a release or change the signed catalog.

## Validation

`tests/Controls` links production registry, planner, binding engine, router, review and storage code with a minimal fake host. Its 124 checks run under Windows WPF without displaying windows or starting Designer. They cover menu hints/formatting/remapping, alias migration, explicit transfer, review confirmation, swaps, competing/disjoint providers, failed/absent providers, original-key retention, editor takeover restoration, persistence, stale saves and exactly-once routing. Existing settings-store checks remain separate. This is offline evidence, not Designer acceptance.

Scoped Designer 10.5 checks on 2026-10-14 with matching 0.1.3 builds confirmed one unchecked Packing opt-out, no empty Mod Controls page, one assigned native row per Pack direction, source/key updates across pages, Clear/Reset/OK/reopen and Cancel preserving the preferences file. On a disposable two-component schematic, Ctrl+Shift+L/R/T/B each executed Better Packing exactly once with Gap 20 and exact final-position receipts. Ctrl+Shift+L used Q-SYS when the opt-out was enabled. All operation and insertion changes were undone; defaults were restored.

The owner subsequently confirmed recorder capture and conflict handling in Designer. Core/Shortcuts 0.1.4 checks verified the saved Preferences `Ctrl+,` menu label, both Tools hints and all View > Sidebars/theme hints. The All Sidebars and Dark Mode menu actions worked; their shortcuts restored the original UI. Installation and these UI checks preserved the preferences file byte-for-byte.

Remaining Designer coverage includes Pack Tools/right-click entry points, a remapped Pack physical key, UCI passthrough, invalid selection/text focus, and Core + Packing without Better Shortcuts. Generic review remains covered by offline fixtures until another provider needs it. Use separate disposable preferences for fresh-install behavior rather than clearing personal preferences. The native driver is adapted but has not been run for this change.
