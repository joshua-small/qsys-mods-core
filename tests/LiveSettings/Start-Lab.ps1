param(
    [Parameter(Mandatory)][string]$LabDirectory,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [switch]$Fresh
)
$ErrorActionPreference = 'Stop'
$lab = (Resolve-Path -LiteralPath $LabDirectory).Path.TrimEnd('\')
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$config = Get-Content -LiteralPath "$lab\.settings-live-lab.json" -Raw | ConvertFrom-Json
if ($config.ApplicationDirectory -ne $lab -or !(Test-Path -LiteralPath "$lab\.selection-probe-lab")) { throw 'Requires an explicitly prepared disposable copy.' }
if ($config.ProfileDirectory -ne "$lab\profile") { throw 'Unexpected disposable profile directory.' }
if ($lab.StartsWith($env:ProgramFiles, [StringComparison]::OrdinalIgnoreCase)) { throw 'Never run this fixture from Program Files.' }
if ((Get-FileHash -LiteralPath "$lab\Q-Sys Designer.dll").Hash -ne '0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60') { throw 'Unexpected Designer binary.' }
if ((Get-FileHash -LiteralPath "$lab\ModuleCommon.dll").Hash -ne 'a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee') { throw 'Unexpected ModuleCommon binary.' }
# Stop only the exact disposable copy and its Chromium children, and wait until
# files are released before archiving state or replacing fixture assemblies.
$running = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($lab+'\', [StringComparison]::OrdinalIgnoreCase) -and
    $_.Name -in @('Q-Sys Designer.exe', 'CefSharp.BrowserSubprocess.exe')
}
foreach ($proc in $running) {
    $p = Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue
    if ($p) { Stop-Process -Id $p.Id; $p.WaitForExit() }
}
if ($Fresh) {
    $archive = Join-Path $lab ('previous-run-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $archive | Out-Null
    foreach ($name in @('profile', 'mods-lab-settings', 'live-evidence')) {
        $source = [IO.Path]::GetFullPath((Join-Path $lab $name))
        $destination = [IO.Path]::GetFullPath((Join-Path $archive $name))
        if (!$source.StartsWith($lab+'\') -or !$destination.StartsWith($lab+'\')) { throw 'Archive outside lab.' }
        if (Test-Path -LiteralPath $source) { Move-Item -LiteralPath $source -Destination $destination }
    }
}
New-Item -ItemType Directory -Force -Path "$lab\profile\Temp", "$lab\profile\Documents\QSC\Q-Sys Designer" | Out-Null
foreach ($artifact in @('ProfileHook\release\ProfileHook.dll', 'Probe\release\Designer.Modules.SettingsLab.dll', 'Qmm.Shared.ModsCore\release\Qmm.mods-core.Runtime.dll', 'Designer.Modules.ModsCore\release\Designer.Modules.ModsCore.dll')) {
    Copy-Item -LiteralPath (Join-Path $build $artifact) -Destination $lab
}
$psi = [Diagnostics.ProcessStartInfo]::new("$lab\Q-Sys Designer.exe")
$psi.WorkingDirectory = $lab
$psi.UseShellExecute = $false
$psi.WindowStyle = 'Hidden'
$psi.Environment['DOTNET_STARTUP_HOOKS'] = "$lab\ProfileHook.dll"
$psi.Environment['DOTNET_ReadyToRun'] = '0'
$psi.Environment['DOTNET_TieredCompilation'] = '0'
$psi.Environment['COMPlus_JitNoInline'] = '1'
$psi.Environment['TEMP'] = "$lab\profile\Temp"
$psi.Environment['TMP'] = "$lab\profile\Temp"
$p = [Diagnostics.Process]::Start($psi)
@{ ProcessId = $p.Id; ApplicationDirectory = $lab } | ConvertTo-Json | Set-Content -LiteralPath "$lab\lab-process.json"
$p.Id
