param([Parameter(Mandatory)][ValidatePattern('^cfis_perf_[a-f0-9]{32}$')][string]$Database,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
while($true){
 $resources=@(docker stats --no-stream --format '{{json .}}' cfis-perf-api cfis-postgres-1 | ForEach-Object {$_|ConvertFrom-Json})
 $activity=docker compose exec -T postgres psql -U cfis -d $Database -At -c "SELECT json_build_object('connections',count(*),'active',count(*) FILTER(WHERE state='active'),'waitingLocks',count(*) FILTER(WHERE wait_event_type='Lock')) FROM pg_stat_activity WHERE datname=current_database();"
 @{time=(Get-Date).ToString('o');resources=$resources;database=($activity|ConvertFrom-Json)}|ConvertTo-Json -Depth 6 -Compress|Add-Content -LiteralPath $OutputPath
 Start-Sleep -Seconds 5
}
