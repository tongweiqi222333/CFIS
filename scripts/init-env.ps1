$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
if(-not(Test-Path .env)){[IO.File]::WriteAllText((Join-Path $PWD '.env'),'POSTGRES_PASSWORD='+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+[Environment]::NewLine)}
if(-not(Select-String -Path .env -Pattern '^APP_DATABASE_PASSWORD=' -Quiet)){Add-Content .env ('APP_DATABASE_PASSWORD='+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)))}
