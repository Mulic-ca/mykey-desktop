param([Parameter(Mandatory=$true)][string]$FixtureDirectory, [string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = (Resolve-Path -LiteralPath (Join-Path $root 'Artifacts')).Path.TrimEnd('\') + '\'
$fixture = (Resolve-Path -LiteralPath (Join-Path $FixtureDirectory 'mykey.db')).Path
if (-not $fixture.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a synthetic fixture under project Artifacts' }
$item = Get-Item -LiteralPath $fixture
while ($item -and $item.FullName.Length -ge $artifacts.Length - 1) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Fixture path must not use links or junctions' }
    $item = if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent }
}
$productionFiles = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'MYKEY.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'MYKEY.lnk'),
    (Join-Path $env:LOCALAPPDATA 'Programs\MYKEY\MYKEY.exe')
)
$productionHashes = @{}
foreach ($path in $productionFiles) {
    $productionHashes[$path] = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { $null }
}
function Assert-ProductionUntouched {
    foreach ($path in $productionFiles) {
        $current = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { $null }
        if ($current -ne $productionHashes[$path]) { throw ('Production file changed during test: ' + $path) }
    }
}
$publish = Join-Path $root 'bin\Release\net8.0-windows\win-x64\publish\MYKEY.exe'
$run = Join-Path $root ('Artifacts\release-smoke-' + (Get-Date -Format yyyyMMdd-HHmmss))
$null = New-Item -ItemType Directory -Path $run
$data = Join-Path $run 'single-data'
$null = New-Item -ItemType Directory -Path $data
Copy-Item -LiteralPath $fixture -Destination (Join-Path $data 'mykey.db')
@{ CloseToTray=$true; Theme='dark'; CheckUpdatesOnStartup=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding UTF8
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SmokeWindow {
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
function Wait-Window($process) {
    for ($i=0; $i -lt 100; $i++) {
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited unexpectedly' }
        if ($process.MainWindowHandle -ne 0 -and $process.Responding) { return $process.MainWindowHandle }
        Start-Sleep -Milliseconds 100
    }
    throw 'Application did not open a responsive window'
}
$arguments = '--test-mode --data-dir "' + $data + '"'
$first = Start-Process -FilePath $publish -ArgumentList $arguments -PassThru
try {
    $handle = Wait-Window $first
    $second = Start-Process -FilePath $publish -ArgumentList $arguments -PassThru
    if (-not $second.WaitForExit(10000)) { throw 'Second instance did not exit' }
    $first.Refresh()
    if ($first.HasExited) { throw 'First instance was lost' }
    Write-Output 'PASS: second instance exits and first remains responsive'
    $null = [SmokeWindow]::PostMessage($handle,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    if ([SmokeWindow]::IsWindowVisible($handle)) { throw 'Close-to-tray did not hide window' }
    $third = Start-Process -FilePath $publish -ArgumentList $arguments -PassThru
    if (-not $third.WaitForExit(10000)) { throw 'Tray reactivation created second instance' }
    Start-Sleep -Milliseconds 300
    if (-not [SmokeWindow]::IsWindowVisible($handle)) { throw 'Existing tray window was not shown' }
    Write-Output 'PASS: launching again restores existing tray window'
} finally { if (-not $first.HasExited) { Stop-Process -Id $first.Id } }

if (-not $Compiler) { $Compiler = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
& $Compiler /Q /DTestInstall (Join-Path $root 'installer\MYKEY.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed' }
$setup = Join-Path $root 'dist\MYKEY-Setup-1.2.0-x64-test.exe'
$install = Join-Path $run 'installed'
$installData = Join-Path $install 'data'
$null = New-Item -ItemType Directory -Path $installData -Force
Copy-Item -LiteralPath $fixture -Destination (Join-Path $installData 'mykey.db')
@{ CloseToTray=$false; Theme='dark'; CheckUpdatesOnStartup=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installData 'settings.json') -Encoding UTF8
$beforeDb = (Get-FileHash -LiteralPath (Join-Path $installData 'mykey.db')).Hash
$beforeSettings = (Get-FileHash -LiteralPath (Join-Path $installData 'settings.json')).Hash
$options = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /GROUP="MYKEY QA" /DIR="' + $install + '"'
$installer = Start-Process -FilePath $setup -ArgumentList $options -PassThru -Wait -WindowStyle Hidden
if ($installer.ExitCode -ne 0) { throw ('Install exit '+$installer.ExitCode) }
Assert-ProductionUntouched
$installedExe = Join-Path $install 'MYKEY.exe'
if ((Get-FileHash -LiteralPath $installedExe).Hash -ne (Get-FileHash -LiteralPath $publish).Hash) { throw 'Installed EXE differs from publish' }
if ((Get-FileHash -LiteralPath (Join-Path $installData 'mykey.db')).Hash -ne $beforeDb) { throw 'Installer changed database' }
if ((Get-FileHash -LiteralPath (Join-Path $installData 'settings.json')).Hash -ne $beforeSettings) { throw 'Installer changed settings' }
Write-Output 'PASS: installer preserves database and settings byte-for-byte'
$testArguments = '--test-mode --data-dir "' + $installData + '"'
$running = Start-Process -FilePath $installedExe -ArgumentList $testArguments -PassThru
$null = Wait-Window $running
if (-not $running.CloseMainWindow() -or -not $running.WaitForExit(10000) -or $running.ExitCode -ne 0) { throw 'Migrated app did not exit normally' }
$dbPath = Join-Path $installData 'mykey.db'
$stream = [IO.File]::OpenRead($dbPath)
try {
    $header = New-Object byte[] 16
    if ($stream.Read($header, 0, $header.Length) -ne 16 -or [Text.Encoding]::ASCII.GetString($header).StartsWith('SQLite format 3')) { throw 'Installed app did not encrypt the fixture' }
} finally { $stream.Dispose() }
$encryptedHash = (Get-FileHash -LiteralPath $dbPath).Hash
$keyPath = Join-Path $installData '.mykey-db.key'
$keyHash = (Get-FileHash -LiteralPath $keyPath).Hash
Write-Output 'PASS: installed app migrates the synthetic database and creates its protected key'
$updateDir = Join-Path $run 'update'
$null = New-Item -ItemType Directory -Path $updateDir
$updatePackage = Join-Path $updateDir 'MYKEY-Setup.exe'
Copy-Item -LiteralPath $setup -Destination $updatePackage
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\install-update.ps1') -ParentId 0 -Installer $updatePackage -InstallDirectory $install -TestMode
Assert-ProductionUntouched
Start-Sleep -Milliseconds 700
$updated = Get-Process MYKEY | Where-Object Path -EQ $installedExe
if (-not $updated) { throw 'Update helper did not restart app' }
$null = Wait-Window $updated
if ((Get-FileHash -LiteralPath (Join-Path $updateDir 'MYKEY.previous.exe')).Hash -ne (Get-FileHash -LiteralPath $publish).Hash) { throw 'Rollback copy missing or differs' }
if ((Get-FileHash -LiteralPath (Join-Path $installData 'settings.json')).Hash -ne $beforeSettings) { throw 'Update changed settings' }
Write-Output 'PASS: update helper installs, saves previous executable and restarts responsive app'
Stop-Process -Id $updated.Id
$failureDir = Join-Path $run 'failed-update'
$null = New-Item -ItemType Directory -Path $failureDir
$failurePackage = Join-Path $failureDir 'failure.exe'
Copy-Item -LiteralPath (Join-Path $env:WINDIR 'System32\where.exe') -Destination $failurePackage
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\install-update.ps1') -ParentId 0 -Installer $failurePackage -InstallDirectory $install -TestMode
Assert-ProductionUntouched
$failureResult = Get-Content -LiteralPath (Join-Path $failureDir 'update-result.json') -Raw | ConvertFrom-Json
if ($failureResult.success -or -not $failureResult.restored) { throw 'Installer failure did not trigger rollback' }
if ((Get-FileHash -LiteralPath $installedExe).Hash -ne (Get-FileHash -LiteralPath $publish).Hash) { throw 'Rollback executable hash mismatch' }
Start-Sleep -Milliseconds 700
$restored = Get-Process MYKEY | Where-Object Path -EQ $installedExe
if (-not $restored) { throw 'Rollback did not restart app' }
$null = Wait-Window $restored
Stop-Process -Id $restored.Id
Write-Output 'PASS: failed installer restores previous executable and restarts responsive app'
$uninstaller = Start-Process -FilePath (Join-Path $install 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru -WindowStyle Hidden
if ($uninstaller.ExitCode -ne 0 -or (Get-FileHash -LiteralPath $dbPath).Hash -ne $encryptedHash -or (Get-FileHash -LiteralPath $keyPath).Hash -ne $keyHash) { throw 'Uninstall did not preserve the encrypted database and protected key' }
Assert-ProductionUntouched
Write-Output 'PASS: production executable and shortcuts remain unchanged throughout install, update and uninstall'
Write-Output 'PASS: test reinstall, failed-update rollback and uninstall preserve encrypted data and its key byte-for-byte'
Write-Output ('ARTIFACTS='+$run)
