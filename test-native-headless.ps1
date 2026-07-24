param(
    [Parameter(Mandatory = $true)]
    [string]$ClientRoot,
    [string]$MasterUser = "test",
    [string]$JoinerUser = "test2",
    [string]$World = "LevelsSV\Mammoth\Mammoth.wld",
    [int]$TimeoutSeconds = 90,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$masterCredential = $env:OPENRAKION_HEADLESS_MASTER_CREDENTIAL
$joinerCredential = $env:OPENRAKION_HEADLESS_JOINER_CREDENTIAL
if ([string]::IsNullOrEmpty($masterCredential) -or
    [string]::IsNullOrEmpty($joinerCredential)) {
    throw "Configure OPENRAKION_HEADLESS_MASTER_CREDENTIAL e " +
        "OPENRAKION_HEADLESS_JOINER_CREDENTIAL no ambiente."
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$client = (Resolve-Path -LiteralPath $ClientRoot).Path
$bin = Join-Path $client "Bin"
$clientExecutable = Join-Path $bin "rakion.exe"
if (-not (Test-Path -LiteralPath $clientExecutable)) {
    throw "Cliente original inválido: $clientExecutable"
}
if (Get-Process rakion, RakionBotHost -ErrorAction SilentlyContinue) {
    throw "Feche instâncias existentes de Rakion e RakionBotHost antes do teste."
}

function Resolve-DotNet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $local = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $local) { return $local }
    throw ".NET SDK não encontrado."
}

function Start-BotHost {
    param(
        [string]$Executable,
        [string]$Credential,
        [string[]]$NativeArguments
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.EnvironmentVariables["OPENRAKION_BOT_CREDENTIAL"] = $Credential
    $escaped = foreach ($argument in $NativeArguments) {
        if ($argument.Contains('"')) {
            throw "Argumento nativo contém aspas e não é seguro."
        }
        if ($argument -match "\s") { '"' + $argument + '"' } else { $argument }
    }
    $start.Arguments = $escaped -join " "
    return [System.Diagnostics.Process]::Start($start)
}

function Wait-LogPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [datetime]$Deadline
    )

    while ([datetime]::UtcNow -lt $Deadline) {
        if (Test-Path -LiteralPath $Path) {
            $content = Get-Content -LiteralPath $Path -Raw
            if ($content -match $Pattern) { return $true }
        }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

$dotnet = Resolve-DotNet
$compatLog = Join-Path ([System.IO.Path]::GetTempPath()) "rakion_client_compat.log"
$room = "headless-" + [datetime]::UtcNow.ToString("HHmmss")
$hostExecutable = Join-Path $root `
    "client\RakionBotHost\bin\Release\net9.0-windows\RakionBotHost.exe"
$hosts = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

try {
    if (-not $SkipBuild) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File `
            (Join-Path $root "client\RakionClientCompat\build.ps1")
        if ($LASTEXITCODE -ne 0) { throw "Build da DLL nativa falhou." }
        & $dotnet build `
            (Join-Path $root "client\RakionBotHost\RakionBotHost.csproj") `
            -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Build do BotHost falhou." }
    }

    Copy-Item -LiteralPath `
        (Join-Path $root "client\RakionClientCompat\bin\version.dll") `
        -Destination (Join-Path $bin "version.dll") -Force
    Copy-Item -LiteralPath `
        (Join-Path $root "client\RakionClientCompat\bin\RakionClientPatch.dll") `
        -Destination (Join-Path $bin "RakionClientPatch.dll") -Force
    Remove-Item -LiteralPath $compatLog -Force -ErrorAction SilentlyContinue

    $common = @(
        "--client-root", $client,
        "--world", $World,
        "--server", "1A"
    )
    $master = Start-BotHost $hostExecutable $masterCredential @(
        $common + @(
            "--user", $MasterUser,
            "--role", "master",
            "--room", $room
        )
    )
    [void]$hosts.Add($master)

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    if (-not (Wait-LogPattern $compatLog `
        "headless World: sala master confirmada pelo engine" $deadline)) {
        throw "O master não confirmou a sala dentro do timeout."
    }

    $joiner = Start-BotHost $hostExecutable $joinerCredential @(
        $common + @(
            "--user", $JoinerUser,
            "--role", "joiner",
            "--field", "quick"
        )
    )
    [void]$hosts.Add($joiner)

    $required = @(
        "headless engine: StartGame master retornou=[1-9]",
        "headless engine: JoinGame retornou=[1-9]",
        "headless engine iniciado: mode=0",
        "headless engine iniciado: mode=4"
    )
    foreach ($pattern in $required) {
        if (-not (Wait-LogPattern $compatLog $pattern $deadline)) {
            throw "Gate headless ausente: $pattern"
        }
    }

    $content = Get-Content -LiteralPath $compatLog -Raw
    if ($content -match "excecao no join|ABI de CGame|engine recusado") {
        throw "O engine registrou falha durante o bootstrap nativo."
    }
    Write-Host "PASS native-headless room=$room master=$MasterUser joiner=$JoinerUser"
}
catch {
    if (Test-Path -LiteralPath $compatLog) {
        Get-Content -LiteralPath $compatLog -Tail 80
    }
    Write-Error $_
    exit 1
}
finally {
    foreach ($process in $hosts) {
        if ($process -and -not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit(5000)
        }
        if ($process) { $process.Dispose() }
    }
}
