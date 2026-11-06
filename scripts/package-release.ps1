param([string]$Ref='HEAD')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
if(git status --porcelain){throw '发布打包前请提交或妥善处理工作区变更'}
$commit=(git rev-parse --verify ($Ref+'^{commit}')).Trim()
if($LASTEXITCODE -or $commit -ne (git rev-parse HEAD).Trim()){throw '仅允许打包当前已提交版本，防止源代码与构建包混用'}
./scripts/check-secrets.ps1
$folder=Join-Path (Get-Location) 'output/release-v1'
New-Item -ItemType Directory -Force $folder|Out-Null
$sourceZip=Join-Path $folder 'CFIS-v1.0.0-source.zip'
git archive --format=zip --output $sourceZip $commit
if($LASTEXITCODE){throw '源代码归档失败'}
$stage=Join-Path $folder ('stage-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $sourceZip -DestinationPath $stage
$frontendFolder=Join-Path $stage 'frontend'
dotnet publish backend/CFIS.Api -c Release --no-restore -p:UseAppHost=false -o (Join-Path $folder 'backend')
if($LASTEXITCODE){throw '后端发布构建失败'}
npm.cmd ci --prefix $frontendFolder
if($LASTEXITCODE){throw '前端锁定依赖安装失败'}
npm.cmd run build --prefix $frontendFolder
if($LASTEXITCODE){throw '前端发布构建失败'}
Compress-Archive -Path (Join-Path $folder 'backend/*') -DestinationPath (Join-Path $folder 'CFIS-v1.0.0-backend.zip') -Force
Compress-Archive -Path (Join-Path $frontendFolder 'dist/*') -DestinationPath (Join-Path $folder 'CFIS-v1.0.0-frontend.zip') -Force
$previousDatabase=$env:ConnectionStrings__Database
$previousEnvironment=$env:ASPNETCORE_ENVIRONMENT
try {
 $env:ConnectionStrings__Database='Host=localhost;Database=cfis;Username=cfis'
 $env:ASPNETCORE_ENVIRONMENT='Development'
 dotnet ef migrations script --idempotent --project backend/CFIS.Api --no-build --output (Join-Path $folder 'CFIS-v1.0.0-migrations.sql')
}finally{
 $env:ConnectionStrings__Database=$previousDatabase
 $env:ASPNETCORE_ENVIRONMENT=$previousEnvironment
}
if($LASTEXITCODE){throw '迁移脚本生成失败'}
Copy-Item docs/user-guide.md (Join-Path $folder 'CFIS-v1.0.0-user-guide.md')
Copy-Item docs/release-v1.md (Join-Path $folder 'RELEASE-NOTES.md')
@{version='1.0.0';commit=$commit;builtAt=(Get-Date).ToString('o');backend='.NET 10 framework-dependent portable DLL';frontend='Vite static assets';secretsIncluded=$false}|ConvertTo-Json|Set-Content (Join-Path $folder 'build-manifest.json')
$names=@('CFIS-v1.0.0-source.zip','CFIS-v1.0.0-backend.zip','CFIS-v1.0.0-frontend.zip','CFIS-v1.0.0-migrations.sql','CFIS-v1.0.0-user-guide.md','RELEASE-NOTES.md','build-manifest.json')
$checksums=foreach($name in $names){$hash=(Get-FileHash -Algorithm SHA256 (Join-Path $folder $name)).Hash.ToLowerInvariant();"$hash  $name"}
$checksums|Set-Content (Join-Path $folder 'SHA256SUMS.txt') -Encoding utf8
Write-Output "发布文件已生成于 $folder。未包含.env、数据库备份、实际附件或身份密钥。"
