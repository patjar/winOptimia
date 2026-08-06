$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$productVersion = "3.9.20"
$appProject = Join-Path $root "src\EPFOptimizerPro\EPFOptimizerPro.csproj"
$wixProject = Join-Path $root "installer\EPFOptimizerPro.Installer\EPFOptimizerPro.Installer.wixproj"

$publishDir = Join-Path $root "artifacts\publish\EPFOptimizerPro"
$installerDir = Join-Path $root "artifacts\installer"

if (-not (Test-Path $appProject)) {
    throw "Projet applicatif introuvable : $appProject"
}

if (-not (Test-Path $wixProject)) {
    throw "Projet WiX introuvable : $wixProject"
}

Write-Host "Publication de EPF Optimizer Pro..."

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $installerDir) {
    Remove-Item $installerDir -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDir

Write-Host "Construction du MSI..."

# WiX consomme ces valeurs dans Package.wxs via :
# $(var.ProductVersion) et $(var.PublishDir)
$publishDirForWix = $publishDir.TrimEnd('\') + '\'
$defineConstants = "ProductVersion=$productVersion;PublishDir=$publishDirForWix"

Write-Host "ProductVersion : $productVersion"
Write-Host "PublishDir     : $publishDirForWix"
Write-Host "DefineConstants: $defineConstants"

dotnet build $wixProject `
    -c Release `
    -o $installerDir `
    "/p:AcceptEula=wix7" `
    "/p:ProductVersion=$productVersion" `
    "/p:PublishDir=$publishDirForWix" `
    "/p:DefineConstants=$defineConstants"

$msi = Get-ChildItem $installerDir -Filter "*.msi" -File | Select-Object -First 1

if (-not $msi) {
    throw "MSI introuvable dans $installerDir"
}

$target = Join-Path $root "EPFOptimizerPro-v3.9.20-setup.msi"
Copy-Item $msi.FullName $target -Force

Write-Host "MSI cree : $target"


