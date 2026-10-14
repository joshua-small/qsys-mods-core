# Live settings.json interface

This source candidate adds live settings for the hash-guarded Designer 10.5.0.0
host. It is not a public Q-SYS API. Designer acceptance and release remain
separate from the offline checks below.

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

These are offline checks; no current-user preferences were changed. Before
release, exercise valid/invalid external saves, native Preferences round trips,
theme/audio/tracing/Hard Links behavior, simultaneous edits, recovery/reopen and
a second process in a disposable Designer. The concurrently developed chord API
in Core #4 must retain the default-conflict policy when branches integrate.

On 2026-10-14, the Release loader/runtime build completed with zero warnings or
errors. Results: 36 live-file checks, 32 Windows settings-bridge checks, 134
Windows WPF controls checks and 8 existing settings-store checks passed.
