param(
    [Parameter(Mandatory = $true)][string]$PrivateKeyPath,
    [Parameter(Mandatory = $true)][string]$PublicKeyPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$private = [IO.Path]::GetFullPath($PrivateKeyPath)
$public = [IO.Path]::GetFullPath($PublicKeyPath)
if (-not $Force -and ((Test-Path -LiteralPath $private) -or (Test-Path -LiteralPath $public))) {
    throw 'Uma das chaves já existe. Use -Force somente durante rotação planejada.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($private)) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($public)) | Out-Null
$key = [Security.Cryptography.ECDsa]::Create()
try {
    $curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    $key.GenerateKey($curve)
    [IO.File]::WriteAllText($private, $key.ExportECPrivateKeyPem())
    [IO.File]::WriteAllText($public, $key.ExportSubjectPublicKeyInfoPem())
} finally {
    $key.Dispose()
}
Write-Host "Chave privada: $private"
Write-Host "Chave pública: $public"
Write-Host 'Distribua somente a chave pública com o launcher.'
