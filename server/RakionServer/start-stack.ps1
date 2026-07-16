# start-stack.ps1 — sobe todo o stack .NET do OpenRakion.
# Os web apps (launcher :80, admin :8080) precisam do runtime ASP.NET; como o .NET aqui é
# user-local, exportamos DOTNET_ROOT para o apphost achar o Microsoft.AspNetCore.App.
$dotnetRoots = @($env:DOTNET_ROOT, "$HOME\.dotnet", "$env:LOCALAPPDATA\Microsoft\dotnet") |
    Where-Object { $_ -and (Test-Path (Join-Path $_ 'dotnet.exe')) }
if ($dotnetRoots.Count -eq 0) { throw 'Runtime .NET não encontrado' }
$env:DOTNET_ROOT = $dotnetRoots[0]
$root = $PSScriptRoot
$bin = "bin\Release\net9.0"

function Start-Svc([string]$proc, [string]$exe, [string]$argline) {
    if (Get-Process -Name $proc -ErrorAction SilentlyContinue) { Write-Host "= $proc já rodando"; return }
    if (-not (Test-Path $exe)) { Write-Host "! ${proc}: build ausente ($exe) — rode 'dotnet build -c Release'"; return }
    Start-Process -FilePath $exe -ArgumentList $argline -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden
    Write-Host "+ $proc iniciado"
}

Start-Svc "BrokenServer"      "$root\src\RakionServer.Broker\$bin\BrokenServer.exe"        ""
Start-Svc "RakionWorldServer" "$root\src\RakionServer.World\$bin\RakionWorldServer.exe"    "`"$root\deploy\worldserver.ini`""
Start-Svc "BuddyServer"       "$root\src\RakionServer.Buddy\$bin\BuddyServer.exe"          "`"$root\deploy\worldserver.ini`""
if ($env:ConnectionStrings__Rakion -or
    ($env:Legacy__Enabled -eq 'false' -and $env:Auth__Enabled -ne 'true')) {
    Start-Svc "RakionLauncherWeb" "$root\src\RakionServer.LauncherWeb\$bin\RakionLauncherWeb.exe" ""
} else {
    Write-Host "! LauncherWeb não iniciado: defina ConnectionStrings__Rakion ou desligue Legacy/Auth"
}
if ($env:Admin__Password -and $env:ConnectionStrings__Rakion) {
    Start-Svc "RakionAdmin" "$root\src\RakionServer.Admin\$bin\RakionAdmin.exe" ""
} else {
    Write-Host "! RakionAdmin não iniciado: defina Admin__Password e ConnectionStrings__Rakion"
}

Write-Host ""
Write-Host "Stack: launcher configurado em LauncherWeb__Url  |  admin :8080  |  broker :40706  |  world :40708  |  buddy :8500/:8504"
Write-Host "Lembre do MariaDB (docker start rakion-db) antes."
