param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$dllPath = Join-Path $repoRoot "bin\$Configuration\net462\ValheimFloorPlan.dll"
$stageRoot = Join-Path $repoRoot "artifacts\thunderstore\stage"
$zipPath = Join-Path $repoRoot "artifacts\thunderstore\ValheimFloorPlan-1.0.5.zip"

$requiredRootFiles = @(
    "manifest.json",
    "README.md",
    "CHANGELOG.md",
    "icon.png"
)

foreach ($file in $requiredRootFiles) {
    if (-not (Test-Path (Join-Path $repoRoot $file))) {
        throw "Missing required Thunderstore file: $file"
    }
}

if (-not (Test-Path $dllPath)) {
    Write-Host "Build output not found at $dllPath. Running dotnet build..."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed"
    }
}

if (Test-Path $stageRoot) {
    Remove-Item -Recurse -Force $stageRoot
}
New-Item -ItemType Directory -Path $stageRoot | Out-Null

$pluginTarget = Join-Path $stageRoot "BepInEx\plugins\ValheimFloorPlan"
New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null

Copy-Item $dllPath (Join-Path $pluginTarget "ValheimFloorPlan.dll") -Force
Copy-Item (Join-Path $repoRoot "Designer") (Join-Path $pluginTarget "Designer") -Recurse -Force

Copy-Item (Join-Path $repoRoot "manifest.json") (Join-Path $stageRoot "manifest.json") -Force
Copy-Item (Join-Path $repoRoot "README.md") (Join-Path $stageRoot "README.md") -Force
Copy-Item (Join-Path $repoRoot "CHANGELOG.md") (Join-Path $stageRoot "CHANGELOG.md") -Force
Copy-Item (Join-Path $repoRoot "icon.png") (Join-Path $stageRoot "icon.png") -Force

$imagesSource = Join-Path $repoRoot "images"
if (Test-Path $imagesSource) {
    Copy-Item $imagesSource (Join-Path $stageRoot "images") -Recurse -Force
}

$samplesSource = Join-Path $repoRoot "samples"
if (Test-Path $samplesSource) {
    Copy-Item $samplesSource (Join-Path $stageRoot "samples") -Recurse -Force
}

$zipDir = Split-Path -Parent $zipPath
if (-not (Test-Path $zipDir)) {
    New-Item -ItemType Directory -Path $zipDir -Force | Out-Null
}
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath

Write-Host "Thunderstore package created: $zipPath" -ForegroundColor Green
Write-Host "Package includes: mod DLL + Designer folder." -ForegroundColor Cyan
