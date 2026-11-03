@echo off
rem Runs a local dev server with a real persistent Frontier round instead of the development setup that runserver.bat
rem uses (NFDev map, no lobby, events off). The sector is saved to bin\Content.Server\data\kessler\persistence\.
rem Stop it by typing "shutdown" in this window, which saves the sector; closing the window does not save.
cd /d "%~dp0..\.."
dotnet run --project Content.Server -- ^
  --cvar config.preset_development=false ^
  --cvar game.map=Dusk ^
  --cvar game.lobbyenabled=true ^
  --cvar game.role_timers=false ^
  --cvar kessler.persistence.enabled=true ^
  %*
pause
