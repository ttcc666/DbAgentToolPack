$ErrorActionPreference = "Stop"
dotnet pack .\src\DbAgent\DbAgent.csproj -c Release
dotnet tool update --global --add-source .\nupkg DbAgent.Tool 2>$null
if ($LASTEXITCODE -ne 0) {
    dotnet tool install --global --add-source .\nupkg DbAgent.Tool
}
