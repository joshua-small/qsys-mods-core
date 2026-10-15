# Shared mod controls (0.1.5)

Core owns the binding engine, approved native-action replacements, and the `mods-core-controls` section of `preferences.json`. Better Shortcuts is an optional view/editor of that state. Feature mods must not attach competing replacement handlers or implement a second binding store.

## Registration and default conflicts

Feature mods declare stable commands and default shortcuts with Core. Better
Shortcuts edits and explains shared state; it is not required for dispatch,
default assignment, conflict resolution or persistence. Core resolves defaults
even when Better Shortcuts is absent.

At inspected baseline `beafd57` (0.1.4), `Commands.Register` rejected duplicate
IDs but did not check key collisions. Later, `ControlBindings.Validate` rejected
conflicting effective keys before application. This prevented an ambiguous apply
but could abort the whole binding refresh. Successful registration did not mean
a default was conflict-free.

This source candidate changes default resolution:

- Existing native keys and explicit saved assignments take precedence.
- A colliding mod default stays unassigned. Two competing mod defaults both
  stay unassigned; load order does not choose a winner.
- Known direct native/editor input bindings and top-level menu access keys also
  prevent default takeover. Undiscovered scopes, OS-reserved keys and embedded
  editor internals remain validation boundaries.
- `ModControls.Bindings.DefaultConflicts(overrides)` exposes command, key and
  competing owners. Better Shortcuts or another inspector can consume it; this
  Core change does not modify Better Shortcuts' UI.
- Conflicting explicit assignments still fail validation. Intentional overwrite
  and reset use Core's existing prepare/review/apply plan, preserving unrelated
  alternatives. The live JSON interface rejects unresolved explicit conflicts.
  Known native/editor InputBindings and menu access keys also require matching
  `EditorOverrides` consent, including chord prefixes. A reviewed Reset that
  takes one of those keys stores an explicit assignment so the chosen default
  becomes active instead of being suppressed again.

`ModCommand.DefaultGesture` declares an ordinary default. Leave it null and use
`ModControls.RegisterShortcut` for a reviewed proposal. Native replacements retain
the native action identity/current keys; do not create a duplicate action with
the same keys. The integrated chord API uses `ModCommand.DefaultBinding` and
`Commands.RegisterDefaultBinding` on canonical native actions. The same default
precedence applies to legacy gestures, chords and a single key that is also a
chord prefix. Different chord completions can share a prefix.

The combined suite has 177 WPF checks, including default collisions, precedence,
reporting, reviewed reset, menu access keys, chord parsing and prefix conflicts.
See [live settings validation](SETTINGS-JSON.md) for disposable-Designer evidence.

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

Register the action first, then call `ModControls.RegisterShortcut` with a `ControlProposal` whose `Binding` is a canonical sequence string (`Ctrl+Alt+L`, `Ctrl+K > C`, semicolon-separated alternatives, or empty to clear). A proposed shortcut is not a default assignment: leave `DefaultGesture` and `DefaultBinding` null if the user must review the initial assignment. Existing default-gesture registration remains supported for backward compatibility.

Use `ModCommand.When` for a readable execution condition; `CanExecute` remains the actual predicate. `ModControls.Describe` reports effective provider/context and separate availability details. `ModControls.Bindings.Display` resolves user assignments and original defaults. Provider names must not be inferred from arbitrary command labels.

Menus consume the same effective bindings after a save. Registered command menu items receive hints automatically. For a native click handler or another native action that represents the same operation, call `Commands.RegisterMenuHint(menuItem, modCommandId)` during initialization. It accepts only the declaring mod's registered actions, preserves the host command and click behavior, and combines applicable native/mod alternatives without duplicates. Clear/Reset/remap update the hint; unavailable providers restore the host hint. Display labels such as `Ctrl+,` never replace serialized WPF identities such as `Ctrl+OemComma` in settings.

## Chords and default bindings (0.1.5)

`ControlSequence` contains one or two physical `ControlStroke` values. Serialize strokes with ` > ` and alternatives with `; `: `Ctrl+K > C; Ctrl+F8`. UI labels use **then** and **or**. Number-row and keypad keys remain distinct. A first stroke needs Ctrl, Alt, Windows or a function key. An ordinary-key command must explicitly set `AllowOrdinaryKey` and a non-null `ShortcutContext`; a chord completion may be an ordinary key. Bare Escape cancels and cannot be a completion.

Set `ModCommand.DefaultBinding` during registration for a mod command. Use `Commands.RegisterDefaultBinding(native, binding, providerName, when, context)` during initialization for a canonical native action; do not create a duplicate command just to give it keys. Only a ready provider supplies defaults. The existing positional `ModCommand` constructor remains binary-compatible. Use reviewed proposals for competing providers of the same native default.

Saved user assignments, including explicit clears, win. New managed defaults yield to conflicting native, legacy-mod or user keys; competing managed defaults both remain inactive. Resolution never writes preferences and does not choose a winner by load order. Better Shortcuts explains inactive defaults in row details and can review/apply all mod defaults as one batch. An explicit reset or assignment uses the conflict planner. Matching whole sequences and single-stroke/chord-prefix collisions are warned about before overwrite; unrelated alternatives and absent-mod settings survive.

