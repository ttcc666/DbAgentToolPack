#!/usr/bin/env bash
set -euo pipefail
dotnet pack ./src/DbAgent/DbAgent.csproj -c Release
if dotnet tool list --global | grep -q '^dbagent '; then
  dotnet tool update --global --add-source ./nupkg DbAgent.Tool
else
  dotnet tool install --global --add-source ./nupkg DbAgent.Tool
fi
