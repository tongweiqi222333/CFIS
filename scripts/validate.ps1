$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
./scripts/check-secrets.ps1
$password=(Get-Content .env | Where-Object {$_ -like 'POSTGRES_PASSWORD=*'}).Substring(18)
$env:CFIS_TEST_ADMIN="Host=127.0.0.1;Database=postgres;Username=cfis;Password=$password"
dotnet restore CFIS.slnx --locked-mode
if($LASTEXITCODE){throw '依赖恢复失败'}
dotnet build CFIS.slnx --no-restore
if($LASTEXITCODE){throw '构建失败'}
dotnet test CFIS.slnx --no-build
if($LASTEXITCODE){throw '后端测试失败'}
npm.cmd ci --prefix frontend
if($LASTEXITCODE){throw '前端依赖恢复失败'}
npm.cmd run build --prefix frontend
if($LASTEXITCODE){throw '前端构建失败'}
./scripts/test-browser.ps1
./scripts/test-upgrade.ps1
Write-Output '常规发布检查通过；恢复、Production及压力演练请按运维手册分别串行执行。'
