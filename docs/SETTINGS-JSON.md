# Live settings.json interface

This source candidate adds live settings for the hash-guarded Designer 10.5.0.0
host. It is not a public Q-SYS API. Offline checks and scoped Designer acceptance
are recorded below; release remains a separate step.

Open **Tools > Q-SYS Mods > Open Settings JSON…**. The default file is
`%LOCALAPPDATA%\QsysMods\Designer-10.5\settings.json`; lab hosts use their
existing `mods-lab-settings` directory. The OS file association chooses the
editor. Adjacent `settings.schema.json` supplies native choices and registered
mod types to JSON-aware editors such as VS Code.

## Editing

Save to apply. Core polls on the UI thread every 750 ms and requires two identical
reads, normally applying after about 1.5 seconds. Comments and trailing commas
are accepted. Duplicate keys, unknown root/native fields, wrong types, unsupported
versions and files over 2 MiB are rejected. All providers validate before any
provider applies. Shared shortcuts use Core's validator; file edits do not
silently transfer another action's assigned key.

Taking a discovered native/editor InputBinding or top-level menu access key
requires the same sequence in that command's `Bindings.EditorOverrides` entry.
This includes a chord whose first stroke claims that key. Better Shortcuts and
Core's review planner supply this consent after a reviewed overwrite; a direct
JSON edit must declare it explicitly too.

```json
{
  "$schema": "./settings.schema.json",
  "schemaVersion": 1,
  "designerVersion": "10.5.0.0",
  "qsys": {
    "general": { "PeriodicHoldoffMinutes": 5 },
    "hardLinks": []
  },
  "mods": {}
}
```

This is a partial update, not factory defaults. Omitted native fields and mod
sections retain their current values. A supplied mod section replaces that whole
typed section; retain its required properties. Empty collections clear their
lists. Missing fields do not mean Reset to Factory Defaults.

Preferences changes are projected back into the file. Those writes produce
formatted JSON, replacing comments/custom formatting. Unknown mod sections
survive. The existing `preferences.json` remains Core's internal mod store for
compatibility; do not edit both files as competing authorities. Shortcut state
is in `mods.mods-core-controls`; the old `better-shortcuts` section is migration
history. Packing's current toggle is in `ReplacementGroups` in shared controls,
not its legacy `ReplaceDefaultPackBehavior`/`ControlsManagedByCore` fields.

## Native scope

The adapter exposes 20 General preference properties:

- Auto save: `Mode`, `PeriodicHoldoffMinutes`.
- General: `Theme`, `ScriptEditor`, `DefaultScriptAccess`, `AssetDirectory`,
  `EnableNewSchematicElements`.
- HoverMon: `AudioApi`, `AudioBuffer`, `AudioQuality`.
- Diagnostics/notifications: `CollectPerformanceData`, `DisableNotifications`.
- Proxy: `ProxyServerEnabled`, `ProxyAddress`.
- Signal tracing: `TraceWireStyle`, `HighlightComponents`, `TraceColorAudio`,
  `TraceColorVideo`, `TraceColorControl`, `TraceColorMediacast`.

Enums use exact host names shown in the generated schema. Colors use uppercase
`#AARRGGBB`. Trace styles use the host converter's choices; proxy addresses use
the native validator. Save timeout is 0–1999999999 minutes; audio buffer must be
nonnegative. Host/device-dependent behavior still needs application checks.

`qsys.hardLinks` contains `Name`, `IpAddress` and `IsEnabled` per link. Native
reserved/invalid IPs and duplicates are rejected. The adapter uses the native
manager and verifies its saved JSON because that manager catches IO errors.
Unexposed per-link extension data survives edits to the same IP. Removing a link
intentionally removes its data.

Hidden proxy credentials, hidden automatic-asset-install settings and Advanced
feature flags are excluded. No arbitrary application state, credentials, window
layouts or design properties are dumped.

