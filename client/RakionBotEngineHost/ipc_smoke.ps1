param(
    [Parameter(Mandatory)]
    [string]$HostPath,
    [string]$World = 'LevelsSV\Mammoth\Mammoth.wld'
)

$ErrorActionPreference = 'Stop'
$pipeName = "openrakion-bot-engine-smoke-$PID-$([Guid]::NewGuid().ToString('N'))"
$stdout = Join-Path $env:TEMP "$pipeName.out.log"
$stderr = Join-Path $env:TEMP "$pipeName.err.log"
$process = Start-Process -FilePath $HostPath `
    -ArgumentList @('--pipe', $pipeName) `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

function Read-Exact {
    param(
        [System.IO.Stream]$Stream,
        [int]$Length
    )

    $buffer = [byte[]]::new($Length)
    $offset = 0
    while ($offset -lt $Length) {
        $read = $Stream.Read($buffer, $offset, $Length - $offset)
        if ($read -eq 0) {
            throw 'Pipe encerrado antes do frame completo'
        }
        $offset += $read
    }
    return $buffer
}

function Invoke-Request {
    param(
        [System.IO.Stream]$Stream,
        [uint16]$MessageType,
        [uint32]$CorrelationId,
        [byte[]]$Payload = [byte[]]::new(0)
    )

    $writer = [System.IO.BinaryWriter]::new(
        $Stream, [Text.Encoding]::ASCII, $true)
    $writer.Write([uint32]0x4842524F)
    $writer.Write([uint16]1)
    $writer.Write($MessageType)
    $writer.Write([uint32]$Payload.Length)
    $writer.Write($CorrelationId)
    $writer.Write([uint32]0)
    $writer.Write($Payload)
    $writer.Flush()

    $headerBytes = Read-Exact -Stream $Stream -Length 20
    $headerStream = [IO.MemoryStream]::new($headerBytes, $false)
    $reader = [IO.BinaryReader]::new($headerStream)
    $magic = $reader.ReadUInt32()
    $version = $reader.ReadUInt16()
    $responseType = $reader.ReadUInt16()
    $payloadLength = $reader.ReadUInt32()
    $responseCorrelation = $reader.ReadUInt32()
    $status = $reader.ReadUInt32()
    if ($magic -ne 0x4842524F -or $version -ne 1) {
        throw 'Resposta IPC com magic/version inválido'
    }
    if ($responseType -ne ($MessageType -bor 0x8000) -or
        $responseCorrelation -ne $CorrelationId) {
        throw 'Resposta IPC sem correlação'
    }
    if ($status -ne 0) {
        throw "Host respondeu status $status ao comando $MessageType"
    }
    return Read-Exact -Stream $Stream -Length $payloadLength
}

function New-LoadFieldPayload {
    param(
        [uint32]$FieldId,
        [uint16]$MaximumBots,
        [string]$WorldName
    )

    $worldBytes = [Text.Encoding]::ASCII.GetBytes($WorldName)
    if ($worldBytes.Length -ge 260) {
        throw 'World excede 259 bytes'
    }
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write($FieldId)
    $writer.Write($MaximumBots)
    $writer.Write([uint16]0)
    $writer.Write($worldBytes)
    $writer.Write([byte[]]::new(260 - $worldBytes.Length))
    return $stream.ToArray()
}

try {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(
        '.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(15000)

    $hello = Invoke-Request -Stream $pipe -MessageType 1 -CorrelationId 1
    if ($hello.Length -ne 12 -or
        [BitConverter]::ToUInt32($hello, 4) -ne 3) {
        throw 'Capabilities inválidas no Hello'
    }

    $initialPing = Invoke-Request `
        -Stream $pipe -MessageType 3 -CorrelationId 2
    if ([BitConverter]::ToUInt32($initialPing, 8) -ne 0) {
        throw 'Worker iniciou com field inesperado'
    }

    $load = New-LoadFieldPayload `
        -FieldId 1234 -MaximumBots 8 -WorldName $World
    $loaded = Invoke-Request `
        -Stream $pipe -MessageType 2 -CorrelationId 3 -Payload $load
    if ([BitConverter]::ToUInt32($loaded, 0) -ne 1234 -or
        [BitConverter]::ToUInt32($loaded, 4) -ne 8) {
        throw 'LoadField não confirmou field/capacidade'
    }

    $activePing = Invoke-Request `
        -Stream $pipe -MessageType 3 -CorrelationId 4
    if ([BitConverter]::ToUInt32($activePing, 8) -ne 1234) {
        throw 'Ping não refletiu o field carregado'
    }

    [void](Invoke-Request -Stream $pipe -MessageType 4 -CorrelationId 5)
    $pipe.Dispose()
    if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) {
        throw "Host não encerrou corretamente: $($process.ExitCode)"
    }
    Write-Output 'IPC smoke: Hello, Ping, LoadField e Shutdown validados'
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    if (Test-Path -LiteralPath $stdout) {
        Get-Content -LiteralPath $stdout
        [IO.File]::Delete($stdout)
    }
    if (Test-Path -LiteralPath $stderr) {
        Get-Content -LiteralPath $stderr
        [IO.File]::Delete($stderr)
    }
}
