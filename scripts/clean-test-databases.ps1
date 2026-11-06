$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
# Failed xUnit fixture initialization may bypass its normal disposal.
# Only the locally managed PostgreSQL and random, inactive test databases are eligible.
$names=@(docker compose exec -T postgres psql -U cfis -d postgres -At -c "SELECT datname FROM pg_database WHERE datname ~ '^cfis_(test|e2e|perf|restore)_[a-f0-9]{32}$' AND NOT EXISTS(SELECT 1 FROM pg_stat_activity a WHERE a.datname=pg_database.datname);")
if($LASTEXITCODE){throw '读取本地临时库清单失败'}
$count=0
foreach($name in $names){
 if($name -notmatch '^cfis_(test|e2e|perf|restore)_[a-f0-9]{32}$'){throw '临时库名称校验失败'}
 # Do not force disconnect: another run may have opened it since the inventory query.
 docker compose exec -T postgres dropdb -U cfis --if-exists $name
 if($LASTEXITCODE){throw '清理失败或临时库已被其他测试使用，停止清理'}
 $count++
}
Write-Output "已清理 $count 个无活动连接的随机临时测试库；主库不在允许范围内。"
