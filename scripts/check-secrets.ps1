$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$secrets=@()
if(Test-Path .env){$secrets=@(Get-Content .env | Where-Object {$_ -match '^[A-Z0-9_]*(PASSWORD|SECRET|TOKEN)='} | ForEach-Object {($_ -split '=',2)[1]} | Where-Object {$_.Length -ge 16})}
$files=@(git -c core.quotepath=false ls-files --cached --others --exclude-standard)
foreach($file in $files){
 if($file -match '(^|/)(\.env$|.*\.(pfx|p12|pem|key|dump|tar)$)'){throw "敏感文件进入版本范围：$file"}
 if(!(Test-Path -LiteralPath $file -PathType Leaf)){continue}
 if((Get-Item -LiteralPath $file -Force).Length -gt 5MB){continue}
 $content=[IO.File]::ReadAllText((Join-Path (Get-Location) $file))
 foreach($secret in $secrets){if($content.Contains($secret)){throw "发现当前环境秘密：$file"}}
}
Write-Output '版本范围秘密检查通过；不输出秘密内容。'
