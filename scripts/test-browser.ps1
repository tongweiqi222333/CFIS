param([string]$Name='cfis_e2e')
$ErrorActionPreference='Stop'
if($Name -notmatch '^cfis_e2e_[a-z0-9]+$'){ $Name='cfis_e2e_'+[Guid]::NewGuid().ToString('N') }
Set-Location (Split-Path $PSScriptRoot)
$password=(Get-Content .env | Where-Object {$_ -like 'POSTGRES_PASSWORD=*'}).Substring(18)
$env:ConnectionStrings__Database="Host=localhost;Database=$Name;Username=cfis;Password=$password"
$env:CFIS_API_TARGET='http://localhost:5081'
$env:ASPNETCORE_ENVIRONMENT='Development'
$apiProcess=$null
try {
 docker compose exec -T postgres createdb -U cfis $Name
 if($LASTEXITCODE){throw '创建测试库失败'}
 dotnet run --project backend/CFIS.Api --no-launch-profile -- --migrate
 if($LASTEXITCODE){throw '空库迁移失败'}
 dotnet build backend/CFIS.Api
 if($LASTEXITCODE){throw '浏览器测试API构建失败'}
 $isolated=Join-Path (Get-Location) ('output/e2e-api-'+[Guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $isolated -Force|Out-Null
 Copy-Item backend/CFIS.Api/bin/Debug/net10.0/* -Destination $isolated -Recurse
 $apiProcess=Start-Process dotnet -WindowStyle Hidden -ArgumentList ('"'+(Join-Path $isolated 'CFIS.Api.dll')+'" --urls http://localhost:5081') -PassThru -RedirectStandardOutput docs/e2e-api.log -RedirectStandardError docs/e2e-api-error.log
 for($i=0;$i -lt 30;$i++){try{Invoke-WebRequest http://localhost:5081/health/ready -UseBasicParsing|Out-Null;break}catch{Start-Sleep -Seconds 1}}
 Push-Location frontend
 try {npx.cmd playwright test;if($LASTEXITCODE){throw '浏览器测试失败'}}finally{Pop-Location}
} finally {
 if($apiProcess -and !$apiProcess.HasExited){Stop-Process -Id $apiProcess.Id}
 docker compose exec -T postgres dropdb -U cfis --force $Name
}

