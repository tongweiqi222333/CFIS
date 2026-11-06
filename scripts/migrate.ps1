$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
./scripts/init-env.ps1
$env:ConnectionStrings__Database='Host=localhost;Database=cfis;Username=cfis;Password='+((Get-Content .env | Where-Object {$_ -like 'POSTGRES_PASSWORD=*'}).Substring(18))
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet tool restore
dotnet ef database update --project backend/CFIS.Api
if($LASTEXITCODE){throw '迁移失败'}
./scripts/provision-runtime.ps1
