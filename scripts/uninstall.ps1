$ErrorActionPreference='Stop'
$installRoot=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$expected=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\NetworkSpeedMonitor')).TrimEnd('\')
if($installRoot -ne $expected){throw 'This uninstaller only operates inside the named per-user installation directory.'}
$exe=Join-Path $installRoot 'NetworkSpeedMonitor.exe'
if(Test-Path -LiteralPath $exe){& $exe --quit}
Get-Process -Name NetworkSpeedMonitor -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe} | Wait-Process -Timeout 15 -ErrorAction Stop
$startMenu=Join-Path ([Environment]::GetFolderPath('Programs')) 'NetworkSpeedMonitor.lnk'
$desktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '系统性能监控.lnk'
foreach($path in @($startMenu,$desktop)){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -LiteralPath $run -Name 'NetworkSpeedMonitor' -ErrorAction SilentlyContinue
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkSpeedMonitor.Windows'
if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key -Recurse -Force}
Set-Location -LiteralPath ([IO.Path]::GetTempPath())
Remove-Item -LiteralPath $installRoot -Recurse -Force
Write-Output 'Uninstalled. Local history and preferences were retained in LocalAppData\NetworkSpeedMonitor.'
