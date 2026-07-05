@echo off
REM Compila msgfix.dll (x86) — patch client-side do render da janela F9 do messenger.
REM Requer MSVC BuildTools. Saida: %~dp0msgfix.dll (bundle junto ao RakionLauncher).
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x86
cd /d "%~dp0"
cl /nologo /LD /O2 /MT msgfix.cpp /Fe:msgfix.dll
del /q msgfix.obj msgfix.exp msgfix.lib 2>nul
echo EXITCODE=%ERRORLEVEL%
