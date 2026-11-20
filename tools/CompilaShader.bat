@echo off
rem Compila gli shader FSR 3.1 nel bundle usato dalla mod (richiede Unity 2020.3.26f1).
rem La prima esecuzione puo' richiedere qualche minuto: Unity deve importare il progetto.
setlocal
set UNITY="C:\Program Files\Unity\Hub\Editor\2020.3.26f1\Editor\Unity.exe"
set ROOT=%~dp0..
echo Compilazione shader FSR in corso... (attendi, nessuna finestra di Unity si aprira')
%UNITY% -batchmode -quit -projectPath "%ROOT%\UnityShaders" -executeMethod BuildShaderBundle.Build -logFile "%ROOT%\UnityShaders\build.log"
if errorlevel 1 (
  echo.
  echo ERRORE: guarda il file UnityShaders\build.log
) else (
  echo.
  echo Fatto! Bundle creato in Bundles\outwardfsr_shaders
)
pause
