param(
    [Parameter(Mandatory = $true)][string]$FilePath,
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
    throw 'The signing certificate must have a private key and be within its validity period.'
}
$purposes = @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
    ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
if ('1.3.6.1.5.5.7.3.3' -notin $purposes) {
    throw 'The certificate is not enabled for code signing.'
}
$signature = Set-AuthenticodeSignature -LiteralPath $FilePath -Certificate $certificate -HashAlgorithm SHA256 -IncludeChain Signer
if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw 'The resulting signature has an unexpected or missing signer.'
}
# Windows reports an untrusted self-signed root as UnknownError. Do not install trust roots.
$selfSigned = $certificate.Subject -eq $certificate.Issuer
if ($signature.Status -ne 'Valid' -and -not ($selfSigned -and $signature.Status -in @('UnknownError', 'NotTrusted'))) {
    throw "Signing failed: $($signature.Status): $($signature.StatusMessage)"
}
Write-Output "Signed $(Split-Path $FilePath -Leaf): SHA256, signer=$($certificate.Thumbprint), WindowsTrust=$($signature.Status)"
