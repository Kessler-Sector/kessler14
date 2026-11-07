@echo off
rem Runs a local dev server with a real persistent Frontier round instead of the development setup that runserver.bat
rem uses (NFDev map, no lobby, events off). The sector is saved to bin\Content.Server\data\kessler\persistence\.
rem Stop it by typing "shutdown" in this window, which saves the sector; closing the window does not save.
rem The full server log is also written to bin\Content.Server\logs\, so errors that scroll away or come before a crash are kept.
cd /d "%~dp0..\.."
echo To stop: type "shutdown" and wait for "Saved persistent sector" (about a minute). Closing the window loses what happened since the last autosave (every 15 minutes).
dotnet run --project Content.Server -- ^
  --cvar config.preset_development=false ^
  --cvar game.map=Dusk ^
  --cvar game.lobbyenabled=true ^
  --cvar game.role_timers=false ^
  --cvar kessler.persistence.enabled=true ^
  --cvar kessler.persistence.autosave_minutes=15 ^
  --cvar log.enabled=true ^
  --cvar log.path=logs ^
  %*
pause
