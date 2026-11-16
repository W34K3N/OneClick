$ErrorActionPreference = 'Continue'
$rep = 'C:\Temp\sign_report.txt'
function Rep($s) { $s | Out-File -FilePath $rep -Append -Encoding UTF8 }
"" | Out-File -FilePath $rep -Encoding UTF8

$ws = (Get-Item 'C:\Users\itsme\OneDrive\*\*\WinSetup').FullName
$exe = Join-Path $ws 'OneClick.exe'
Rep ("exe: " + $exe + "  exists=" + (Test-Path $exe))

$name = 'CN=OneClick Self-Signed'
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $name } | Select-Object -First 1
if (-not $cert) {
    Rep "creating certificate..."
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $name `
        -KeyUsage DigitalSignature -FriendlyName 'OneClick code signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
} else {
    Rep ("existing cert: " + $cert.Thumbprint)
}
Rep ("thumbprint: " + $cert.Thumbprint)
Rep ("subject:    " + $cert.Subject)
Rep ("notafter:   " + $cert.NotAfter)

$cerPath = Join-Path $ws 'OneClick-cert.cer'
$pfxPath = Join-Path $ws 'OneClick-cert.pfx'
$pfxPass = ConvertTo-SecureString -String 'oneclick' -Force -AsPlainText
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPass | Out-Null
Rep ("exported cer: " + (Test-Path $cerPath) + "  pfx: " + (Test-Path $pfxPath))

$sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -TimestampServer 'http://timestamp.digicert.com'
Rep ("signature status: " + $sig.Status)
if ($sig.StatusMessage) { Rep ("message: " + $sig.StatusMessage) }

$chk = Get-AuthenticodeSignature -FilePath $exe
Rep ("verify status: " + $chk.Status)
Rep ("verify signer: " + $(if ($chk.SignerCertificate) { $chk.SignerCertificate.Subject } else { 'none' }))
Rep ("file size: " + (Get-Item $exe).Length)
Rep ("done " + (Get-Date -Format 'HH:mm:ss'))