The window input router shows valid completions without moving focus. It cancels on Escape, an invalid completion, a mouse click, focus/window changes, settings reapplication or a ten-second timeout. Invalid completions are consumed, not replayed into an editor. Repeated held keys cannot execute twice. Provider readiness, host state, command availability and context are checked again before execution. Text entry is excluded unless the command opts in; browser-hosted input is not an exhaustive supported scope.

Native main-menu and discovered context-menu items display the same effective sequences. Context hints are refreshed when opening and restored on close. Top-level native Alt menu access keys participate in overwrite warnings, as do discovered editor input bindings. Class bindings, OS shortcuts and future editor scopes are not an exhaustive conflict inventory. There is no global keyboard hook.

Binding format 2 accepts sequences. Format 1 single-key settings still load without an install-time rewrite. A future unknown format is rejected rather than guessed. Downgrading a profile containing chords requires restoring its prior preferences backup.

## Persistence and migration

On first load, Core reads old Better Shortcuts `Overrides` and `EditorOverrides` into its shared state. It retains unknown/absent commands and explicit editor takeover consent. The old section stays untouched for rollback history but is no longer read once the shared section exists. Loading does not write; Preferences OK or a first-run decision/dismissal saves the shared section with the existing atomic settings store and stale-document check.

Better Packing defaults on, including fresh installs. A saved legacy `ReplaceDefaultPackBehavior=false` is preserved when `ControlsManagedByCore` is false. Four previous explicit off decisions migrate to one off group; partial selections become the default on group. Once saved, the group choice wins and old per-action records remain historical. A new default cannot silently displace an already selected provider. Conflicting new default groups both stay off until chosen in their mod preferences. No user preferences are modified by the build/install process itself.

Deploy Core and Better Shortcuts **0.1.5 together**, with Better Packing **0.1.3**. Core refuses pre-0.1.1 versions of the two feature mods because they own conflicting handlers. Other existing command registrations retain the original binary-compatible `ModCommand` constructor. Before a public release, the signed catalog must enforce minimum Core **0.1.5** for Shortcuts and **0.1.3** for Packing; current 0.1.x package policy predates these APIs. This branch does not publish a release or change the signed catalog.

## Validation

`tests/Controls` links production registry, planner, binding engine, router, review and storage code with a minimal fake host. Its combined checks run under Windows WPF without displaying windows or starting Designer. They cover default-conflict precedence/reporting, sequence parsing/cancellation, prefix conflicts, custom-binding preservation, native and context-menu chord hints, alias migration, explicit transfer, review confirmation, swaps, competing/disjoint providers, failed/absent providers, original-key retention, editor takeover restoration, persistence, stale saves and exactly-once routing. Timer/focus handlers are wired to the tested cancellation state; these offline checks alone do not establish physical event delivery. Existing settings-store checks remain separate.

Scoped Designer 10.5 checks on 2026-10-14 with matching 0.1.3 builds confirmed one unchecked Packing opt-out, no empty Mod Controls page, one assigned native row per Pack direction, source/key updates across pages, Clear/Reset/OK/reopen and Cancel preserving the preferences file. On a disposable two-component schematic, Ctrl+Shift+L/R/T/B each executed Better Packing exactly once with Gap 20 and exact final-position receipts. Ctrl+Shift+L used Q-SYS when the opt-out was enabled. All operation and insertion changes were undone; defaults were restored.

The owner subsequently confirmed recorder capture and conflict handling in Designer. Core/Shortcuts 0.1.4 checks verified the saved Preferences `Ctrl+,` menu label, both Tools hints and all View > Sidebars/theme hints. The All Sidebars and Dark Mode menu actions worked; their shortcuts restored the original UI. Installation and these UI checks preserved the preferences file byte-for-byte.

Remaining Packing coverage includes Tools/right-click entry points, a remapped Pack physical key, UCI passthrough, invalid selection/text focus, and Core + Packing without Better Shortcuts. Generic review remains covered by offline fixtures until another provider needs it. Use separate disposable preferences for fresh-install behavior rather than clearing personal preferences. These remaining Packing paths are separate from the synthetic Core-only single-key/chord dispatch and conflict checks recorded in [live settings validation](SETTINGS-JSON.md).

Scoped 0.1.5 physical-key checks confirmed new page/UCI/variable chords, the completion overlay, category expansion/focus, UCI toolbox insertion with the tab inactive, the new panel modifiers and right-dock toggle, Script Programmer Mode and Component Controls Info. The complete observations and remaining adapter/remap checks are in [Better Shortcuts acceptance](https://github.com/joshua-small/qsys-better-shortcuts/blob/codex/shortcut-table/docs/DESIGNER-VALIDATION-10.5.md). Personal preferences remained byte-for-byte unchanged after the coordinated local installation. This is Designer-only evidence, not live Core or hardware acceptance.
