param([string]$PackageRoot = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\')
$manifestPath = Join-Path $root 'client-package.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "client-package.json ausente em $root"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or -not $manifest.files) { throw 'manifesto de pacote inválido' }

$required = @(
    'RakionLauncher.exe', 'launcher.settings.json', 'server.host', 'cash-shop.url',
    'Bin\rakion.exe', 'Bin\engine.dll', 'Bin\version.dll', 'Bin\RakionClientPatch.dll',
    'DataSetup.xfs', 'Data\SeriousSam.gms'
)
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        $failures.Add("ausente: $relative")
    }
}
foreach ($entry in $manifest.files.PSObject.Properties) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $root $entry.Name))
    if (-not $path.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("caminho inseguro: $($entry.Name)")
        continue
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("ausente: $($entry.Name)")
        continue
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $entry.Value) { $failures.Add("hash divergente: $($entry.Name)") }
}
if ($failures.Count -gt 0) { throw "pacote inválido:`n- $($failures -join "`n- ")" }
$fileCount = @($manifest.files.PSObject.Properties).Count
Write-Host "Pacote íntegro: $fileCount arquivos validados."
