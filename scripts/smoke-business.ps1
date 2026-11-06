$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$api='http://127.0.0.1:5080/api/v1'
function Send($path,$body){Invoke-RestMethod ($api+$path) -Method Post -Headers $script:headers -ContentType application/json -Body ($body|ConvertTo-Json -Depth 8)}
$script:headers=@{}
$credentials=@{email=('phase3-smoke-'+[Guid]::NewGuid().ToString('N')+'@example.test');password='Phase3-Smoke!123456'}
Send '/auth/register' $credentials | Out-Null
$session=Send '/auth/login' $credentials
$script:headers=@{Authorization="Bearer $($session.accessToken)"}
$org=Send '/organizations' @{name='Phase3部署验收测试（非业务数据）'}
$root="/organizations/$($org.id)"
$today=(Invoke-RestMethod ($api+$root+'/finance-settings') -Headers $script:headers).today
$party=Send ($root+'/counterparties') @{requestId=[Guid]::NewGuid().ToString();name='验收往来方';kind='Both';contact='测试'}
$account=Send ($root+'/accounts') @{name='验收账户';currency='CNY';kind='Cash'}
$income=Send ($root+'/categories') @{name='验收收入';kind='Income'}
$expense=Send ($root+'/categories') @{name='验收支出';kind='Expense'}
$opening=Send ($root+'/transactions') @{requestId=[Guid]::NewGuid().ToString();accountId=$account.id;kind='Opening';amount='100';description='部署验收期初';businessDate=$today}
$ar=Send ($root+'/receivables') @{requestId=[Guid]::NewGuid().ToString();number='AR-SMOKE';counterpartyId=$party.id;categoryId=$income.id;currency='CNY';amount='20';issueDate=$today;dueDate=$today}
$ap=Send ($root+'/payables') @{requestId=[Guid]::NewGuid().ToString();number='AP-SMOKE';counterpartyId=$party.id;categoryId=$expense.id;currency='CNY';amount='15';issueDate=$today;dueDate=$today}
$budget=Send ($root+'/budgets') @{requestId=[Guid]::NewGuid().ToString();name='部署验收预算';currency='CNY';startDate=$today;endDate=$today;items=@(@{categoryId=$expense.id;amount='10'})}
Send ($root+'/budgets/'+$budget.id+'/status') @{status='Active'} | Out-Null
$received=Send ($root+'/receivables/'+$ar.id+'/settlements') @{requestId=[Guid]::NewGuid().ToString();accountId=$account.id;amount='20';businessDate=$today}
$paid=Send ($root+'/payables/'+$ap.id+'/settlements') @{requestId=[Guid]::NewGuid().ToString();accountId=$account.id;amount='15';businessDate=$today}
$execution=Invoke-RestMethod ($api+$root+'/budgets/'+$budget.id+'/execution') -Headers $script:headers
if(-not $execution.items[0].overBudget){throw '预算超支预警未生效'}
$export=Invoke-WebRequest ($api+$root+'/exports/transactions.xlsx') -Headers $script:headers -UseBasicParsing
if($export.StatusCode -ne 200 -or $export.RawContentLength -lt 1000){throw 'Excel导出失败'}
"Linux Excel: HTTP $($export.StatusCode), bytes=$($export.RawContentLength)"
foreach($transaction in @($received,$paid,$opening)){Send ($root+'/transactions/'+$transaction.id+'/reverse') @{requestId=[Guid]::NewGuid().ToString();reason='验收完成冲回';businessDate=$today}|Out-Null}
Send ($root+'/receivables/'+$ar.id+'/cancel') @{} | Out-Null
Send ($root+'/payables/'+$ap.id+'/cancel') @{} | Out-Null
$reconciliation=Invoke-RestMethod ($api+$root+'/reconciliation') -Headers $script:headers
if($reconciliation[0].actual -ne '0.0000' -or $reconciliation[0].difference -ne '0.0000'){throw '验收冲回或余额核对失败'}
"Runtime role smoke passed; organization=$($org.id); final balance=0.0000"

$query="?dateFrom=$today&dateTo=$today&currency=CNY"
$report=Invoke-RestMethod ($api+$root+'/reports/summary'+$query) -Headers $script:headers
if($report.summary.closing -ne '0.0000'){throw '报表期末金额不匹配'}
$pdf=Invoke-WebRequest ($api+$root+'/reports/export.pdf'+$query) -Headers $script:headers
if($pdf.StatusCode -ne 200 -or $pdf.RawContentLength -lt 1000){throw 'Linux PDF导出失败'}
"Linux PDF: HTTP $($pdf.StatusCode), bytes=$($pdf.RawContentLength)"
