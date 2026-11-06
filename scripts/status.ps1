$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
docker compose ps
foreach($url in @('http://127.0.0.1:5080/health/ready','http://127.0.0.1:5173')){
 try{$r=Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 5;Write-Output "$url : $($r.StatusCode)"}catch{Write-Output "$url : 未就绪"}
}
