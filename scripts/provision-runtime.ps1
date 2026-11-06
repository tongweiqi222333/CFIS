param([string]$Database='cfis')
$ErrorActionPreference='Stop'
if($Database -notmatch '^cfis(_perf_[a-f0-9]{32})?$'){throw '仅允许配置主库或专用性能测试库'}
Set-Location (Split-Path $PSScriptRoot)
$password=(Get-Content .env | Where-Object {$_ -like 'APP_DATABASE_PASSWORD=*'}).Substring(22)
if($password -notmatch '^[a-zA-Z0-9_-]{24,128}$'){throw 'APP_DATABASE_PASSWORD须为24–128位字母数字下划线或连字符'}
$sql=@'
DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='cfis_app') THEN CREATE ROLE cfis_app LOGIN; END IF; END $$;
ALTER ROLE cfis_app PASSWORD '__PASSWORD__' NOSUPERUSER NOCREATEDB NOCREATEROLE;
GRANT CONNECT ON DATABASE cfis TO cfis_app;
GRANT USAGE ON SCHEMA public TO cfis_app;
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO cfis_app;
REVOKE UPDATE,DELETE,TRUNCATE ON transactions,audit_logs FROM cfis_app;
REVOKE INSERT,UPDATE,DELETE,TRUNCATE ON "__EFMigrationsHistory" FROM cfis_app;
REVOKE DELETE,TRUNCATE ON financial_accounts,categories FROM cfis_app;
REVOKE UPDATE,DELETE,TRUNCATE ON receivables,payables,budgets,budget_items FROM cfis_app;
GRANT UPDATE ("Status","ActivatedAt","ClosedAt") ON budgets TO cfis_app;
GRANT UPDATE ("Cancelled","CancelledAt") ON receivables,payables TO cfis_app;
REVOKE UPDATE ON financial_accounts,categories,counterparties FROM cfis_app;
GRANT UPDATE ("Name","Status","Balance") ON financial_accounts TO cfis_app;
GRANT UPDATE ("Name","Status") ON categories TO cfis_app;
GRANT UPDATE ("Name","Status","Contact") ON counterparties TO cfis_app;
REVOKE DELETE,TRUNCATE ON counterparties FROM cfis_app;
REVOKE UPDATE,DELETE,TRUNCATE ON chart_of_accounts,fiscal_periods,accounting_mappings FROM cfis_app;
GRANT UPDATE ("Active") ON chart_of_accounts TO cfis_app;
GRANT UPDATE ("Status","NextNumber","ClosedAt") ON fiscal_periods TO cfis_app;
GRANT UPDATE ("LedgerAccountId") ON accounting_mappings TO cfis_app;
REVOKE UPDATE,DELETE,TRUNCATE ON member_invitations FROM cfis_app;
GRANT UPDATE ("AcceptedAt") ON member_invitations TO cfis_app;
REVOKE UPDATE,TRUNCATE ON journal_entries,journal_lines FROM cfis_app;
GRANT UPDATE ("Version","PeriodId","BusinessDate","Description","Status","Number","PostedAt","DebitTotal","CreditTotal") ON journal_entries TO cfis_app;
REVOKE INSERT,UPDATE,DELETE,TRUNCATE ON report_history_boundary,category_transactions FROM cfis_app;
REVOKE UPDATE,DELETE,TRUNCATE ON attachments,transaction_allocations FROM cfis_app;
'@
$sql=$sql.Replace('__PASSWORD__',$password).Replace('GRANT CONNECT ON DATABASE cfis','GRANT CONNECT ON DATABASE '+$Database)
$sql | docker compose exec -T postgres psql -U cfis -d $Database -v ON_ERROR_STOP=1
if($LASTEXITCODE){throw '数据库运行账号配置失败'}
