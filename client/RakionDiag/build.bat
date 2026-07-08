@echo off
REM Compila a entitydiff.dll (x86) — DLL passiva de diagnóstico do muro do HIT×N.
REM Ajuste o caminho do vcvars se o VS não for o 2022 BuildTools.
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x86
cd /d "%~dp0"
cl /nologo /LD /O2 /MT /EHsc entitydiff.cpp /Fe:C:\temp\entitydiff.dll
echo EXITCODE=%ERRORLEVEL%
