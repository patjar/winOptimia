$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$AppProject = Join-Path $Root "src\EPFOptimizerPro\EPFOptimizerPro.csproj"
$WixProject = Join-Path $Root "installer\EPFOptimizerPro.Installer\EPFOptimizerPro.Installer.wixproj"

if (!(Test-Path $AppProject)) {
    throw "Projet application introuvable : $AppProject"
}

if (!(Test-Path $WixProject)) {
    throw "Projet WiX introuvable : $WixProject"
}

$ProjectText = Get-Content $AppProject -Raw
$Match = [System.Text.RegularExpressions.Regex]::Match($ProjectText, "<Version>([^<]+)</Version>")

if (-not $Match.Success) {
    throw "Version introuvable dans EPFOptimizerPro.csproj"
}

$Version = $Match.Groups[1].Value

$PublishDir = Join-Path $Root "artifacts\publish\EPFOptimizerPro"
$InstallerDir = Join-Path $Root "artifacts\installer"

Write-Host "Publication de EPF Optimizer Pro v$Version..."

dotnet publish $AppProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $PublishDir

Write-Host "Construction du MSI v$Version..."

New-Item -ItemType Directory -Force -Path $InstallerDir | Out-Null

$PublishDirForWix = $PublishDir

if (-not $PublishDirForWix.EndsWith("\")) {
    $PublishDirForWix = $PublishDirForWix + "\"
}

$DefineConstants = "ProductVersion=$Version;PublishDir=$PublishDirForWix"

Write-Host "ProductVersion : $Version"
Write-Host "PublishDir     : $PublishDirForWix"
Write-Host "DefineConstants: $DefineConstants"

dotnet build $WixProject `
    -c Release `
    -o $InstallerDir `
    "/p:AcceptEula=wix7" `
    "/p:ProductVersion=$Version" `
    "/p:DefineConstants=$DefineConstants"

$Msi = Get-ChildItem $InstallerDir -Filter "*.msi" | Select-Object -First 1

if (-not $Msi) {
    throw "MSI introuvable dans $InstallerDir"
}

$Target = Join-Path $Root ("EPFOptimizerPro-v" + $Version + "-setup.msi")

Copy-Item $Msi.FullName $Target -Force

Write-Host "MSI cree : $Target"