[Q-SYS 10.5 Preferences](https://help.qsys.com/Content/Schematic_Library/preferences.htm)
documents the user-facing categories. Property names, `GeneralSettings.Save`,
Hard Links persistence and validator mapping come from static inspection of the
exact binaries guarded by `Host`; these are private implementation contracts.

## Invalid files, concurrency and recovery

Invalid saves leave working settings active and preserve the edited file.
**Settings JSON Error…** shows the failure; diagnostics record the error without
dumping values. Correct and save, or choose **Retry Settings JSON** for an
unchanged candidate. Failed candidates do not reapply on every timer tick.

Sync pauses while native Preferences is open or the main window is disabled.
If both the file and live Preferences changed, Core refuses to choose a winner.
Reconcile values in the file, then choose Retry to acknowledge applying that
file against the current state. Closing Preferences alone does not acknowledge
the conflict.

One Designer owns `settings.json.session.lock` for the projection's lifetime.
A second instance reports the unavailable lease. Atomic projection writes check
original text and retain `.previous`. `settings.last-good.json` contains the
last successfully applied snapshot, also with a prior backup. Removing the
file does not reset preferences; Open Settings JSON recreates it from live state.

Native settings span several vendor stores: this is **not one crash-atomic
transaction across Q-SYS and mods**. On failure, Core attempts to restore affected
native values, bindings and its own successful mod-store write. A failed stale
write is never rolled back over a different process's newer preferences. If
recovery fails, sync stops and reports the problem. Review the recovery file and
restart Designer; the invalid candidate remains available for correction.
A stopped service will not recreate a deleted settings file from unverified
live values or replace the last-good snapshot. An existing file can still open
for inspection while the recovery error remains visible.

## Mod integration

Use `Preferences.Register` or `Preferences.RegisterWithControls`. Native pages
and JSON share typed validation and the registered validator. Validation must
not mutate live state. Successful JSON changes publish `ModSettings<T>.Changed`.
Unknown/unloaded mod sections remain inert JSON until their provider is present.

`SharedSettings.PathName`, `Error`, `Changed`, `Open()` and `Retry()` expose the
interface. The schema helps an editor; runtime provider validation is authoritative.
`SettingsSynchronizer`/`ISettingsTarget` separate file handling from host adapters
for failure testing.

## Validation and integration boundary

`tests/SettingsJson` covers debounce, JSONC, malformed/oversized/duplicate input,
failed application/recovery, simultaneous edits, a single-process lease, round
trips, missing files, unknown sections and stale writes. `tests/SettingsBridge`
runs production registration and adapters with fake native persistence, including
schema references, notifications, native swallowed save errors and stale-save
recovery. Existing settings-store and WPF controls checks also pass.

The branch incorporates chord commit `3ac0ea4`: legacy gestures, declared native
defaults and chords use the same conflict policy, including prefix collisions.
Explicit unresolved conflicts in JSON reject the entire edit before side effects.

34 checks in disposable Designer 10.5 on 2026-10-14 use the guarded fixtures in
[`tests/LiveSettings`](../tests/LiveSettings/README.md). It covers native/mod JSON
application, Preferences pause/resume and reverse projection, malformed and invalid
edits, duplicate and prefix conflicts, default suppression, user precedence,
reviewed overwrite, native-menu takeover consent/reset and chord remapping. The actual Better Shortcuts page verifies
conflict ownership, Cancel, staged overwrite, recorder candidates, OK and menu/JSON
updates. Those recorder checks call its methods in the real Designer process;
they are distinct from physical keyboard checks.

The first native fault injection found that General Save swallowed an I/O error
and left only the in-memory value changed. Core now verifies persisted General
values and both Hard Link files before acceptance, and verifies compensating
recovery. Locking Prefs.xml or the Hard Link names sidecar now rejects the edit,
restores prior live values, and preserves last-good recovery. Clearing a Hard Link
name survives native reload. An unchanged startup file also repairs a missing
recovery snapshot.

PR review added regressions for direct JSON bypass of native/editor overwrite
consent, a reviewed Reset that left its default inactive, and recreating a missing
file after recovery failure. Core now validates takeover consent at the shared
save boundary, makes reviewed takeovers explicit, and preserves the fault state
and last-good snapshot. Native direct InputBindings are covered even when absent
from the registered action's InputGestures.

Physical keyboard input verified exactly-once dispatch after explicit conflict
transfer, the waiting overlay, and a chord remapped through JSON. Restarting with
Better Shortcuts absent preserved native theme/audio-buffer/trace-color values,
mod settings and Core-owned binding state. Physical single-key and two-stroke
dispatch also passed in that Core-only session, each executing its owner once.
Source/fixture
details and scoped receipts are described in the live-test guide. Personal native
and mod preference files are hash-checked independently of the disposable profile.
The live bridge refuses to initialize in a marked lab whose native roaming/local
preference paths still point outside the disposable application directory.

Release builds complete with zero warnings/errors. Offline checks: 40 live-file,
38 Windows bridge, 177 Windows WPF controls and 8 existing store checks. This is
source and scoped application acceptance, not a release or installation into the
regular Designer. Audio-engine/hardware behavior, every General setting, all
editor/floating/UCI scopes, and a competing live process remain outside this
acceptance. Simultaneous edits, lease contention and recovery failure have offline
coverage. Recovery across native stores is compensating, not crash atomic.
