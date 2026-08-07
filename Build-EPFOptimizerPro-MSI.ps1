$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root "src\EPFOptimizerPro\EPFOptimizerPro.csproj"
$wixProject = Join-Path $root "installer\EPFOptimizerPro.Installer\EPFOptimizerPro.Installer.wixproj"
$publishDir = Join-Path $root "artifacts\publish\EPFOptimizerPro"
$installerDir = Join-Path $root "artifacts\installer"

[xml]$projectXml = Get-Content $appProject
$appVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Version introuvable dans EPFOptimizerPro.csproj"
}

Write-Host "Publication de EPF Optimizer Pro v$appVersion..."

dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDir

Write-Host "Construction du MSI v$appVersion..."
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

$publishDirForWix = $publishDir
if (-not $publishDirForWix.EndsWith('\')) {
    $publishDirForWix += '\'
}

$defineConstants = "ProductVersion=$appVersion;PublishDir=$publishDirForWix"

Write-Host "ProductVersion : $appVersion"
Write-Host "PublishDir     : $publishDirForWix"
Write-Host "DefineConstants: $defineConstants"

dotnet build $wixProject `
    -c Release `
    -o $installerDir `
    "/p:AcceptEula=wix7" `
    "/p:ProductVersion=$appVersion" `
    "/p:DefineConstants=$defineConstants"

$msi = Get-ChildItem $installerDir -Filter "*.msi" | Select-Object -First 1
if (-not $msi) {
    throw "MSI introuvable dans $installerDir"
}

$target = Join-Path $root "EPFOptimizerPro-v$appVersion-setup.msi"
Copy-Item $msi.FullName $target -Force
Write-Host "MSI cree : $target"