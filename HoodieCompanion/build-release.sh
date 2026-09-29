#!/usr/bin/env bash
# Cross-build the Windows release from Linux/macOS (same output as build-release.bat).
set -euo pipefail
cd "$(dirname "$0")"
find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
rm -rf publish
dotnet test tests/HoodieCompanion.Tests/HoodieCompanion.Tests.csproj -c Release --nologo
dotnet publish src/HoodieCompanion/HoodieCompanion.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:DebugSymbols=false -o publish/win-x64 --nologo
(cd publish && cp ../README.md ../KNOWN_ISSUES.md win-x64/ && cd win-x64 && zip -q -9 ../HoodieCompanion-win-x64.zip HoodieCompanion.exe README.md KNOWN_ISSUES.md && rm README.md KNOWN_ISSUES.md)
echo "Done: publish/win-x64/HoodieCompanion.exe, publish/HoodieCompanion-win-x64.zip"
