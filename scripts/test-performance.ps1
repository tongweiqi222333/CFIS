param([switch]$KeepDatabase,[switch]$Resume,[switch]$Mixed,[ValidatePattern('^[0-9]+s$')][string]$Duration='120s',[ValidatePattern('^[a-z0-9-]+$')][string]$ReportPrefix='phase-6')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$resultPath=Join-Path (Get-Location) 'performance/results'
New-Item -ItemType Directory -Force $resultPath | Out-Null
$contextPath=Join-Path $resultPath 'context.json'
$target=$null
$sampler=$null
if(!$Resume -and (Test-Path -LiteralPath $contextPath)){Remove-Item -LiteralPath $contextPath}
try{
 $password=(Get-Content .env | Where-Object {$_ -like 'POSTGRES_PASSWORD=*'}).Substring(18)
 $env:CFIS_TEST_ADMIN='Host=127.0.0.1;Database=postgres;Username=cfis;Password='+$password
 if(!$Resume){dotnet run --project tools/CFIS.Performance -- seed $resultPath;if($LASTEXITCODE){throw '性能测试夹具建立失败'}}
 $context=Get-Content -LiteralPath $contextPath -Raw | ConvertFrom-Json
 $candidate=$context.Database
 if($candidate -notmatch '^cfis_perf_[a-f0-9]{32}$'){throw '性能数据库名称无效'}
 $target=$candidate
 ./scripts/provision-runtime.ps1 -Database $target
 $appPassword=(Get-Content .env | Where-Object {$_ -like 'APP_DATABASE_PASSWORD=*'}).Substring(22)
 @("ConnectionStrings__Database=Host=postgres;Database=$target;Username=cfis_app;Password=$appPassword",'Serilog__MinimumLevel__Default=Warning') | Set-Content (Join-Path $resultPath 'runtime.env') -Encoding utf8
 if($Resume){docker start cfis-perf-api | Out-Null}else{docker compose run -d --no-deps --name cfis-perf-api --label cfis.performance=true --env-from-file (Join-Path $resultPath 'runtime.env') api}
 if($LASTEXITCODE){throw '启动性能验收API失败'}
 docker run --rm --network cfis_default --entrypoint sh grafana/k6:1.4.0 -c 'for i in $(seq 1 30); do wget -q -O - http://cfis-perf-api:8080/health/ready && exit 0; sleep 1; done; exit 1'
 if($LASTEXITCODE){throw '性能API未就绪'}
 $phases=if($Mixed){@('mixed')}else{@('before','after')}
 foreach($phase in $phases){
  if($phase -eq 'before'){
   'CREATE INDEX IF NOT EXISTS "IX_transactions_OrganizationId_AccountId" ON transactions ("OrganizationId","AccountId"); CREATE INDEX IF NOT EXISTS "IX_transactions_OrganizationId_DestinationAccountId" ON transactions ("OrganizationId","DestinationAccountId"); DROP INDEX IF EXISTS ix_transactions_account_date_cover; DROP INDEX IF EXISTS ix_transactions_destination_date_cover; DROP INDEX IF EXISTS ix_journal_posted_date_cover; DROP INDEX IF EXISTS ix_journal_lines_entry_cover;' | docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1
  }elseif($phase -eq 'after'){
   Get-Content performance/indexes.sql -Raw | docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1
   'DROP INDEX IF EXISTS "IX_transactions_OrganizationId_AccountId"; DROP INDEX IF EXISTS "IX_transactions_OrganizationId_DestinationAccountId";' | docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1
  }
  if($LASTEXITCODE){throw '索引配置失败'}
  foreach($table in @('transactions','journal_entries','journal_lines')){docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1 -c "VACUUM (ANALYZE) $table;" | Out-Null;if($LASTEXITCODE){throw '统计信息更新失败'}}
  dotnet run --project tools/CFIS.Performance --no-build -- plan $resultPath $phase
  if($LASTEXITCODE){throw '执行计划采集失败'}
  $loadFile=if($Mixed){'performance/mixed.js'}else{'performance/load.js'}
  if($Mixed){
   $samplePath=Join-Path $resultPath 'mixed-samples.jsonl'
   if(Test-Path -LiteralPath $samplePath){Remove-Item -LiteralPath $samplePath}
   $sampler=Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-File',('"'+(Join-Path (Get-Location) 'scripts/sample-performance.ps1')+'"'),'-Database',$target,'-OutputPath',('"'+$samplePath+'"')) -RedirectStandardError (Join-Path $resultPath 'sampler-error.log')
  }
  docker stats --no-stream --format '{{json .}}' cfis-perf-api cfis-postgres-1 | Set-Content (Join-Path $resultPath "$phase-resources-before.jsonl")
  docker run --rm --network cfis_default -v "${resultPath}:/results" -v "$(Join-Path (Get-Location) $loadFile):/load.js:ro" -e "PHASE=$phase" -e "DURATION=$Duration" grafana/k6:1.4.0 run /load.js
  if($LASTEXITCODE){throw "k6 $phase 阈值或检查失败"}
  if($sampler -and !$sampler.HasExited){Stop-Process -Id $sampler.Id;$sampler=$null}
 }
 if($Mixed){
  Get-Content scripts/verify-business.sql -Raw | docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1
  if($LASTEXITCODE){throw '压力测试后财务一致性检查失败'}
  docker stats --no-stream --format '{{json .}}' cfis-perf-api cfis-postgres-1 | Set-Content (Join-Path $resultPath 'mixed-resources-after.jsonl')
  Copy-Item (Join-Path $resultPath 'mixed-k6.json') (Join-Path 'docs/performance' ($ReportPrefix+'-results.json'))
  if(Test-Path (Join-Path $resultPath 'mixed-samples.jsonl')){Copy-Item (Join-Path $resultPath 'mixed-samples.jsonl') (Join-Path 'docs/performance' ($ReportPrefix+'-resources.jsonl'))}
 }
 Write-Output '性能验收完成：真实P50/P95及执行计划已保存于performance/results。'
}finally{
 if($sampler -and !$sampler.HasExited){Stop-Process -Id $sampler.Id}
 $containerLabel=docker inspect --format '{{index .Config.Labels "cfis.performance"}}' cfis-perf-api 2>$null
 if($containerLabel -eq 'true'){docker logs cfis-perf-api *> (Join-Path $resultPath 'api.log');docker rm -f cfis-perf-api | Out-Null}
 if(!$target -and (Test-Path -LiteralPath $contextPath)){$candidate=(Get-Content -LiteralPath $contextPath -Raw|ConvertFrom-Json).Database;if($candidate -match '^cfis_perf_[a-f0-9]{32}$'){$target=$candidate}}
 if($target -match '^cfis_perf_[a-f0-9]{32}$' -and !$KeepDatabase){docker compose exec -T postgres dropdb -U cfis --if-exists --force $target}
}
