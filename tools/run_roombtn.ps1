# run_roombtn.ps1 — roda o hook Frida que LOCALIZA a tela do game room (p/ o botao "Adicionar Bot").
# NAO cole isto no Python (>>>). Rode no PowerShell:
#   powershell -ExecutionPolicy Bypass -File "<caminho>\tools\run_roombtn.ps1"
# (opcional)  ... run_roombtn.ps1 1234   <- PID do cliente, se o auto-detect falhar
#
# 1) Abra o jogo pelo launcher e fique no LOBBY (lista de salas).
# 2) Rode este script. Quando aparecer "hooks ativos", ENTRE numa sala (game room).
# 3) Os botoes serao logados aqui E salvos em tools\roombtn.log. Me mande esse arquivo/linhas.

# NAO usar ErrorActionPreference=Stop: o traceback do python (frida ausente) vira stderr e
# abortaria o script ANTES de instalar o frida. Tratamos os erros por $LASTEXITCODE.
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$pyScript = Join-Path $here "frida_roombtn.py"
$logFile  = Join-Path $here "roombtn.log"

# 1) acha o Python
$py = $null
foreach ($cand in @("python", "py")) {
    if (Get-Command $cand -ErrorAction SilentlyContinue) { $py = $cand; break }
}
if (-not $py) {
    Write-Host "[ERRO] Python nao encontrado no PATH. Instale o Python 3 (python.org)." -ForegroundColor Red
    exit 1
}
Write-Host "[run] python: $py" -ForegroundColor DarkGray

# 2) frida instalado? (suprime saida; decide por exit code, sem abortar)
& $py -c "import frida" > $null 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[run] frida nao encontrado; instalando via pip (pode demorar 1 min)..." -ForegroundColor Yellow
    & $py -m pip install frida
    & $py -c "import frida" > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERRO] frida nao instalou. Tente manualmente:  $py -m pip install frida" -ForegroundColor Red
        Write-Host "       (se usar Python da Microsoft Store, instale o do python.org)" -ForegroundColor Red
        exit 1
    }
}
& $py -c "import frida; print('[run] frida ' + frida.__version__ + ' OK')"

# 3) roda o hook (PID opcional via @args) e salva o log
Write-Host "[run] >>> Deixe o jogo no LOBBY. Anexando ao cliente... <<<" -ForegroundColor Cyan
Write-Host "[run] Quando aparecer 'hooks ativos', ENTRE numa SALA. Saida tambem em: $logFile" -ForegroundColor Cyan
& $py $pyScript @args 2>&1 | Tee-Object -FilePath $logFile
