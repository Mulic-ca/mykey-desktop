param([int]$ParentId, [string]$Installer, [string]$InstallDirectory)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallDirectory 'MYKEY.exe'
$backup = Join-Path (Split-Path $Installer -Parent) 'MYKEY.previous.exe'
$status = Join-Path $InstallDirectory 'data\update-result.json'
$copied = $false
$result = @{ success = $false; restored = $false; message = '' }
try {
    $parent = if ($ParentId -gt 0) { Get-Process -Id $ParentId -ErrorAction SilentlyContinue } else { $null }
    if ($parent -and -not $parent.WaitForExit(30000)) { throw 'MYKEY did not exit before update' }
    Copy-Item -LiteralPath $exe -Destination $backup
    $copied = $true
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', ('/DIR="' + $InstallDirectory + '"'))
    $setup = Start-Process -FilePath $Installer -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($setup.ExitCode -ne 0) { throw ('Installer exit code: ' + $setup.ExitCode) }
    $result.success = $true
    $result.message = 'Updated'
} catch {
    $result.message = $_.Exception.Message
    if ($copied) {
        try {
            Copy-Item -LiteralPath $backup -Destination $exe -Force
            $result.restored = $true
        } catch { $result.message += '; rollback failed: ' + $_.Exception.Message }
    }
}
$result | ConvertTo-Json | Set-Content -LiteralPath $status -Encoding UTF8
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $Installer -Parent) 'update-result.json') -Encoding UTF8
Start-Process -FilePath $exe -WorkingDirectory $InstallDirectory
