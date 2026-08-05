Set-Location $PSScriptRoot
dotnet clean
dotnet restore
dotnet build .\EPFOptimizerPro.csproj -c Release
