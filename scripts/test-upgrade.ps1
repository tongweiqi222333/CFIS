param([string]$Baseline='20261101040000_AcceptanceGuards',[string]$Result='docs/upgrade-v1-results.json')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$target='cfis_restore_'+[Guid]::NewGuid().ToString('N');$previous=$env:ConnectionStrings__Database
try{
 docker compose exec -T postgres createdb -U cfis $target
 if($LASTEXITCODE){throw '创建升级验收库失败'}
 $password=(Get-Content .env | Where-Object {$_ -like 'POSTGRES_PASSWORD=*'}).Substring(18)
 $env:ConnectionStrings__Database="Host=127.0.0.1;Database=$target;Username=cfis;Password=$password"
 dotnet ef database update $Baseline --project backend/CFIS.Api --no-build *> docs/phase6-upgrade.log
 if($LASTEXITCODE){throw 'Phase5基线迁移失败'}
 $org=[Guid]::NewGuid().ToString();$account=[Guid]::NewGuid().ToString()
 $seed=@'
INSERT INTO organizations ("Id","Name","TimeZone","CreatedAt") VALUES ('__ORG__','Phase6升级验收','Asia/Shanghai',now());
INSERT INTO financial_accounts ("Id","OrganizationId","Name","Currency","Status","Kind","Balance") VALUES ('__ACCOUNT__','__ORG__','升级前账户','CNY','Active','Bank',0);
'@
 $seed.Replace('__ORG__',$org).Replace('__ACCOUNT__',$account)|docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1|Out-Null
 if($LASTEXITCODE){throw '基线数据写入失败'}
 $query='SELECT md5(row_to_json(a)::text) FROM financial_accounts a;'
 $before=(docker compose exec -T postgres psql -U cfis -d $target -At -c $query).Trim()
 dotnet ef database update --project backend/CFIS.Api --no-build *>> docs/phase6-upgrade.log
 if($LASTEXITCODE){throw '最新迁移失败'}
 $after=(docker compose exec -T postgres psql -U cfis -d $target -At -c $query).Trim();if($before -ne $after){throw '升级改变了既有账户数据'}
 dotnet ef migrations has-pending-model-changes --project backend/CFIS.Api --no-build *>> docs/phase6-upgrade.log
 if($LASTEXITCODE){throw '迁移与模型不一致'}
 Get-Content scripts/verify-business.sql -Raw|docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1
 if($LASTEXITCODE){throw '升级一致性检查失败'}
 @{date=(Get-Date).ToString('o');from=$Baseline;to=(docker compose exec -T postgres psql -U cfis -d $target -At -c 'SELECT max("MigrationId") FROM "__EFMigrationsHistory";').Trim();dataDigestUnchanged=$true;pendingModelChanges=$false;consistencyPassed=$true}|ConvertTo-Json|Set-Content -LiteralPath $Result
 Write-Output '指定基线到最新版本升级演练通过，原有数据摘要不变。'
}finally{
 $env:ConnectionStrings__Database=$previous
 if($target -match '^cfis_restore_[a-f0-9]{32}$'){docker compose exec -T postgres dropdb -U cfis --if-exists --force $target}
}
