param(
    [string]$ClientRoot = $env:RAKION_GOLDEN_ROOT,
    [string]$ReRoot = $env:RAKION_RE_WORK
)

if (-not $ClientRoot) { throw 'informe -ClientRoot ou RAKION_GOLDEN_ROOT' }
if (-not $ReRoot) { throw 'informe -ReRoot ou RAKION_RE_WORK' }

$artifacts = @(
    @{ Name = 'client engine'; Path = Join-Path $ClientRoot 'Bin\engine.dll'; Sha256 = '83B20D6C32CD66B95C8F8E41AD6DE13A58E8F5F948CD21CBD118D42EF8CF88F2' },
    @{ Name = 'client executable'; Path = Join-Path $ClientRoot 'Bin\rakion.bin'; Sha256 = '435F50E3FF9F3F140D4C335336B4BA4A758DF823C146210CC8DA90460960FFFF' },
    @{ Name = 'world original'; Path = Join-Path $ReRoot 'RakionWorldServ.ORIG.exe'; Sha256 = 'BBB50355A4B0BA366FD3B2A5E85C21F846C0350456DBD3EA2AFE1C6703D770A2' },
    @{ Name = 'world ghidra'; Path = Join-Path $ReRoot 'ghidra-proj\worldserv.exe'; Sha256 = 'A661955168C481D5CF48BA39569180D4C0DE4AEC9EFE7C0B705FE1258E49DE6B' },
    @{ Name = 'world live'; Path = Join-Path $ReRoot 'server\RakionWorldServ\RakionWorldServ.exe'; Sha256 = '1B8B5EB1AF36F414D7B2C4D58196E63C7D6918C403741A5DBA40D5EB9C8EE0E5' }
)

$failed = $false
foreach ($artifact in $artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path)) {
        Write-Output "MISSING $($artifact.Name): $($artifact.Path)"
        $failed = $true
        continue
    }

    $actual = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash
    $status = if ($actual -eq $artifact.Sha256) { 'OK' } else { $failed = $true; 'MISMATCH' }
    Write-Output "$status $($artifact.Name): $actual"
}

if ($failed) { exit 1 }
