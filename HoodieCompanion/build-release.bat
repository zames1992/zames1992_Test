@echo off
rem ===========================================================================
rem  Hoodie Companion - clean Release build
rem  Output: publish\win-x64\HoodieCompanion.exe  (self-contained, single file)
rem          publish\HoodieCompanion-win-x64.zip   (distributable)
rem  Requires the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
rem ===========================================================================
setlocal
cd /d "%~dp0"

echo [1/4] Cleaning bin, obj and publish ...
for /d /r src %%d in (bin obj) do if exist "%%d" rd /s /q "%%d"
for /d /r tests %%d in (bin obj) do if exist "%%d" rd /s /q "%%d"
if exist publish rd /s /q publish

echo [2/4] Running unit tests ...
dotnet test tests\HoodieCompanion.Tests\HoodieCompanion.Tests.csproj -c Release --nologo || goto :fail

echo [3/4] Publishing self-contained win-x64 single-file executable ...
dotnet publish src\HoodieCompanion\HoodieCompanion.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false -o publish\win-x64 --nologo || goto :fail

echo [4/4] Packaging ...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Compress-Archive -Force -Path 'publish\win-x64\HoodieCompanion.exe','README.md','KNOWN_ISSUES.md' -DestinationPath 'publish\HoodieCompanion-win-x64.zip'" || goto :fail

rem Keep a ready-to-run copy in the root of the repository folder (e.g. Desktop\Hoodie\HoodieCompanion.exe).
copy /y "publish\win-x64\HoodieCompanion.exe" "..\HoodieCompanion.exe" >nul || echo (could not update ..\HoodieCompanion.exe - is Hoodie running? Close it and build again)

echo.
echo Done:
echo   ..\HoodieCompanion.exe                 ^<-- run this to start Hoodie (root of the repository folder)
echo   publish\win-x64\HoodieCompanion.exe
echo   publish\HoodieCompanion-win-x64.zip
if not defined CI (
  explorer "publish\win-x64"
  pause
)
exit /b 0

:fail
echo.
echo BUILD FAILED
if not defined CI pause
exit /b 1
