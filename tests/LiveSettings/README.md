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
