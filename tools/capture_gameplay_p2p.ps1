param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action,

    [int[]]$Ports = (2300..2315),

    [string]$OutputDirectory = 'C:\temp\rakion-p2p'
)

$ErrorActionPreference = 'Stop'
$etlPath = Join-Path $OutputDirectory 'gameplay-p2p.etl'
$pcapPath = Join-Path $OutputDirectory 'gameplay-p2p.pcapng'
$textPath = Join-Path $OutputDirectory 'gameplay-p2p.txt'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-Capture {
    if ($Ports.Count -gt 32) {
        throw 'PktMon aceita no máximo 32 filtros; informe somente as portas anunciadas pelos clientes.'
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    foreach ($path in @($etlPath, $pcapPath, $textPath)) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }

    & pktmon stop 2>$null | Out-Null
    & pktmon filter remove | Out-Null
    foreach ($port in $Ports) {
        & pktmon filter add "RakionP2P-$port" -t UDP -p $port | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Falha ao registrar filtro UDP $port. Execute o PowerShell como administrador."
        }
    }

    & pktmon start --capture --comp all --pkt-size 0 --file-name $etlPath --file-size 256 --log-mode circular
    if ($LASTEXITCODE -ne 0) {
        throw 'Falha ao iniciar PktMon. Execute o PowerShell como administrador.'
    }

    Write-Host "Captura ativa em $etlPath para UDP: $($Ports -join ', ')."
}

function Stop-Capture {
    & pktmon stop | Out-Null
    if (-not (Test-Path -LiteralPath $etlPath)) {
        throw "Captura não encontrada em $etlPath."
    }

    & pktmon etl2pcap $etlPath --out $pcapPath | Out-Null
    & pktmon etl2txt $etlPath --out $textPath --timestamp --hex | Out-Null
    & pktmon filter remove | Out-Null
    Write-Host "Captura finalizada: $pcapPath"
    Write-Host "Dump hexadecimal: $textPath"
}

switch ($Action) {
    'Start' {
        if (-not (Test-Administrator)) { throw 'A captura PktMon exige PowerShell como administrador.' }
        Start-Capture
    }
    'Stop' {
        if (-not (Test-Administrator)) { throw 'A captura PktMon exige PowerShell como administrador.' }
        Stop-Capture
    }
    'Status' { & pktmon status }
}
