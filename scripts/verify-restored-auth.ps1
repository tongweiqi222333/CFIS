param([Parameter(Mandatory)][ValidatePattern('^cfis_restore_[a-f0-9]{32}$')][string]$Database)
$ErrorActionPreference='Stop'
$previousConnection=$env:ConnectionStrings__Database
$previousEnvironment=$env:ASPNETCORE_ENVIRONMENT
$process=$null
try {
 if(Get-NetTCPConnection -LocalPort 5082 -State Listen -ErrorAction SilentlyContinue){throw '恢复验收端口5082已占用，拒绝连接其他服务'}
 $password=(Get-Content .env | Where-Object {$_ -like 'POSTGRES_PASSWORD=*'}).Substring(18)
 $env:ConnectionStrings__Database="Host=127.0.0.1;Database=$Database;Username=cfis;Password=$password"
 $env:ASPNETCORE_ENVIRONMENT='Development'
 $isolated=Join-Path (Get-Location) ('output/restore-api-'+[Guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $isolated -Force|Out-Null
 Copy-Item backend/CFIS.Api/bin/Debug/net10.0/* -Destination $isolated -Recurse
 $process=Start-Process dotnet -WindowStyle Hidden -ArgumentList ('"'+(Join-Path $isolated 'CFIS.Api.dll')+'" --urls http://127.0.0.1:5082') -PassThru -RedirectStandardOutput docs/restore-api.log -RedirectStandardError docs/restore-api-error.log
 $api='http://127.0.0.1:5082/api/v1'
 $ready=$false
 for($i=0;$i -lt 30;$i++){try{Invoke-RestMethod http://127.0.0.1:5082/health/ready | Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}}
 if(!$ready){throw '恢复环境API未就绪'}
 function IdentitySession {
  $body=@{email=([Guid]::NewGuid().ToString('N')+'@restore.example.test');password='Restore-Validation!123456'}|ConvertTo-Json
  Invoke-RestMethod ($api+'/auth/register') -Method Post -ContentType application/json -Body $body | Out-Null
  $session=Invoke-RestMethod ($api+'/auth/login') -Method Post -ContentType application/json -Body $body
  return @{Authorization="Bearer $($session.accessToken)"}
 }
 $a=IdentitySession
 $b=IdentitySession
 $orgA=Invoke-RestMethod ($api+'/organizations') -Method Post -Headers $a -ContentType application/json -Body '{"name":"恢复隔离验收A"}'
 $orgB=Invoke-RestMethod ($api+'/organizations') -Method Post -Headers $b -ContentType application/json -Body '{"name":"恢复隔离验收B"}'
 Invoke-RestMethod ($api+'/organizations/'+$orgA.id) -Headers $a | Out-Null
 $response=Invoke-WebRequest ($api+'/organizations/'+$orgB.id) -Headers $a -SkipHttpErrorCheck
 if([int]$response.StatusCode -ne 404){throw '恢复后企业隔离验证失败'}
 Write-Output '恢复库注册、登录、企业访问及跨企业404验证通过（测试数据仅位于临时恢复库）'
}finally{
 if($process -and !$process.HasExited){Stop-Process -Id $process.Id}
 $env:ConnectionStrings__Database=$previousConnection
 $env:ASPNETCORE_ENVIRONMENT=$previousEnvironment
}
