# run_roombtn.ps1 — roda o hook Frida que LOCALIZA a tela do game room (p/ o botao "Adicionar Bot").
# NAO cole isto no Python (>>>). Rode no PowerShell:
#   powershell -ExecutionPolicy Bypass -File "<caminho>\tools\run_roombtn.ps1"
# (opcional)  ... run_roombtn.ps1 1234   <- PID do cliente, se o auto-detect falhar
#
# 1) Abra o jogo pelo launcher e fique no LOBBY (lista de salas).
# 2) Rode este script. Quando aparecer "hooks ativos", ENTRE numa sala (game room).
# 3) Os botoes serao logados aqui E salvos em tools\roombtn.log. Me mande esse arquivo/linhas.

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$pyScript = Join-Path $here "frida_roombtn.py"
$logFile  = Join-Path $here "roombtn.log"

# 1) acha o Python
$py = $null
foreach ($cand in @("python", "py")) {
    $c = Get-Command $cand -ErrorAction SilentlyContinue
    if ($c) { $py = $cand; break }
}
if (-not $py) {
    Write-Host "[ERRO] Python nao encontrado no PATH. Instale o Python 3 (python.org) e tente de novo." -ForegroundColor Red
    exit 1
}
Write-Host "[run] usando: $py" -ForegroundColor DarkGray

# 2) garante o frida
& $py -c "import frida" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[run] instalando frida (pip)..." -ForegroundColor Yellow
    & $py -m pip install --quiet frida
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERRO] falhou instalar o frida. Rode manualmente:  $py -m pip install frida" -ForegroundColor Red
        exit 1
    }
}

# 3) roda o hook (passa um PID opcional) e tee p/ o log
Write-Host "[run] >>> Deixe o jogo no LOBBY. Anexando ao cliente... <<<" -ForegroundColor Cyan
Write-Host "[run] Quando aparecer 'hooks ativos', ENTRE numa SALA. Saida tambem em: $logFile" -ForegroundColor Cyan
& $py $pyScript @args 2>&1 | Tee-Object -FilePath $logFile
