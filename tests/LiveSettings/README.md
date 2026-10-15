# Disposable Designer live settings validation

This fixture runs inside a copied, hash-guarded Designer 10.5. It is test tooling;
never include ProfileHook or SettingsLab in a product package. It redirects managed
native roaming/local/Documents/user-profile paths before native initialization.
This is preference isolation, not an OS security sandbox.

Prepare a separate Designer installation copy outside Program Files, excluding
unrelated mod modules. Add matching Better Shortcuts 0.1.5, Better Packing 0.1.3,
Named Signal Colors and Snapshot Inspector modules. Do not copy personal preferences,
credentials or designs. Use a blank untitled session. Record original native
Prefs.xml, HardLink.json, HardLinkNames.json and mod preferences.json hashes.

Build `Module/Designer.Modules.ModsCore.csproj`, `ProfileHook/ProfileHook.csproj`
and `Probe/Probe.csproj` with .NET 10, the same external `--artifacts-path`, Release,
and `-p:DesignerDir=<supported native references>`. WSL can compile; Designer and
WPF execution remain native Windows. Keep generated files outside the source tree.

Create an empty `.selection-probe-lab` marker in the copy, and
`.settings-live-lab.json` containing absolute `ApplicationDirectory` and
`ProfileDirectory` strings; the latter must be `<ApplicationDirectory>\profile`.
Run `Start-Lab.ps1 -LabDirectory <copy> -BuildDirectory <artifacts>/bin -Fresh`.
The launcher checks both vendor hashes, waits for the lab's previous processes,
archives prior test state, installs the built fixture files and launches only the
copy. It creates Documents before startup so the native asset directory stays in
the lab. The hook fails startup if path redirection fails and removes its startup
environment before Chromium children launch. Verify `profile/isolation.json`.

The probe waits for Core and a usable main window, then writes `live-evidence/result.json`
and `state.json`. A fresh profile may show Designer's informational startup notice;
close it normally to let the test run. The probe exercises actual native persistence,
and includes baseline/live provider snapshots in its local state receipt to diagnose
a simultaneous-edit rejection instead of mistaking it for the expected test error.
Keep those snapshots local along with the disposable preferences.
The probe exercises
the Preferences dialog and Better Shortcuts controls. It invokes recorder methods
for deterministic UI-component checks; that does not establish physical recorder
input. It adds synthetic commands only in this disposable process. Hard Links use
disabled documentation address `192.0.2.1`, not a real Core.

On success, test physical Ctrl+Alt+F9 (Lab first) and Ctrl+Alt+F11 then P (Lab physical).
`state.json` records per-command counts. Check that a single stroke or completed
chord increments only its owner once, and inspect the waiting overlay and hints.
Keep the two strokes within the production ten-second timeout. Reopen without
`-Fresh` to check persisted values. For Core-only dispatch, temporarily move Better
Shortcuts out of this copy while it is stopped, then reopen the retained profile.
The existing result prevents the fixture from rerunning its editor-dependent suite.
Restore the module afterward. None of these actions belongs in the user's regular
Designer installation.

Retain the result, tested module hashes and personal-file hash comparison locally.
Do not commit copied vendor binaries, native preference files, captures, or builds.
Stop the disposable process after checks. Application checks do not establish DSP
or hardware behavior; see [the settings contract](../../docs/SETTINGS-JSON.md).

## Extended acceptance modes

Pass `-Mode acceptance` to round-trip all 20 General fields through JSON and
native Preferences OK, stage simultaneous native/file edits, explicitly Retry,
and recreate a missing file without resetting native state. Run `-Mode audio`
to initialize and stop the native output and decoder for WASAPI, DirectSound and
WavOut at both supported qualities. This sends no audio payload and uses no Core.

While that isolated owner is running, `-Additional -Mode competing` starts a
second process against the same lab profile without replacing DLLs or stopping
the owner. It must refuse the settings lease and have no active synchronizer.
Check that an external JSON edit still applies in the first process. Do not use
`-Additional` with `-Fresh`.

`-Mode recovery` uses a test-only Harmony hook to lock the recovery sidecar after
the baseline snapshot. It checks that a subsequent write/compensation failure
faults synchronization, preserves the candidate and last-good bytes, and refuses
missing-file recreation. Restoring last-good does not silently resume a faulted
session. Restart with `-Mode retained` to verify all native and mod values.

`-Mode contexts` raises WPF key events through actual Designer windows. It checks
single-key and chord dispatch on the main window, text-input opt-in, the native
floating Component Controls route and an owned modal Preferences dialog. These
are application routing tests, not physical key delivery. The floating check
also covers the control moving to its final presentation source after loading.

Each mode writes `acceptance-<pid>.json`, preserving successes and failures.
Retained mode additionally writes `context.json` with focus types and execution
counts (never typed text). For physical tests, use Ctrl+Alt+F9 and Ctrl+Alt+F11
then P on schematic/UCI canvases and in native text/Monaco. Text must block these
default commands. Ctrl+Alt+F7 and Ctrl+Alt+F8 then G explicitly opt into text input
and must each increment their owner once. Detached Actipro script tabs are
outside Core's attached window scopes; their keys remain ordinary editor input.

Keep Procmon captures and native logs local when investigating personal-file
writes. Compare exact paths and process IDs, and report timestamp/code-path
correlation separately from direct writer attribution. Never commit preference
contents, authentication values or raw native logs.
