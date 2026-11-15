param([switch]$NoLaunch)
$ErrorActionPreference='Stop'
$packageRoot=$PSScriptRoot
$sourceExe=Join-Path $packageRoot 'NetworkSpeedMonitor.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Run install.ps1 from the published package folder.' }
$programsRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'
$installRoot=Join-Path $programsRoot 'NetworkSpeedMonitor'
$expected=[IO.Path]::GetFullPath($installRoot).TrimEnd('\')
if ((Split-Path -Parent $expected) -ne [IO.Path]::GetFullPath($programsRoot).TrimEnd('\')) { throw 'Invalid installation path.' }
& $sourceExe --quit
Get-Process -Name NetworkSpeedMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $sourceExe -or $_.Path -eq (Join-Path $installRoot 'NetworkSpeedMonitor.exe') } | Wait-Process -Timeout 15 -ErrorAction Stop
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $packageRoot -Force | Copy-Item -Destination $installRoot -Recurse -Force
$shell=New-Object -ComObject WScript.Shell
$exe=Join-Path $installRoot 'NetworkSpeedMonitor.exe'
$startMenu=Join-Path ([Environment]::GetFolderPath('Programs')) 'NetworkSpeedMonitor.lnk'
$desktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '系统性能监控.lnk'
foreach($path in @($startMenu,$desktop)) { $shortcut=$shell.CreateShortcut($path);$shortcut.TargetPath=$exe;$shortcut.WorkingDirectory=$installRoot;$shortcut.Description='任务栏实时网速、CPU、内存及系统资源监控';$shortcut.IconLocation=$exe+',0';$shortcut.Save() }
$uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkSpeedMonitor.Windows'
New-Item -Path $uninstallKey -Force | Out-Null
$psExe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$values=@{DisplayName='NetworkSpeedMonitor Windows';DisplayVersion='0.1.4';Publisher='NetworkSpeedMonitor';InstallLocation=$installRoot;DisplayIcon=$exe;UninstallString=('"'+$psExe+'" -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $installRoot 'uninstall.ps1')+'"');NoModify=1;NoRepair=1}
foreach($pair in $values.GetEnumerator()){Set-ItemProperty -LiteralPath $uninstallKey -Name $pair.Key -Value $pair.Value}
Write-Output ('Installed: '+$exe)
if(-not $NoLaunch){Start-Process -FilePath $exe -ArgumentList '--show'}
