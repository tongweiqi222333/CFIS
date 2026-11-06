param([switch]$SkipBuild,[switch]$OpenBrowser)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
docker info --format '{{.OSType}}'|Out-Null
if($LASTEXITCODE){throw '请先打开Docker Desktop并等待Linux引擎就绪'}
./scripts/init-env.ps1
docker compose up -d --wait postgres
if($LASTEXITCODE){throw '数据库启动失败，请检查端口5432及Docker状态'}
./scripts/migrate.ps1
if($SkipBuild){docker compose up -d api}else{docker compose up -d --build api}
if($LASTEXITCODE){throw 'API启动失败，请查看docker compose logs api'}
$ready=$false;for($i=0;$i -lt 40;$i++){try{Invoke-RestMethod http://127.0.0.1:5080/health/ready|Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}};if(!$ready){throw 'API未就绪，请检查迁移和数据库权限'}
if(!(Test-Path frontend/node_modules/vite/bin/vite.js)){npm.cmd ci --prefix frontend;if($LASTEXITCODE){throw '前端依赖安装失败'}}
$listener=Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue
if($listener){
 if((Invoke-WebRequest http://127.0.0.1:5173 -UseBasicParsing).Content -notmatch 'CFIS'){throw '5173端口已被其他程序占用'}
}else{
 New-Item -ItemType Directory -Force output/runtime|Out-Null
 $env:CFIS_API_TARGET='http://127.0.0.1:5080'
 $root=(Get-Location).Path
 $process=Start-Process node -WindowStyle Hidden -WorkingDirectory (Join-Path $root 'frontend') -ArgumentList ('"'+(Join-Path $root 'frontend/node_modules/vite/bin/vite.js')+'" --host 127.0.0.1 --port 5173 --strictPort') -PassThru -RedirectStandardOutput output/runtime/frontend.log -RedirectStandardError output/runtime/frontend-error.log
 @{pid=$process.Id;workspace=$root}|ConvertTo-Json|Set-Content output/runtime/frontend.json
}
$ready=$false;for($i=0;$i -lt 25;$i++){try{Invoke-WebRequest http://127.0.0.1:5173 -UseBasicParsing|Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}};if(!$ready){throw '前端启动失败，请查看output/runtime日志'}
Write-Output 'CFIS v1.0 已启动：http://127.0.0.1:5173。登录后可生成独立演示账套。'
if($OpenBrowser){Start-Process http://127.0.0.1:5173}
