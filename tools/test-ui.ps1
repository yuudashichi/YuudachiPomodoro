param([switch]$Full, [switch]$SkipIdleMetrics, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$run = Join-Path $projectRoot ('artifacts\ui-test-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $run 'app'
$profile = Join-Path $run 'profile'
[string[]]$restoreArguments = if ($NoRestore) { @('--no-restore') } else { @(('-p:RestorePackagesPath=' + (Join-Path $projectRoot '.packages')), '-p:RestoreLockedMode=true') }
dotnet build (Join-Path $projectRoot 'src\XiliPomodoro\XiliPomodoro.csproj') -c Release -p:EnableUiTests=true -o $output @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'UI test build failed' }
New-Item -ItemType Directory -Force -Path $profile | Out-Null
$previous = @{}
foreach ($name in @('XILI_DATA_DIR', 'XILI_RUN_SMOKE', 'XILI_QUICK_CHECK', 'XILI_SKIP_IDLE_METRICS')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$process = $null
try {
    $env:XILI_DATA_DIR = $profile
    $env:XILI_RUN_SMOKE = if ($Full) { '1' } else { '0' }
    $env:XILI_QUICK_CHECK = if ($Full) { '0' } else { '1' }
    $env:XILI_SKIP_IDLE_METRICS = if ($SkipIdleMetrics) { '1' } else { '0' }
    $process = Start-Process -FilePath (Join-Path $output '惜立番茄钟.exe') -PassThru -WindowStyle Hidden
    $report = Join-Path $profile 'smoke-results.txt'
    $deadline = [DateTime]::UtcNow.AddMinutes(4)
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        $lines = if (Test-Path -LiteralPath $report) { @(Get-Content -LiteralPath $report) } else { @() }
        if ($lines -match '^FAIL') { throw ($lines -join "`n") }
        if ($lines -contains 'COMPLETE') { $lines; Write-Output "Artifacts: $profile"; return }
        if ($process.HasExited) { throw "UI test process exited: $($process.ExitCode)" }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI test timed out; see $profile"
} finally {
    if ($process -and !$process.HasExited -and $process.Path -eq (Join-Path $output '惜立番茄钟.exe')) { Stop-Process -Id $process.Id }
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
