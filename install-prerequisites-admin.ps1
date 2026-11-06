$ErrorActionPreference = 'Continue'
Start-Transcript -Path 'E:\Documents\大学\维捷\硕士\twq\CFIS\docs\windows-prerequisites-admin.log' -Force
Get-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform
Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform -All -NoRestart
wsl --install --no-distribution --web-download
Stop-Transcript
