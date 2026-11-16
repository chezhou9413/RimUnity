@echo off
dotnet run --project "%~dp0Tools\Build\RimUnity.Build.csproj" --configuration Release -- %*
exit /b %errorlevel%
