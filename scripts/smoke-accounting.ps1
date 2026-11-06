$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$api='http://127.0.0.1:5080/api/v1'
$credentials=@{email=('phase5-smoke-'+[Guid]::NewGuid().ToString('N')+'@example.test');password='Phase5-Smoke!123456'}|ConvertTo-Json
Invoke-RestMethod ($api+'/auth/register') -Method Post -ContentType application/json -Body $credentials | Out-Null
$login=Invoke-RestMethod ($api+'/auth/login') -Method Post -ContentType application/json -Body $credentials
$headers=@{Authorization="Bearer $($login.accessToken)"}
function Send($path,$body){Invoke-RestMethod ($api+$path) -Method Post -Headers $headers -ContentType application/json -Body ($body|ConvertTo-Json -Depth 8)}
$org=Send '/organizations' @{name='Phase5会计部署验收（非业务数据）'}
$root='/organizations/'+$org.id+'/accounting'
$today=(Invoke-RestMethod ($api+'/organizations/'+$org.id+'/finance-settings') -Headers $headers).today
$period=Send ($root+'/periods') @{code='SMOKE';startDate=$today;endDate=$today}
$asset=Send ($root+'/accounts') @{code='1002';name='验收资产';kind='Asset';currency='CNY'}
$capital=Send ($root+'/accounts') @{code='4001';name='验收资本';kind='Equity';currency='CNY'}
$entry=Send ($root+'/journals') @{requestId=[Guid]::NewGuid().ToString();businessDate=$today;currency='CNY';description='受限运行账号会计验收';lines=@(@{ledgerAccountId=$asset.id;debit='10.1234';credit='0'},@{ledgerAccountId=$capital.id;debit='0';credit='10.1234'})}
$posted=Send ($root+'/journals/'+$entry.id+'/post') @{version=$entry.version}
if($posted.status -ne 'Posted'){throw '过账失败'}
$reverse=Send ($root+'/journals/'+$entry.id+'/reverse') @{requestId=[Guid]::NewGuid().ToString();businessDate=$today;reason='验收完成冲回'}
if($reverse.status -ne 'Posted'){throw '冲销失败'}
$trial=Invoke-RestMethod ($api+$root+"/trial-balance?dateFrom=$today&dateTo=$today&currency=CNY") -Headers $headers
if($trial.difference -ne '0.0000' -or $trial.debit -ne '20.2468'){throw '试算结果错误'}
$closed=Send ($root+'/periods/'+$period.id+'/close') @{}
if($closed.status -ne 'Closed'){throw '封账失败'}
Write-Output "受限运行账号会计冒烟通过，企业$($org.id)，借贷20.2468，差额0.0000，已封账。"
