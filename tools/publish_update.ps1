param(
    [Parameter(Mandatory = $true)][string]$SourceDir,
    [Parameter(Mandatory = $true)][string]$ContentRoot,
    [Parameter(Mandatory = $true)][int]$AppId,
    [Parameter(Mandatory = $true)][int]$Version,
    [string]$DeleteListPath
)

$ErrorActionPreference = 'Stop'
if ($AppId -le 0 -or $Version -le 0) { throw 'AppId e Version devem ser positivos.' }
$source = [IO.Path]::GetFullPath($SourceDir).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = [IO.Path]::GetFullPath($ContentRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'SourceDir não existe.' }
$appRoot = Join-Path $root $AppId
$final = Join-Path $appRoot $Version
$temporary = Join-Path $appRoot (".$Version.publishing-" + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $final) { throw "Release $AppId/$Version já existe." }
[IO.Directory]::CreateDirectory($temporary) | Out-Null

function Assert-RelativePath([string]$Value) {
    $path = $Value.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($path) -or $path.Length -gt 240 -or
        [IO.Path]::IsPathRooted($path) -or $path.Contains(':')) { throw "Path inválido: $Value" }
    foreach ($segment in $path.Split('/')) {
        if ($segment.Length -eq 0 -or $segment -eq '.' -or $segment -eq '..') {
            throw "Path inválido: $Value"
        }
    }
    return $path
}

try {
    foreach ($item in Get-ChildItem -LiteralPath $source -Recurse -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse point recusado: $($item.FullName)"
        }
        if ($item.PSIsContainer) { continue }
        $relative = Assert-RelativePath ([IO.Path]::GetRelativePath($source, $item.FullName))
        if ($relative -in @('_ready', 'delete.list') -or $relative.StartsWith('.update/')) {
            throw "Nome reservado no release: $relative"
        }
        $destination = Join-Path $temporary $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $item.FullName -Destination $destination
    }
    if ($DeleteListPath) {
        $deletes = foreach ($line in [IO.File]::ReadAllLines([IO.Path]::GetFullPath($DeleteListPath))) {
            $candidate = $line.Trim()
            if ($candidate.Length -eq 0 -or $candidate.StartsWith('#')) { continue }
            Assert-RelativePath $candidate
        }
        [IO.File]::WriteAllLines((Join-Path $temporary 'delete.list'), [string[]]$deletes)
    }
    [IO.Directory]::CreateDirectory($appRoot) | Out-Null
    [IO.Directory]::Move($temporary, $final)
    [IO.File]::WriteAllText((Join-Path $final '_ready'), '')
    Write-Host "Release publicada: $final"
} catch {
    $fullTemporary = [IO.Path]::GetFullPath($temporary)
    $safePrefix = [IO.Path]::GetFullPath($appRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if ($fullTemporary.StartsWith($safePrefix) -and (Test-Path -LiteralPath $fullTemporary)) {
        Remove-Item -LiteralPath $fullTemporary -Recurse -Force
    }
    throw
}
