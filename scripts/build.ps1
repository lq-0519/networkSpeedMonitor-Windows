param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$projectFile = Join-Path $projectRoot 'src\NetworkSpeedMonitor\NetworkSpeedMonitor.csproj'
Push-Location $projectRoot
try {
    & $dotnetPath restore $projectFile --use-lock-file
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    & $dotnetPath build $projectFile -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $testOutput = Join-Path $projectRoot 'work\self-test.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $testOutput) -Force | Out-Null
    & $dotnetPath (Join-Path $projectRoot 'src\NetworkSpeedMonitor\bin\Release\net10.0-windows\NetworkSpeedMonitor.dll') --self-test $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
    Get-Content -LiteralPath $testOutput
    if ($Publish) {
        $publishPath = Join-Path $projectRoot 'artifacts\NetworkSpeedMonitor-0.1.0-win-x64'
        & $dotnetPath publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publishPath
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install.ps1'),(Join-Path $projectRoot 'scripts\uninstall.ps1'),(Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $publishPath -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $publishPath -Recurse -Force
        Write-Output $publishPath
    }
} finally { Pop-Location }
