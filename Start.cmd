@echo off
set "AT2APP=%~dp0src\Plant.Wpf\bin\Release\net48\AT2AT3.Simulator.exe"
if not exist "%AT2APP%" (
  echo Anwendung noch nicht gebaut. Bitte build.ps1 ausfuehren.
  pause
  exit /b 1
)
start "" "%AT2APP%"
