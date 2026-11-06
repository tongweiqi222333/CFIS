param([Parameter(Mandatory)][string]$Backup,[switch]$RecordManifest)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$target='cfis_restore_'+[Guid]::NewGuid().ToString('N')
try {
 docker compose cp $Backup postgres:/tmp/restore-check.dump
 if($LASTEXITCODE){throw '备份导入失败'}
 docker compose exec -T postgres createdb -U cfis $target
 docker compose exec -T postgres pg_restore -U cfis -d $target --exit-on-error /tmp/restore-check.dump
 if($LASTEXITCODE){throw '恢复失败'}
 docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1 -c 'SELECT count(*) FROM "__EFMigrationsHistory";'
 if($LASTEXITCODE){throw '恢复后查询失败'}
 Get-Content scripts/verify-business.sql -Raw | docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1
 if($LASTEXITCODE){throw '恢复后业务一致性检查失败'}
 $fingerprintSql="SELECT md5(string_agg(payload,E'\n' ORDER BY payload)) FROM (SELECT 'accounts:'||row_to_json(t)::text AS payload FROM financial_accounts t UNION ALL SELECT 'organizations:'||row_to_json(t)::text FROM organizations t UNION ALL SELECT 'users:'||row_to_json(t)::text FROM ""AspNetUsers"" t UNION ALL SELECT 'members:'||row_to_json(t)::text FROM organization_members t UNION ALL SELECT 'counterparties:'||row_to_json(t)::text FROM counterparties t UNION ALL SELECT 'categories:'||row_to_json(t)::text FROM categories t UNION ALL SELECT 'audit:'||row_to_json(t)::text FROM audit_logs t UNION ALL SELECT 'transactions:'||row_to_json(t)::text FROM transactions t UNION ALL SELECT 'receivables:'||row_to_json(t)::text FROM receivables t UNION ALL SELECT 'payables:'||row_to_json(t)::text FROM payables t UNION ALL SELECT 'budgets:'||row_to_json(t)::text FROM budgets t UNION ALL SELECT 'budget_items:'||row_to_json(t)::text FROM budget_items t) all_rows;"
 $hasHistory=(docker compose exec -T postgres psql -U cfis -d $target -At -c "SELECT to_regclass('public.report_history_boundary') IS NOT NULL").Trim()
 if($hasHistory -eq 't'){$fingerprintSql=$fingerprintSql.Replace("FROM financial_accounts t","FROM financial_accounts t UNION ALL SELECT 'history_boundary:'||row_to_json(t)::text FROM report_history_boundary t")}
 $hasAccounting=(docker compose exec -T postgres psql -U cfis -d $target -At -c "SELECT to_regclass('public.journal_entries') IS NOT NULL").Trim()
 if($hasAccounting -eq 't'){foreach($table in @('chart_of_accounts','fiscal_periods','journal_entries','journal_lines','accounting_mappings')){$fingerprintSql=$fingerprintSql.Replace("FROM financial_accounts t","FROM financial_accounts t UNION ALL SELECT '$table'||row_to_json(t)::text FROM $table t")}}
 $hasInvitations=(docker compose exec -T postgres psql -U cfis -d $target -At -c "SELECT to_regclass('public.member_invitations') IS NOT NULL").Trim()
 if($hasInvitations -eq 't'){$fingerprintSql=$fingerprintSql.Replace("FROM financial_accounts t","FROM financial_accounts t UNION ALL SELECT 'invitations:'||row_to_json(t)::text FROM member_invitations t")}
 foreach($table in @('attachments','transaction_allocations')){
 $present=(docker compose exec -T postgres psql -U cfis -d $target -At -c "SELECT to_regclass('public.$table') IS NOT NULL").Trim()
 if($present -eq 't'){
 if($table -eq 'attachments'){$fingerprintSql=$fingerprintSql.Replace("FROM financial_accounts t","FROM financial_accounts t UNION ALL SELECT 'attachment:'||json_build_array(t.""Id"",t.""OrganizationId"",t.""ResourceId"",t.""ResourceKind"",t.""Name"",t.""Sha256"",encode(sha256(t.""Content""),'hex'))::text FROM attachments t")}
 else{$fingerprintSql=$fingerprintSql.Replace("FROM financial_accounts t","FROM financial_accounts t UNION ALL SELECT 'allocations:'||row_to_json(t)::text FROM transaction_allocations t")}
 }
 }
 $digest=($fingerprintSql | docker compose exec -T postgres psql -U cfis -d $target -v ON_ERROR_STOP=1 -At).Trim()
 if($LASTEXITCODE){throw '恢复摘要计算失败'}
 $manifest=$Backup+'.business-digest'
 if($RecordManifest){('v6:'+$digest) | Set-Content -LiteralPath $manifest -Encoding utf8}
 elseif(Test-Path -LiteralPath $manifest){if((Get-Content -LiteralPath $manifest -Raw).Trim() -notmatch '^v6:'){Write-Output '旧版摘要未包含最新附件和分摊数据，跳过摘要比对，保留业务一致性检查'}elseif((Get-Content -LiteralPath $manifest -Raw).Trim() -ne ('v6:'+$digest)){throw '恢复业务数据与备份验收摘要不一致'}}
 else{Write-Output '旧备份无摘要，已执行业务一致性检查'}
 & ./scripts/verify-restored-auth.ps1 -Database $target
 Write-Output '恢复业务一致性验证通过'
}finally{
 docker compose exec -T postgres dropdb -U cfis --if-exists --force $target
 docker compose exec -T postgres rm -f /tmp/restore-check.dump
}

