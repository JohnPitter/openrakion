# start-stack.ps1 — sobe todo o stack .NET do OpenRakion.
# Os web apps (launcher :80, admin :8080) precisam do runtime ASP.NET; como o .NET aqui é
# user-local, exportamos DOTNET_ROOT para o apphost achar o Microsoft.AspNetCore.App.
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
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
Start-Svc "RakionLauncherWeb" "$root\src\RakionServer.LauncherWeb\$bin\RakionLauncherWeb.exe" ""
Start-Svc "RakionAdmin"       "$root\src\RakionServer.Admin\$bin\RakionAdmin.exe"          ""

Write-Host ""
Write-Host "Stack: launcher http://localhost  |  admin http://localhost:8080  |  broker :40706  |  world :40708"
Write-Host "Lembre do MariaDB (docker start rakion-db) antes."
