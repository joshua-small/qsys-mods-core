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

### Document history in operation windows (0.1.8)

Modeless mod windows that edit the main document can opt into `ModControls.Bindings.AttachDocumentHistory(window)`. Core forwards only native Undo/Redo to the main window, retaining native availability checks. Effective history shortcuts are read from the same binding store, including remaps, alternatives, chords and cleared assignments. A live binding change cancels pending chords. Text boxes, editable combos, password fields and recognized editors keep their own undo behavior; disabled windows or modal-disabled owners cannot forward document edits. Registration is idempotent and removed on close or Core disposal.

This is an explicit operation-window capability, not automatic routing for preferences or other dialogs. Better Packing 0.3.0 is the initial consumer. The shared-controls harness passes 217 checks, including history forwarding, local text undo, live remaps/clears, pending-chord cancellation, blocked-owner handling, duplicate native gestures and close cleanup. Those checks use unshown WPF trees. Separately, physical Ctrl+Z and Ctrl+Y with the Better Packing window focused restored the exact pre-pack and post-pack positions in isolated Designer 10.5, preserving all nine control-wire identities and attachments. The first physical run exposed duplicate native Undo gestures; deduplicating effective history gestures fixed that failure. See the [physical acceptance record](https://github.com/joshua-small/qsys-better-packing/blob/main/docs/PHYSICAL-WIRE-ACCEPTANCE.md#feedback-update-physical-acceptance).

`ControlSequence` contains one or two physical `ControlStroke` values. Serialize strokes with ` > ` and alternatives with `; `: `Ctrl+K > C; Ctrl+F8`. UI labels use **then** and **or**. Number-row and keypad keys remain distinct. A first stroke needs Ctrl, Alt, Windows or a function key. An ordinary-key command must explicitly set `AllowOrdinaryKey` and a non-null `ShortcutContext`; a chord completion may be an ordinary key. Bare Escape cancels and cannot be a completion. Escape release also cancels an armed chord if its key-down was intercepted before reaching the window.

Set `ModCommand.DefaultBinding` during registration for a mod command. Use `Commands.RegisterDefaultBinding(native, binding, providerName, when, context)` during initialization for a canonical native action; do not create a duplicate command just to give it keys. Only a ready provider supplies defaults. The existing positional `ModCommand` constructor remains binary-compatible. Use reviewed proposals for competing providers of the same native default.

Saved user assignments, including explicit clears, win. New managed defaults yield to conflicting native, legacy-mod or user keys; competing managed defaults both remain inactive. Resolution never writes preferences and does not choose a winner by load order. Better Shortcuts explains inactive defaults in row details and can review/apply all mod defaults as one batch. An explicit reset or assignment uses the conflict planner. Matching whole sequences and single-stroke/chord-prefix collisions are warned about before overwrite; unrelated alternatives and absent-mod settings survive.

The window input router shows valid completions without moving focus. It cancels on Escape, an invalid completion, a mouse click, focus/window changes, settings reapplication or a ten-second timeout. Invalid completions are consumed, not replayed into an editor. Repeated held keys cannot execute twice. Provider readiness, host state, command availability and context are checked again before execution. Text entry is excluded unless the command opts in. This includes native text boxes, editable selectors, WebView ancestry and the docked Legacy Actipro SyntaxEditor (including a focused EditorView descendant). Other browser/editor hosts are not an exhaustive supported scope.

Single-key mod shortcuts use those same guards; they cannot bypass `AllowInText`
through the command binding used by menus. Native commands retain their own WPF
route unless Core manages their default/context/sequence. The native floating
Component Controls scope follows presentation-source changes after loading and
permits only opted-in mod commands. Detached Actipro script tabs are not an
attached Core scope; opting into text does not promise global OS/window routing.

Native main-menu and discovered context-menu items display the same effective sequences. Context hints are refreshed when opening and after a binding change while the menu remains open; closing restores the menu's original presentation. Designer 10.5's schematic, UCI-edit and component/control icon menus share the native PopupToolBar template rather than a MenuItem context menu. Its string tooltips show the same effective bindings, update while open, and restore their original text on unload or Core disposal. Rich tooltip objects and native commands remain untouched. Top-level native Alt menu access keys participate in overwrite warnings, as do discovered editor input bindings. Class bindings, OS shortcuts and future editor scopes are not an exhaustive conflict inventory. There is no global keyboard hook.

Binding format 2 accepts sequences. Format 1 single-key settings still load without an install-time rewrite. A future unknown format is rejected rather than guessed. Downgrading a profile containing chords requires restoring its prior preferences backup.

## Persistence and migration

On first load, Core reads old Better Shortcuts `Overrides` and `EditorOverrides` into its shared state. It retains unknown/absent commands and explicit editor takeover consent. The old section stays untouched for rollback history but is no longer read once the shared section exists. Loading does not write; Preferences OK or a first-run decision/dismissal saves the shared section with the existing atomic settings store and stale-document check.

Better Packing defaults on, including fresh installs. A saved legacy `ReplaceDefaultPackBehavior=false` is preserved when `ControlsManagedByCore` is false. Four previous explicit off decisions migrate to one off group; partial selections become the default on group. Once saved, the group choice wins and old per-action records remain historical. A new default cannot silently displace an already selected provider. Conflicting new default groups both stay off until chosen in their mod preferences. No user preferences are modified by the build/install process itself.

The coordinated public versions are Core **0.1.6**, Better Packing **0.2.0** and Better Shortcuts **0.2.0**. Both feature packages require Core **>=0.1.6 and <0.2.0**. Their new 0.2 package ranges preserve every historical 0.1.x approval and pin; public Core 0.1.5 is also retained unchanged. Core refuses pre-0.1.1 versions of the two feature mods because they own conflicting handlers. Other existing command registrations retain the original binary-compatible `ModCommand` constructor. The earlier 0.1.5/0.1.3 application results below remain attributed to those local builds; a version-only packaging change does not add application acceptance.

## Validation

### Native icon menus and Legacy editor, 2026-10-15

The [exact companion report and fixture](https://github.com/joshua-small/qsys-better-shortcuts/blob/d7a6017fc7afb7de432bbaf76f144c25825e2c02/docs/DESIGNER-VALIDATION-10.5.md#native-icon-menus-and-legacy-editor-2026-10-15) cover schematic/UCI edit menus and emulating component/control variants in isolated Designer 10.5.0. All 28 command tooltips follow live remap/Clear/Reset with stable native identities and availability; the noncommand Unlock hint stays native. Owner hover confirms the rendered Align Left chord tooltip. Representative native Align/Duplicate/Undo, UCI Pack/page-center/Lock/Unlock clicks pass. Hidden/disabled button properties are distinguished from visible execution.

This follow-up found a real text guard defect: a physical ordinary mod shortcut executed with the docked Legacy EditorView focused. The guard now recognizes its SyntaxEditor ancestry. The regression failed before the fix; all 201 Core WPF controls checks and four native Legacy routed-event assertions pass afterward. Separate external input confirms ordinary single/chord exclusion, explicit single/chord opt-in, typing/Undo/Redo/Find and Escape-release cancellation. Final physical counters are ordinary single/chord 0 and opted-in single/chord 1 each; singles used helper input and chords used owner input. F5 stops at the unnamed-Core validation message without deployment; F6/F7 enter local emulation and return to Design, retaining native gestures. Key receipts and observed state transitions support those function-key results; no separate command-entry receipt is claimed. Computer Use remained stopped after the test Escape.

Component Copy All/Paste All is **blocked**, not accepted: Paste All remains disabled even with owner mouse input. The native custom clipboard format is present but its typed read returns null with and without the binding engine. A separate process with no mods loaded reproduces the native BinaryFormatter clipboard rejection while plain text succeeds. The [guarded diagnostic](https://github.com/joshua-small/qsys-better-shortcuts/tree/d7a6017fc7afb7de432bbaf76f144c25825e2c02/tests/NativeClipboardRepro) documents that scope; no vendor patch or unsafe runtime workaround was applied. Keep the blocker in [issue #4](https://github.com/joshua-small/qsys-mods-core/issues/4).

Personal Prefs.xml, both HardLink files and mod preferences match their same-start hashes byte-for-byte after the disposable process stopped, including custom bindings. No restoration, regular installation, release or hardware deployment occurred. Detached script windows remain unsupported; other browser hosts and runtime UCI popups are not accepted by this run. US-only layout and no reserved shortcuts remain the owner's scope. Existing feature proposals stay open.

### Previous accepted checkpoints

The 2026-10-15 physical editor follow-up used unchanged product code from Core `41d8c98` and Shortcuts `dc6e64e`. The [exact companion fixture and report](https://github.com/joshua-small/qsys-better-shortcuts/blob/3fbd8f7300fe1798df9de28d4223947f6fbf9942/docs/DESIGNER-VALIDATION-10.5.md#physical-editor-and-native-function-key-acceptance-2026-10-15) accept docked UCI Monaco and the native Control Name validating textbox in isolated Designer 10.5.0. Windows helper input verified editing, Monaco Undo/Redo/Find, property select-all/commit/document Undo, ordinary-single exclusion and opted-in single dispatch. Owner-entered chords verified ordinary-prefix exclusion and exactly-once opt-in in both editors, plus Monaco Escape cancellation. Receipts identify the actual focused controls; routed-event checks are not substituted for this input.

With Monaco focused, native F5 reached the unnamed synthetic Core validation message without deployment; F6 entered local emulation and F7 returned to Design. Native effective bindings and gesture collections remained F5/F6/F7. F7 acceptance is supported by its key receipt and the visible/recorded state transition; no separate Disconnect command-entry receipt was captured. The fixture built with zero warnings/errors. After stopping the disposable process, personal Prefs.xml, both HardLink files and mod preferences matched their same-start hashes byte-for-byte, including custom bindings. No product code, regular installation or public release changed. This closes the scoped editor/function-key item in [issue #4](https://github.com/joshua-small/qsys-mods-core/issues/4); other browser surfaces, unsupported detached script windows, additional native icon templates and separate feature proposals are not accepted by this run.

The 2026-10-14 automated context/layout follow-up (America/Los_Angeles) passed 181 Core WPF controls checks and 92 assertions in a fresh profile-redirected Designer 10.5.0 copy. It exercises root/nested schematic and UCI routes, text and docked Monaco opt-ins/exclusions, held keys, shared prefixes, availability, focus/click/page/modal/timeout cancellation, reopened floating Component Controls, swapped/mixed docks, sidebar focus and F2. The companion [Shortcuts acceptance fixture](https://github.com/joshua-small/qsys-better-shortcuts/tree/e1696c1ad3dab6fb6c974cd8374987a353cdd3b4/tests/DesignerAcceptance) runs with `-Contexts`. Its native Copy fixture menu verifies actual context-opening dispatch and live remap/Clear/Reset while open. The open-menu refresh regression failed before the Core fix; closing still restores original presentation. A fixture menu does not establish every Designer icon-menu template.

The 2026-10-15 follow-up passed **194 Core WPF checks** and **95 native automated assertions**, including cancellation when Escape key-down never reaches the floating route. Use the [updated companion fixture](https://github.com/joshua-small/qsys-better-shortcuts/tree/4143e2b239d1059f12e50971258a63a8e1727e43/tests/DesignerAcceptance) and its [scoped acceptance report](https://github.com/joshua-small/qsys-better-shortcuts/blob/4143e2b239d1059f12e50971258a63a8e1727e43/docs/DESIGNER-VALIDATION-10.5.md#floating-component-controls-and-native-icon-toolbar-2026-10-15) for this run; the earlier fixture above contains 92 assertions. The actual schematic `PopupToolBar` was opened in Designer: its Copy tooltip property followed live JSON remap, Clear, Reset and reopen while retaining the native command identity. This extends the fixture-menu evidence to that native icon toolbar; it does not cover every native template or claim a separate tooltip-hover rendering check.

Automated application/routed-event acceptance remains distinct from physical input. The reviewed Shortcuts PR #5 accepts physical recorder/save. The owner-assisted follow-up also accepts populated floating Component Controls single-key/chord delivery, ordinary-command exclusion, close/reopen without duplicate handlers and Escape cancellation. The physical cancellation retest contained both Escape down/up; the missing-down fallback is independently covered by the WPF and native automated regressions. The earlier missing key-down cannot be attributed to a particular host or external input layer. The owner uses US layout only and requests no reserved shortcuts, so alternate layouts/AltGr and OS-reserved combinations are outside this acceptance scope and remain untested. Detached Actipro script windows remain unsupported; the fixture verifies that exclusion without adding routes. Personal preferences, including custom bindings, matched their same-start hashes after the disposable instance stopped. No public release or regular installation is implied by these tests.

`tests/Controls` links production registry, planner, binding engine, router, review and storage code with a minimal fake host. Its combined checks run under Windows WPF without displaying windows or starting Designer. They cover default-conflict precedence/reporting, sequence parsing/cancellation, prefix conflicts, custom-binding preservation, native and context-menu chord hints, alias migration, explicit transfer, review confirmation, swaps, competing/disjoint providers, failed/absent providers, original-key retention, editor takeover restoration, persistence, stale saves and exactly-once routing. Timer/focus handlers are wired to the tested cancellation state; these offline checks alone do not establish physical event delivery. Existing settings-store checks remain separate.

Scoped Designer 10.5 checks on 2026-10-14 with matching 0.1.3 builds confirmed one unchecked Packing opt-out, no empty Mod Controls page, one assigned native row per Pack direction, source/key updates across pages, Clear/Reset/OK/reopen and Cancel preserving the preferences file. On a disposable two-component schematic, Ctrl+Shift+L/R/T/B each executed Better Packing exactly once with Gap 20 and exact final-position receipts. Ctrl+Shift+L used Q-SYS when the opt-out was enabled. All operation and insertion changes were undone; defaults were restored.

The owner subsequently confirmed recorder capture and conflict handling in Designer. Core/Shortcuts 0.1.4 checks verified the saved Preferences `Ctrl+,` menu label, both Tools hints and all View > Sidebars/theme hints. The All Sidebars and Dark Mode menu actions worked; their shortcuts restored the original UI. Installation and these UI checks preserved the preferences file byte-for-byte.

Remaining Packing coverage includes Tools/right-click entry points, a remapped Pack physical key, UCI passthrough, invalid selection/text focus, and Core + Packing without Better Shortcuts. Generic review remains covered by offline fixtures until another provider needs it. Use separate disposable preferences for fresh-install behavior rather than clearing personal preferences. These remaining Packing paths are separate from the synthetic Core-only single-key/chord dispatch and conflict checks recorded in [live settings validation](SETTINGS-JSON.md).

Scoped 0.1.5 physical-key checks confirmed new page/UCI/variable chords, the completion overlay, category expansion/focus, UCI toolbox insertion with the tab inactive, the new panel modifiers and right-dock toggle, Script Programmer Mode and Component Controls Info. The complete observations and remaining adapter/remap checks are in [Better Shortcuts acceptance](https://github.com/joshua-small/qsys-better-shortcuts/blob/codex/shortcut-table/docs/DESIGNER-VALIDATION-10.5.md). Personal preferences remained byte-for-byte unchanged after the coordinated local installation. This is Designer-only evidence, not live Core or hardware acceptance.
