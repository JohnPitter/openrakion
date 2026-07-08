# diag.ps1 — diagnóstico do muro do HIT×N por LEITURA EXTERNA PURA (ReadProcessMemory).
# NÃO injeta nada (injeção via LoadLibrary crasha o anti-tamper); só LÊ a memória do rakion.exe, como os
# patches read/write da launcher que o anti-tamper permite. Resolve a entidade de cada slot replicando o
# GetPlayerEntity @engine.dll 0x36121530 (puro read, cravado por disasm):
#     pNet=[0x362ba778]; A=[pNet+0x18]; B=[A+0x10]; entry=B+slot*0x100; ent=[entry+4] (0 => slot vazio).
# Dumpa o struct de cada slot em snapshots -> C:\temp\entdiff\pid<PID>\slotNN_snapSS.bin, e diffa
# humano-peer vs bot (diff_entities.py).
#
# Uso:  (com o(s) cliente(s) JÁ no stage, com o bot)
#   .\diag.ps1                              # dumpa; no fim mostra o comando do diff
#   .\diag.ps1 -HumanSeat 10 -BotSeat 11    # também roda o diff
param(
    [int]$HumanSeat = -1,
    [int]$BotSeat   = -1,
    [int]$Snapshots = 24,
    [int]$IntervalMs = 2500,
    [int]$EntBytes  = 0x2800,
    [string]$DumpDir = 'C:\temp\entdiff'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Mem {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool i, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
    const uint PROCESS_VM_READ = 0x0010, PROCESS_QUERY_INFORMATION = 0x0400;
    public static IntPtr Open(int pid){ return OpenProcess(PROCESS_VM_READ|PROCESS_QUERY_INFORMATION, false, pid); }
    public static uint RU(IntPtr h, uint addr){
        byte[] b = new byte[4]; IntPtr n;
        if (!ReadProcessMemory(h, (IntPtr)addr, b, 4, out n) || (int)n != 4) return 0;
        return BitConverter.ToUInt32(b,0);
    }
    // resolve a entidade do slot (cadeia do GetPlayerEntity@0x36121530). 0 = slot vazio/erro.
    public static uint SlotEntity(IntPtr h, int slot){
        uint pNet = RU(h, 0x362ba778); if (pNet==0) return 0;
        uint A = RU(h, pNet + 0x18);   if (A==0) return 0;
        uint B = RU(h, A + 0x10);      if (B==0) return 0;
        uint entry = B + (uint)slot*0x100;
        if (RU(h, entry) == 0) return 0;    // primeiro campo 0 = slot sem player
        return RU(h, entry + 4);            // CEntity*
    }
    public static byte[] Read(IntPtr h, uint addr, int len){
        byte[] b = new byte[len]; IntPtr n;
        ReadProcessMemory(h, (IntPtr)addr, b, len, out n);   // páginas ilegíveis ficam 0 (parcial ok)
        return b;
    }
}
"@

# 1) ESPERA o(s) rakion.exe estar(em) num STAGE (a cadeia resolve entidade). Você pode rodar o script ANTES
#    de abrir o jogo — ele aguarda você abrir os clientes e entrar na partida com o bot.
Write-Host "Aguardando o jogo entrar num STAGE (abra o(s) cliente(s) e entre na partida com o bot)..." -ForegroundColor Cyan
Write-Host "  (Ctrl+C aborta.)" -ForegroundColor DarkGray
$procs = @()
$waitUntil = (Get-Date).AddMinutes(20)
while ((Get-Date) -lt $waitUntil) {
    $procs = @(Get-Process rakion -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        # algum cliente já num stage? (SlotEntity de qualquer slot != 0)
        $ready = $false
        foreach ($p in $procs) {
            $h = [Mem]::Open($p.Id); if ($h -eq [IntPtr]::Zero) { continue }
            try { for ($s = 0; $s -lt 20; $s++) { if ([Mem]::SlotEntity($h, $s) -ne 0) { $ready = $true; break } } }
            finally { [void][Mem]::CloseHandle($h) }
            if ($ready) { break }
        }
        if ($ready) { break }
    }
    Start-Sleep -Seconds 2
}
if ($procs.Count -eq 0 -or (Get-Date) -ge $waitUntil) {
    throw "Nenhum rakion.exe num stage após a espera. Confirme que o jogo ABRE (a injeção foi removida) e que você entrou na partida com o bot."
}
Write-Host "Stage detectado — rakion.exe: $($procs.Count) processo(s) (PIDs $($procs.Id -join ', '))" -ForegroundColor Green

# 2) limpa dumps antigos
if (Test-Path $DumpDir) { Remove-Item "$DumpDir\*" -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force $DumpDir | Out-Null

# 3) snapshots
Write-Host "Dumpando $Snapshots snapshots a cada ${IntervalMs}ms. Mantenha o stage ativo (parado, andando, batendo)." -ForegroundColor Cyan
for ($snap = 0; $snap -lt $Snapshots; $snap++) {
    foreach ($p in $procs) {
        $h = [Mem]::Open($p.Id)
        if ($h -eq [IntPtr]::Zero) { continue }
        try {
            $pidDir = Join-Path $DumpDir ("pid{0}" -f $p.Id)
            New-Item -ItemType Directory -Force $pidDir | Out-Null
            $occ = @()
            for ($slot = 0; $slot -lt 20; $slot++) {
                $ent = [Mem]::SlotEntity($h, $slot)
                if ($ent -eq 0) { continue }
                $bytes = [Mem]::Read($h, $ent, $EntBytes)
                [System.IO.File]::WriteAllBytes((Join-Path $pidDir ("slot{0:D2}_snap{1:D2}.bin" -f $slot, $snap)), $bytes)
                $occ += ("{0}(0x{1:X8})" -f $slot, $ent)
            }
            if ($snap -eq 0) { Write-Host ("  PID {0}: slots com entidade = {1}" -f $p.Id, ($(if($occ){$occ -join ' '}else{'(nenhum — entrou no stage?)'}))) -ForegroundColor DarkGray }
        } finally { [void][Mem]::CloseHandle($h) }
    }
    Start-Sleep -Milliseconds $IntervalMs
}
Write-Host "Dumps prontos em $DumpDir." -ForegroundColor Green

# 4) diff (por PID — o cliente certo é o que tem AMBOS os seats)
function Run-Diff($h, $b) {
    Get-ChildItem $DumpDir -Directory | ForEach-Object {
        $dir = $_.FullName
        $hasH = Test-Path (Join-Path $dir ("slot{0:D2}_snap00.bin" -f $h))
        $hasB = Test-Path (Join-Path $dir ("slot{0:D2}_snap00.bin" -f $b))
        Write-Host "`n== $($_.Name): humano seat $h $(if($hasH){'OK'}else{'FALTA'}), bot seat $b $(if($hasB){'OK'}else{'FALTA'}) ==" -ForegroundColor Cyan
        if ($hasH -and $hasB) { python (Join-Path $here 'diff_entities.py') $dir $h $b }
        else { Write-Host "  (esse cliente não vê os dois seats como entidade — pule)" -ForegroundColor DarkGray }
    }
}

if ($HumanSeat -ge 0 -and $BotSeat -ge 0) {
    Run-Diff $HumanSeat $BotSeat
} else {
    Write-Host "`nPegue os seats no worldserver.log (host=0; humano-peer e bot) e rode:" -ForegroundColor Cyan
    Write-Host "  .\diag.ps1 -HumanSeat <h> -BotSeat <b>   (re-dumpa e diffa)" -ForegroundColor White
    Write-Host "  ou o diff direto num pidNN:  python .\diff_entities.py $DumpDir\pid<PID> <h> <b>" -ForegroundColor White
}
