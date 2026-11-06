$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
if(Test-Path output/runtime/frontend.json){
 $state=Get-Content output/runtime/frontend.json -Raw|ConvertFrom-Json
 if($state.workspace -eq (Get-Location).Path){
 $process=Get-CimInstance Win32_Process -Filter ("ProcessId="+[int]$state.pid)
 $expected=Join-Path (Get-Location) 'frontend/node_modules/vite/bin/vite.js'
 if($process -and $process.CommandLine.Contains($expected)){Stop-Process -Id ([int]$state.pid)}
 }
 Remove-Item -LiteralPath output/runtime/frontend.json
}
docker compose stop api postgres
if($LASTEXITCODE){throw '容器停止失败'}
Write-Output '本脚本管理的前端及Compose服务已停止，数据库与密钥卷保留。此前独立启动的前端需由原启动入口停止。'
