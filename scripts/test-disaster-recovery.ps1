param([string]$Result='docs/recovery-v1-results.json')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$suffix=[Guid]::NewGuid().ToString('N');$target='cfis_restore_'+$suffix;$container='cfis-recovery-'+$suffix;$volume='cfis_recovery_keys_'+$suffix
$folder=Join-Path (Get-Location) 'output/recovery';New-Item -ItemType Directory -Force $folder | Out-Null
$api='http://127.0.0.1:5080/api/v1'
$ready=$false;for($i=0;$i -lt 40;$i++){try{Invoke-RestMethod http://127.0.0.1:5080/health/ready|Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}};if(!$ready){throw '源应用未就绪'}
$credentials=@{email=('phase6-recovery-'+$suffix+'@example.test');password=('Recovery-'+[Guid]::NewGuid().ToString('N')+'!aA1')}|ConvertTo-Json
Invoke-RestMethod ($api+'/auth/register') -Method Post -ContentType application/json -Body $credentials | Out-Null
$session=Invoke-RestMethod ($api+'/auth/login') -Method Post -ContentType application/json -Body $credentials
$headers=@{Authorization='Bearer '+$session.accessToken}
$org=Invoke-RestMethod ($api+'/organizations') -Method Post -Headers $headers -ContentType application/json -Body '{"name":"Phase6灾难恢复演练（非业务数据）"}'
$sourcePath=$api+'/organizations/'+$org.id
$account=Invoke-RestMethod ($sourcePath+'/accounts') -Method Post -Headers $headers -ContentType application/json -Body '{"name":"恢复验证账户","currency":"CNY","kind":"Bank"}'
$date=(Invoke-RestMethod ($sourcePath+'/finance-settings') -Headers $headers).today
$transaction=@{requestId=[Guid]::NewGuid().ToString();accountId=$account.id;kind='Opening';amount='10.1234';businessDate=$date;description='恢复前验收数据'}|ConvertTo-Json
$sourceTransaction=Invoke-RestMethod ($sourcePath+'/transactions') -Method Post -Headers $headers -ContentType application/json -Body $transaction
$attachmentPath=Join-Path $folder 'source-receipt.pdf'
[IO.File]::WriteAllText($attachmentPath,"%PDF-1.4`nCFIS v1 recovery fixture`n%%EOF")
$attachment=Invoke-RestMethod ($sourcePath+'/attachments') -Method Post -Headers $headers -Form @{resourceKind='Transaction';resourceId=$sourceTransaction.id;file=(Get-Item -LiteralPath $attachmentPath)}
./scripts/backup.ps1
$backup=Get-ChildItem backups/cfis-*.dump | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$stamp=$backup.BaseName.Substring(5);$keyFile=Join-Path (Get-Location) ('backups/keys-'+$stamp+'.tar')
$watch=[Diagnostics.Stopwatch]::StartNew();$snapshotTime=$backup.LastWriteTimeUtc
try {
 docker compose exec -T postgres createdb -U cfis $target
 if($LASTEXITCODE){throw '创建恢复库失败'}
 docker compose cp $backup.FullName "postgres:/tmp/recovery-$suffix.dump"
 docker compose exec -T postgres pg_restore -U cfis -d $target --exit-on-error "/tmp/recovery-$suffix.dump"
 if($LASTEXITCODE){throw '恢复数据库失败'}
 docker volume create $volume | Out-Null
 docker run --rm --user root --entrypoint sh -v "${volume}:/keys" -v "${keyFile}:/backup/keys.tar:ro" cfis-api -c 'tar -xf /backup/keys.tar -C /keys && chown -R app:app /keys'
 if($LASTEXITCODE){throw '恢复保护密钥失败'}
 $password=(Get-Content .env | Where-Object {$_ -like 'APP_DATABASE_PASSWORD=*'}).Substring(22)
 @("ConnectionStrings__Database=Host=postgres;Database=$target;Username=cfis_app;Password=$password",'ASPNETCORE_ENVIRONMENT=Development','DataProtection__Path=/home/app/.aspnet/DataProtection-Keys','Serilog__MinimumLevel__Default=Warning') | Set-Content (Join-Path $folder 'runtime.env')
 # Restored ACLs retain the main runtime role; no administrative database connection in the restored app.
 docker run -d --name $container --label cfis.recovery=true --network cfis_default -p 127.0.0.1:5083:8080 --env-file (Join-Path $folder 'runtime.env') -v "${volume}:/home/app/.aspnet/DataProtection-Keys" cfis-api | Out-Null
 if($LASTEXITCODE){throw '恢复应用启动失败'}
 $ready=$false;for($i=0;$i -lt 40;$i++){try{Invoke-RestMethod http://127.0.0.1:5083/health/ready|Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}};if(!$ready){throw '恢复应用未就绪'}
 $restored='http://127.0.0.1:5083/api/v1'
 Invoke-RestMethod ($restored+'/me') -Headers $headers | Out-Null
 Invoke-RestMethod ($restored+'/auth/login') -Method Post -ContentType application/json -Body $credentials | Out-Null
 $accounts=Invoke-RestMethod ($restored+'/organizations/'+$org.id+'/accounts') -Headers $headers
 if(($accounts.items|Where-Object id -eq $account.id).balance -ne '10.1234'){throw '恢复前账户金额不一致'}
 # Existing refresh token must be decryptable using the restored DataProtection repository.
 Invoke-RestMethod ($restored+'/auth/refresh') -Method Post -ContentType application/json -Body (@{refreshToken=$session.refreshToken}|ConvertTo-Json) | Out-Null
 $category=Invoke-RestMethod ($restored+'/organizations/'+$org.id+'/categories') -Method Post -Headers $headers -ContentType application/json -Body '{"name":"恢复后收入","kind":"Income"}'
 $transaction=@{requestId=[Guid]::NewGuid().ToString();accountId=$account.id;kind='Income';categoryId=$category.id;amount='0.0001';businessDate=$date;description='恢复后继续办理验收'}|ConvertTo-Json
 Invoke-RestMethod ($restored+'/organizations/'+$org.id+'/transactions') -Method Post -Headers $headers -ContentType application/json -Body $transaction | Out-Null
 $accounts=Invoke-RestMethod ($restored+'/organizations/'+$org.id+'/accounts') -Headers $headers
 if(($accounts.items|Where-Object id -eq $account.id).balance -ne '10.1235'){throw '恢复后业务续办失败'}
 $restoredAttachment=Join-Path $folder 'restored-receipt.pdf'
 Invoke-WebRequest ($restored+'/organizations/'+$org.id+'/attachments/'+$attachment.id) -Headers $headers -OutFile $restoredAttachment
 if((Get-FileHash $restoredAttachment).Hash -ne (Get-FileHash $attachmentPath).Hash){throw '恢复后附件内容不一致'}
 $watch.Stop()
 @{date=(Get-Date).ToString('o');backup=$backup.Name;recoverySeconds=[Math]::Round($watch.Elapsed.TotalSeconds,2);snapshotUtc=$snapshotTime.ToString('o');existingLogin=$true;existingAccessToken=$true;existingRefreshToken=$true;businessContinued=$true;runtimeRole='cfis_app';attachments='Uploaded before backup; restored download SHA256 matches original';rpo='Snapshot recovery: writes after pg_dump snapshot are not included. No PITR configured.'}|ConvertTo-Json|Set-Content -LiteralPath $Result
 Write-Output '灾难恢复演练通过：已有用户、原访问和刷新凭据、保护密钥及恢复后业务续办已验证。'
}finally{
 docker logs $container *> (Join-Path $folder 'api.log')
 docker rm -f $container 2>$null | Out-Null
 docker volume rm $volume 2>$null | Out-Null
 if($target -match '^cfis_restore_[a-f0-9]{32}$'){docker compose exec -T postgres dropdb -U cfis --if-exists --force $target}
 docker compose exec -T postgres rm -f "/tmp/recovery-$suffix.dump"
}
