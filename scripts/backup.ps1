$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
New-Item -ItemType Directory -Force backups | Out-Null
$stamp=Get-Date -Format yyyyMMdd-HHmmss
$dbPath="backups/cfis-$stamp.dump"
$keyPath="backups/keys-$stamp.tar"
try {
 docker compose exec -T postgres pg_dump -U cfis -d cfis -Fc -f /tmp/cfis-backup.dump
 if($LASTEXITCODE){throw '数据库备份失败'}
 docker compose cp postgres:/tmp/cfis-backup.dump $dbPath
 docker compose exec -T api tar -cf /tmp/keys.tar -C /home/app/.aspnet/DataProtection-Keys .
 if($LASTEXITCODE){throw '密钥备份失败'}
 docker compose cp api:/tmp/keys.tar $keyPath
 if($LASTEXITCODE){throw '密钥备份导出失败'}
 Copy-Item -LiteralPath .env -Destination ("backups/config-$stamp.env")
 if(Test-Path output/deployment/protection.pfx){Copy-Item output/deployment/protection.pfx ("backups/protection-$stamp.pfx")}
 & ./scripts/verify-restore.ps1 -Backup $dbPath -RecordManifest
 Write-Output "备份完成：$dbPath、$keyPath。密钥备份属于敏感文件，请加密离线保管。"
} finally {
 docker compose exec -T postgres rm -f /tmp/cfis-backup.dump
 docker compose exec -T api rm -f /tmp/keys.tar
}

