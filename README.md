# Q-SYS Mods Core

Shared services for independent Q-SYS Designer mods: native Preferences pages, command and menu registration, exact-host checks, theme helpers, and local diagnostics. This is a Designer extension, not a DSP Core plugin or a public Q-SYS SDK.

Core supports the inspected Designer **10.5.0.0** binaries only. Runtime checks require SHA-256 `0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60` for `Q-Sys Designer.dll` and `a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee` for `ModuleCommon.dll`. An isolated-lab marker does not bypass these checks.

## Using the mods

Open **File > Preferences** for Better Packing, Named Signal Colors, and Better Shortcuts. Pages use the existing left navigation and right content area. Operation windows appear under **Tools > Q-SYS Mods**. Passive filters have no empty operation windows.

Install with Q-SYS Mod Manager **0.8 or newer** and the approved schema-3 catalog. When replacing Schematic Tools 0.16 or earlier, use **Split Schematic Tools** so the bundle and replacement hooks are never enabled together. The manager preserves unrelated exact pins and an undoable copy of the old bundle. Core alone does not install any keyboard shortcuts.

Settings are stored in `%LOCALAPPDATA%\QsysMods\Designer-10.5\preferences.json`. A Preferences dialog stages every page, validates all values, then saves one document on OK. Closing or Cancel discards staged changes. Atomic replacement retains `.previous`; a file lock and a stale-document check prevent concurrent overwrites. Unknown sections survive temporarily uninstalled mods. Corrupt files are reported and preserved, not reset silently. Each mod can supply migration defaults; Named Signal Colors reads the old palette without changing its original file.

## Mod API

Reference `Core/Qmm.Shared.ModsCore.csproj` while developing, and ship a dependency on `mods-core` rather than another runtime copy. The runtime assembly is the privately owned `Qmm.mods-core.Runtime.dll`, so Core can be versioned without conflicting shared-file hashes. Only its loader is named `Designer.Modules.ModsCore.dll`.

- Derive the native loader from `ModModule` and call `Mods.Register(id, initialize)`. Initialization waits for native commands and isolates individual mod failures. Loader-only menu entries are hidden.
- Register stable command IDs with `Commands.Register(new ModCommand(...))`. A command has a name, group, action, availability predicate, optional default gesture, and optional menu entry. Duplicate IDs fail. Menus work without Better Shortcuts. Native adapters keep Q-SYS's routed `CanExecute` behavior.
- Register a typed page with `Preferences.Register`. Its editor returns a view and a reader; validation must not mutate live settings. The returned `ModSettings<T>.Changed` event fires after a successful save. Consumers must unsubscribe when their windows close.
- Use `Host.Theme(window)` for live host styles and `Host.Receipt` for best-effort local diagnostics. Editing operations use `Host.RequiredReceipt` before their first mutation so a write failure stops the operation. `HostSelection` contains the extracted, version-scoped selection/geometry helpers.

The integration attaches pages to the observed native Preferences tab control and prefixes its OK handler through Harmony. These are version-scoped implementation contracts, not supported Q-SYS SDK interfaces.

Q-SYS's loader examines interfaces in module assemblies. Avoid open generic implementation helpers in `Designer.Modules.*` assemblies; in particular, collection expressions targeting `IEnumerable<T>` can produce a generic helper that prevents startup. Use ordinary arrays for those returns.

## Build and validation

Use .NET SDK 10.0.112, the Windows targeting pack, and local vendor references:

```text
dotnet build Module/Designer.Modules.ModsCore.csproj -c Release -p:DesignerDir="C:\Program Files\QSC\Q-SYS Designer 10.5"
dotnet run --project tests/Settings/Settings.csproj
```

WSL builds use the corresponding `/mnt/c/...` Designer path and separate artifact outputs. No vendor binaries belong in this repository.

Eight settings-store checks cover defaults, preservation, stale saves, backups, corrupt files, and identifiers. The guarded native driver in `tests/Native` exercises independently loaded feature modules, native Preferences Cancel/OK/reopen, defaults, shortcut conflicts/remapping/reset, sidebar collapse/restore, theme changes with an open Snapshot Inspector, six sidebar focus targets, Schematic Elements search focus, and the isolated /mods-dev diagnostics flag. It requires an exact disposable application path, `.selection-probe-lab`, a matching host, a new output directory, and a blank untitled document. Native test results establish only those application behaviors; they do not establish DSP/hardware acceptance or physical-key delivery.

The driver accepts both the original Better Shortcuts list and its four-column table. For the table it also checks column names, reference rows with assignment disabled, keybinding search, and an empty search result. These table assertions are prepared for [Better Shortcuts #2](https://github.com/joshua-small/qsys-better-shortcuts/issues/2); compiling the driver does not establish that they have run successfully in Designer.

The split and release sequencing are tracked in [Schematic Tools #4](https://github.com/joshua-small/qsys-schematic-tools/issues/4) and [Mod Manager #18](https://github.com/joshua-small/qsys-mod-manager/issues/18). No stable split release is implied by this source branch.
