@echo off
rem Builds the latest code, then starts Fourvale Multibox Manager.
cd /d "%~dp0..\.."
dotnet run --project src\Multibox.App
