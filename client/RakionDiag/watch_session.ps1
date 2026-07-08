# watch_session.ps1 — observa AO VIVO os contadores da sessão (CSessionState + tick object) por LEITURA
# externa pura, p/ achar o CONTADOR DE GAMETICK CONFIRMADO que TRAVA quando o bot entra.
#
# Cadeia (cravada por disasm do engine.dll): pNet=[0x362ba778]; A(CSessionState)=[pNet+0x18];
# G(tick object)=[pNet+0x2c]. Os contadores de tick/sequência moram em A e G.
#
# Método: dumpa A[0..range] e G[0..range] a cada IntervalMs por DurationS; no fim reporta, por offset, os
# dwords que são CONTADORES (mudam monotonicamente). Rode DUAS vezes e compare:
#   (1) 2 humanos SÓ, se batendo (HIT funciona)  -> os contadores de tick AVANÇAM
#   (2) 2 humanos + bot no stage (HIT congela)   -> o contador CONFIRMADO deve TRAVAR (rate cai a ~0)
# O offset que avança em (1) e trava em (2) é o gametick confirmado — o alvo do fix.
#
# Uso:  .\watch_session.ps1                 # 15s, região 0x400
#       .\watch_session.ps1 -DurationS 30 -Tag "com-bot"
param(
    [int]$DurationS = 15,
    [int]$IntervalMs = 200,
    [int]$Range = 0x400,
    [string]$Tag = "run",
    [string]$OutDir = 'C:\temp\entdiff\watch'
)
$ErrorActionPreference = 'Stop'

if (-not ('RkDiagMem' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class RkDiagMem {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool i, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
    public static IntPtr Open(int pid){ return OpenProcess(0x0410, false, pid); }
    public static uint RU(IntPtr h, uint addr){ byte[] b=new byte[4]; IntPtr n; if(!ReadProcessMemory(h,(IntPtr)addr,b,4,out n)||(int)n!=4) return 0; return BitConverter.ToUInt32(b,0); }
    public static byte[] Read(IntPtr h, uint addr, int len){ byte[] b=new byte[len]; IntPtr n; ReadProcessMemory(h,(IntPtr)addr,b,len,out n); return b; }
}
"@
}

$procs = @(Get-Process rakion -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) { throw "rakion.exe não está rodando. Entre no stage e rode de novo." }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# escolhe o cliente do HOST (o que enxerga mais players remotos): mais entidades de slot com [entry+4].
function CountEntities($h) {
    $pNet = [RkDiagMem]::RU($h, 0x362ba778); if ($pNet -eq 0) { return 0 }
    $A = [RkDiagMem]::RU($h, $pNet + 0x18); if ($A -eq 0) { return 0 }
    $B = [RkDiagMem]::RU($h, $A + 0x10); if ($B -eq 0) { return 0 }
    $c = 0; for ($s=0; $s -lt 20; $s++) { if ([RkDiagMem]::RU($h, $B + [uint32]$s*0x100 + 4) -ne 0) { $c++ } }
    return $c
}

foreach ($p in $procs) {
    $h = [RkDiagMem]::Open($p.Id); if ($h -eq [IntPtr]::Zero) { continue }
    try {
        $pNet = [RkDiagMem]::RU($h, 0x362ba778)
        $A = [RkDiagMem]::RU($h, $pNet + 0x18)
        $G = [RkDiagMem]::RU($h, $pNet + 0x2c)
        $ents = CountEntities $h
        Write-Host ("PID {0}: pNet=0x{1:X8} A=0x{2:X8} G=0x{3:X8}  ({4} entidades)" -f $p.Id,$pNet,$A,$G,$ents) -ForegroundColor Cyan
        if ($A -eq 0 -or $G -eq 0) { Write-Host "  (sem sessão — pulei)" -ForegroundColor DarkGray; continue }

        # amostra A e G ao longo do tempo
        $snapsA = @(); $snapsG = @()
        $ticks = [int]($DurationS * 1000 / $IntervalMs)
        Write-Host ("  observando {0}s ({1} amostras a cada {2}ms)... mantenha o combate ativo." -f $DurationS,$ticks,$IntervalMs) -ForegroundColor DarkGray
        for ($t = 0; $t -lt $ticks; $t++) {
            $snapsA += ,([RkDiagMem]::Read($h, $A, $Range))
            $snapsG += ,([RkDiagMem]::Read($h, $G, $Range))
            Start-Sleep -Milliseconds $IntervalMs
        }

        # reporta os dwords que são CONTADORES (monotônicos crescentes, delta > 0)
        function ReportCounters($name, $base, $snaps) {
            Write-Host "  == $name (base 0x$('{0:X8}' -f $base)): contadores (dwords que sobem) ==" -ForegroundColor Yellow
            $found = 0
            for ($off = 0; $off + 4 -le $Range; $off += 4) {
                $vals = foreach ($s in $snaps) { [BitConverter]::ToUInt32($s, $off) }
                $first = $vals[0]; $last = $vals[-1]
                if ($last -le $first) { continue }         # não cresceu
                $mono = $true; $prev = $vals[0]
                foreach ($v in $vals) { if ($v -lt $prev) { $mono = $false; break }; $prev = $v }
                if (-not $mono) { continue }                # não é monotônico (ruído)
                $delta = $last - $first
                if ($delta -gt 0 -and $delta -lt 100000) {   # counter plausível de tick (não ponteiro)
                    Write-Host ("    +0x{0:X3}: {1} -> {2}  (+{3})" -f $off, $first, $last, $delta) -ForegroundColor Green
                    $found++
                }
            }
            if ($found -eq 0) { Write-Host "    (nenhum contador monotônico — sessão parada?)" -ForegroundColor DarkGray }
        }
        ReportCounters "A/CSessionState" $A $snapsA
        ReportCounters "G/tick object"   $G $snapsG

        # salva os snapshots crus p/ diff offline entre runs (com-bot vs sem-bot)
        $tagDir = Join-Path $OutDir ("{0}_pid{1}" -f $Tag, $p.Id)
        New-Item -ItemType Directory -Force $tagDir | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $tagDir "A_first.bin"), $snapsA[0])
        [System.IO.File]::WriteAllBytes((Join-Path $tagDir "A_last.bin"),  $snapsA[-1])
        [System.IO.File]::WriteAllBytes((Join-Path $tagDir "G_first.bin"), $snapsG[0])
        [System.IO.File]::WriteAllBytes((Join-Path $tagDir "G_last.bin"),  $snapsG[-1])
    } finally { [void][RkDiagMem]::CloseHandle($h) }
}

Write-Host "`nComo usar o resultado:" -ForegroundColor Cyan
Write-Host "  1) Rode com 2 HUMANOS batendo (HIT ok):   .\watch_session.ps1 -Tag sem-bot" -ForegroundColor White
Write-Host "  2) Rode com 2 humanos + BOT (HIT congela): .\watch_session.ps1 -Tag com-bot" -ForegroundColor White
Write-Host "  O contador que AVANÇA no (1) e TRAVA no (2) é o gametick confirmado." -ForegroundColor White
