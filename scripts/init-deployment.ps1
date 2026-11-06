$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$folder=Join-Path (Get-Location) 'output/deployment'
New-Item -ItemType Directory -Force $folder | Out-Null
if(Test-Path (Join-Path $folder 'protection.pfx')){throw '证书已存在，请保留现有密钥；拒绝覆盖'}
$rsa=[Security.Cryptography.RSA]::Create(3072)
$request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
$san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new();$san.AddDnsName('localhost');$san.AddIpAddress([Net.IPAddress]::Loopback);$request.CertificateExtensions.Add($san.Build())
$certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddYears(1))
$password=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
[IO.File]::WriteAllBytes((Join-Path $folder 'protection.pfx'),$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
[IO.File]::WriteAllText((Join-Path $folder 'server.crt'),$certificate.ExportCertificatePem())
[IO.File]::WriteAllText((Join-Path $folder 'server.key'),$rsa.ExportPkcs8PrivateKeyPem())
Add-Content .env ('PROTECTION_CERTIFICATE_PASSWORD='+$password)
$certificate.Dispose();$rsa.Dispose()
Write-Output '本地演练证书已生成于被忽略的 output/deployment。真实部署请替换为可信证书并安全备份保护证书及密码。'
