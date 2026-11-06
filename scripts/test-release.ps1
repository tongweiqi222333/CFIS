$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
if(!(Test-Path output/deployment/protection.pfx)){./scripts/init-deployment.ps1}
$credentials=@{email=('release-'+[Guid]::NewGuid().ToString('N')+'@example.test');password=('Release-'+[Guid]::NewGuid().ToString('N')+'!aA1')}|ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5080/api/v1/auth/register -Method Post -ContentType application/json -Body $credentials|Out-Null
$watch=[Diagnostics.Stopwatch]::StartNew()
try {
 docker compose -f compose.yaml -f compose.production.yaml config --quiet
 if($LASTEXITCODE){throw '部署配置校验失败'}
 docker compose -f compose.yaml -f compose.production.yaml up -d --build api frontend *> docs/phase6-production-build.log
 if($LASTEXITCODE){throw '生产模式容器启动失败'}
 $ready=$false;for($i=0;$i -lt 60;$i++){try{Invoke-RestMethod https://127.0.0.1:8443/health/ready -SkipCertificateCheck|Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}};if(!$ready){throw '生产模式未就绪'}
 $response=Invoke-WebRequest https://127.0.0.1:8443/ -SkipCertificateCheck
 if($response.Content -notmatch '<div id="app">'||!$response.Headers['Content-Security-Policy']){throw '前端或安全响应头错误'}
 $registration=Invoke-WebRequest https://127.0.0.1:8443/api/v1/auth/register -Method Post -ContentType application/json -Body $credentials -SkipCertificateCheck -SkipHttpErrorCheck
 if([int]$registration.StatusCode -ne 403){throw '生产注册开关无效'}
 $session=Invoke-RestMethod https://127.0.0.1:8443/api/v1/auth/login -Method Post -ContentType application/json -Body $credentials -SkipCertificateCheck
 Invoke-RestMethod https://127.0.0.1:8443/api/v1/me -Headers @{Authorization='Bearer '+$session.accessToken} -SkipCertificateCheck | Out-Null
 docker compose -f compose.yaml -f compose.production.yaml exec -T api sh -c 'grep -l encryptedSecret /home/app/.aspnet/DataProtection-Keys/key-*.xml >/dev/null'
 if($LASTEXITCODE){throw '生产保护密钥未加密'}
 $watch.Stop()
 @{date=(Get-Date).ToString('o');httpsFrontend=$true;productionApi=$true;registrationDisabled=$true;existingUserLogin=$true;encryptedProtectionKeys=$true;seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2);certificate='Local rehearsal self-signed certificate; trusted public certificate required for public hosting';rollback='Returned to development compose without deleting data or keys'}|ConvertTo-Json|Set-Content docs/release-phase-6-results.json
 Write-Output '生产模式HTTPS、前端、登录、注册限制和密钥加密验收通过。'
}finally{
 docker logs cfis-api-1 *> docs/phase6-production-api.log
 docker logs cfis-frontend-1 *> docs/phase6-production-frontend.log
 docker compose -f compose.yaml -f compose.production.yaml stop frontend | Out-Null
 docker compose up -d api postgres | Out-Null
}
