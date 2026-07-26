param(
    [Parameter(Mandatory)]
    [string]$HostPath,
    [string]$World = 'LevelsSV\Mammoth\Mammoth.wld',
    [ValidateRange(200, 213)]
    [byte]$MapId = 211,
    [ValidateRange(1, 4)]
    [byte]$Mode = 2
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
    $writer.Write([uint16]6)
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
    if ($magic -ne 0x4842524F -or $version -ne 6) {
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
        [byte]$Map,
        [byte]$BattleMode,
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
    $writer.Write($Map)
    $writer.Write($BattleMode)
    $writer.Write($worldBytes)
    $writer.Write([byte[]]::new(260 - $worldBytes.Length))
    return $stream.ToArray()
}

function New-AddBotPayload {
    param(
        [uint32]$BotId,
        [string]$Name,
        [string]$Species
    )

    $nameBytes = [Text.Encoding]::ASCII.GetBytes($Name)
    $speciesBytes = [Text.Encoding]::ASCII.GetBytes($Species)
    if ($nameBytes.Length -ge 32 -or $speciesBytes.Length -ge 16) {
        throw 'Identidade do bot excede o contrato IPC'
    }
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write($BotId)
    $writer.Write($nameBytes)
    $writer.Write([byte[]]::new(32 - $nameBytes.Length))
    $writer.Write($speciesBytes)
    $writer.Write([byte[]]::new(16 - $speciesBytes.Length))
    return $stream.ToArray()
}

try {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(
        '.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(15000)

    $hello = Invoke-Request -Stream $pipe -MessageType 1 -CorrelationId 1
    if ($hello.Length -ne 12 -or
        [BitConverter]::ToUInt32($hello, 4) -ne 127) {
        throw 'Capabilities inválidas no Hello'
    }

    $initialPing = Invoke-Request `
        -Stream $pipe -MessageType 3 -CorrelationId 2
    if ([BitConverter]::ToUInt32($initialPing, 8) -ne 0) {
        throw 'Worker iniciou com field inesperado'
    }

    $load = New-LoadFieldPayload `
        -FieldId 1234 -MaximumBots 4 -Map $MapId `
        -BattleMode $Mode -WorldName $World
    $loaded = Invoke-Request `
        -Stream $pipe -MessageType 2 -CorrelationId 3 -Payload $load
    if ([BitConverter]::ToUInt32($loaded, 0) -ne 1234 -or
        [BitConverter]::ToUInt32($loaded, 4) -ne 4) {
        throw 'LoadField não confirmou field/capacidade'
    }

    $activePing = Invoke-Request `
        -Stream $pipe -MessageType 3 -CorrelationId 4
    if ([BitConverter]::ToUInt32($activePing, 8) -ne 1234) {
        throw 'Ping não refletiu o field carregado'
    }

    $correlation = 5
    foreach ($botIndex in 0..3) {
        $botId = [uint32](41 + $botIndex)
        $addBot = New-AddBotPayload -BotId $botId `
            -Name "BotProbe$($botIndex + 1)" -Species 'Human'
        $added = Invoke-Request -Stream $pipe -MessageType 5 `
            -CorrelationId $correlation -Payload $addBot
        $correlation++
        if ($added.Length -ne 12 -or
            [BitConverter]::ToUInt32($added, 0) -ne $botId -or
            [BitConverter]::ToUInt32($added, 4) -ne ($botIndex + 1) -or
            [BitConverter]::ToUInt32($added, 8) -ne 4) {
            throw 'AddBot não confirmou fonte local/capacidade nativa'
        }
    }

    $botPing = Invoke-Request `
        -Stream $pipe -MessageType 3 -CorrelationId $correlation
    $correlation++
    if ([BitConverter]::ToUInt32($botPing, 12) -ne 4) {
        throw 'Ping não refletiu os bots criados'
    }

    $readyBots = [Collections.Generic.HashSet[uint32]]::new()
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $tickPayload = [BitConverter]::GetBytes([uint32]1)
        $tick = Invoke-Request -Stream $pipe -MessageType 6 `
            -CorrelationId $correlation -Payload $tickPayload
        $correlation++
        if ($tick.Length -ne 8 -or
            [BitConverter]::ToUInt32($tick, 0) -ne 1 -or
            [BitConverter]::ToUInt32($tick, 4) -ne 4) {
            throw 'Tick não confirmou frame/player ativo'
        }

        foreach ($botId in 41..44) {
            $snapshotPayload = [BitConverter]::GetBytes([uint32]$botId)
            $snapshot = Invoke-Request -Stream $pipe -MessageType 7 `
                -CorrelationId $correlation -Payload $snapshotPayload
            $correlation++
            if ($snapshot.Length -ne 36 -or
                [BitConverter]::ToUInt32($snapshot, 0) -ne $botId) {
                throw 'Snapshot retornou contrato inválido'
            }
            if (([BitConverter]::ToUInt32($snapshot, 4) -band 1) -ne 0) {
                [void]$readyBots.Add([uint32]$botId)
            }
        }
        if ($readyBots.Count -eq 4) { break }
        Start-Sleep -Milliseconds 20
    }
    if ($readyBots.Count -ne 4) {
        throw "Somente $($readyBots.Count)/4 bots publicaram entidade após 50 ticks"
    }

    $snapshotPayload = [BitConverter]::GetBytes([uint32]41)
    $baseline = Invoke-Request -Stream $pipe -MessageType 7 `
        -CorrelationId $correlation -Payload $snapshotPayload
    $correlation++
    $originX = [BitConverter]::ToSingle($baseline, 8)
    $originY = [BitConverter]::ToSingle($baseline, 12)
    $originZ = [BitConverter]::ToSingle($baseline, 16)
    $aimStream = [IO.MemoryStream]::new()
    $aimWriter = [IO.BinaryWriter]::new($aimStream)
    $aimWriter.Write([uint32]41)
    $aimWriter.Write([single]($originX + 1000))
    $aimWriter.Write([single]$originY)
    $aimWriter.Write([single]($originZ + 500))
    $aim = Invoke-Request -Stream $pipe -MessageType 9 `
        -CorrelationId $correlation -Payload $aimStream.ToArray()
    $correlation++
    if ($aim.Length -ne 4 -or
        [BitConverter]::ToUInt32($aim, 0) -ne 41) {
        throw 'Aim nativo não confirmou o bot'
    }
    $moved = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $inputStream = [IO.MemoryStream]::new()
        $inputWriter = [IO.BinaryWriter]::new($inputStream)
        $inputWriter.Write([uint32]41)
        $inputWriter.Write([uint32]1)
        $input = Invoke-Request -Stream $pipe -MessageType 8 `
            -CorrelationId $correlation -Payload $inputStream.ToArray()
        $correlation++
        if ($input.Length -ne 8 -or
            [BitConverter]::ToUInt32($input, 0) -ne 41 -or
            [BitConverter]::ToUInt32($input, 4) -ne 1) {
            throw 'Input nativo não confirmou bot/flags'
        }

        $tickPayload = [BitConverter]::GetBytes([uint32]1)
        [void](Invoke-Request -Stream $pipe -MessageType 6 `
            -CorrelationId $correlation -Payload $tickPayload)
        $correlation++
        $snapshot = Invoke-Request -Stream $pipe -MessageType 7 `
            -CorrelationId $correlation -Payload $snapshotPayload
        $correlation++
        $deltaX = [BitConverter]::ToSingle($snapshot, 8) - $originX
        $deltaY = [BitConverter]::ToSingle($snapshot, 12) - $originY
        $deltaZ = [BitConverter]::ToSingle($snapshot, 16) - $originZ
        if (($deltaX * $deltaX + $deltaY * $deltaY +
                $deltaZ * $deltaZ) -gt 0.0001) {
            $moved = $true
            break
        }
        Start-Sleep -Milliseconds 20
    }
    if (-not $moved) {
        throw 'Input forward não alterou a posição nativa após 50 ticks'
    }

    $stopStream = [IO.MemoryStream]::new()
    $stopWriter = [IO.BinaryWriter]::new($stopStream)
    $stopWriter.Write([uint32]41)
    $stopWriter.Write([uint32]0)
    [void](Invoke-Request -Stream $pipe -MessageType 8 `
        -CorrelationId $correlation -Payload $stopStream.ToArray())
    $correlation++

    $lifecycleStream = [IO.MemoryStream]::new()
    $lifecycleWriter = [IO.BinaryWriter]::new($lifecycleStream)
    $lifecycleWriter.Write([uint32]41)
    $lifecycleWriter.Write([uint32]2)
    $dead = Invoke-Request -Stream $pipe -MessageType 10 `
        -CorrelationId $correlation -Payload $lifecycleStream.ToArray()
    $correlation++
    if ($dead.Length -ne 8 -or
        [BitConverter]::ToUInt32($dead, 0) -ne 41 -or
        [BitConverter]::ToUInt32($dead, 4) -ne 2) {
        throw 'Lifecycle Dead não confirmou bot/estado'
    }
    $deadSnapshot = Invoke-Request -Stream $pipe -MessageType 7 `
        -CorrelationId $correlation -Payload $snapshotPayload
    $correlation++
    if (([BitConverter]::ToUInt32($deadSnapshot, 4) -band 2) -ne 0) {
        throw 'Snapshot nativo permaneceu vivo após Lifecycle Dead'
    }

    $lifecycleStream.SetLength(0)
    $lifecycleStream.Position = 0
    $lifecycleWriter.Write([uint32]41)
    $lifecycleWriter.Write([uint32]1)
    $alive = Invoke-Request -Stream $pipe -MessageType 10 `
        -CorrelationId $correlation -Payload $lifecycleStream.ToArray()
    $correlation++
    if ($alive.Length -ne 8 -or
        [BitConverter]::ToUInt32($alive, 4) -ne 1) {
        throw 'Lifecycle Alive não confirmou bot/estado'
    }
    $aliveSnapshot = Invoke-Request -Stream $pipe -MessageType 7 `
        -CorrelationId $correlation -Payload $snapshotPayload
    $correlation++
    if (([BitConverter]::ToUInt32($aliveSnapshot, 4) -band 2) -eq 0) {
        throw 'Snapshot nativo permaneceu morto após Lifecycle Alive'
    }

    [void](Invoke-Request -Stream $pipe -MessageType 4 `
        -CorrelationId $correlation)
    $pipe.Dispose()
    if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) {
        throw "Host não encerrou corretamente: $($process.ExitCode)"
    }
    Write-Output 'IPC smoke: quatro fontes, aim, input, lifecycle, ticks e snapshots nativos validados'
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
