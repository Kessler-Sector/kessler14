@echo off
rem Runs a local dev server with a real persistent Frontier round instead of the development setup that runserver.bat
rem uses (NFDev map, no lobby, events off). The sector is saved to bin\Content.Server\data\kessler\persistence\.
rem Stop it by typing "shutdown" in this window, which saves the sector; closing the window does not save.
rem The full server log is also written to bin\Content.Server\logs\, so errors that scroll away or come before a crash are kept.
cd /d "%~dp0..\.."
dotnet run --project Content.Server -- ^
  --cvar config.preset_development=false ^
  --cvar game.map=Dusk ^
  --cvar game.lobbyenabled=true ^
  --cvar game.role_timers=false ^
  --cvar kessler.persistence.enabled=true ^
  --cvar log.enabled=true ^
  --cvar log.path=logs ^
  %*
pause
