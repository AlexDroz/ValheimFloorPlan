param(
    # Release by default for published packages. Day-to-day `dotnet build` stays Debug.
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$dllPath = Join-Path $repoRoot "bin\$Configuration\net462\ValheimFloorPlan.dll"
$stageRoot = Join-Path $repoRoot "artifacts\thunderstore\stage"
$manifestVersion = (Get-Content (Join-Path $repoRoot "manifest.json") | ConvertFrom-Json).version_number
$zipPath = Join-Path $repoRoot "artifacts\thunderstore\ValheimFloorPlan-$manifestVersion.zip"

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

# Always build fresh so the package can never ship a stale DLL.
Write-Host "Building $Configuration..."
dotnet build -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed"
}
if (-not (Test-Path $dllPath)) {
    throw "Build output not found at $dllPath"
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

# images/ is intentionally NOT packaged: README.md loads every screenshot from
# raw.githubusercontent.com, so bundling them only adds ~55 MB to the download.

$samplesSource = Join-Path $repoRoot "samples"
if (Test-Path $samplesSource) {
    # Copy only the loose sample files. Subfolders (samples/<BundleName>/) are created
    # by the mod when a .vpfset bundle is loaded and must not ship in the package.
    $samplesTarget = Join-Path $stageRoot "samples"
    New-Item -ItemType Directory -Path $samplesTarget -Force | Out-Null
    Get-ChildItem -Path $samplesSource -File | Copy-Item -Destination $samplesTarget -Force
}

$zipDir = Split-Path -Parent $zipPath
if (-not (Test-Path $zipDir)) {
    New-Item -ItemType Directory -Path $zipDir -Force | Out-Null
}
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

# Build the zip ourselves rather than with Compress-Archive: Windows PowerShell 5.1
# writes entry names with '\' separators, which violates the zip spec and makes
# Linux / Steam Deck mod managers extract files with backslashes in their names.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$stageFull = (Resolve-Path $stageRoot).Path.TrimEnd('\') + '\'
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in Get-ChildItem -Path $stageRoot -Recurse -Force) {
        $entryName = $item.FullName.Substring($stageFull.Length).Replace('\', '/')
        if ($item.PSIsContainer) {
            # Keep empty folders (e.g. Designer/Plans) as explicit directory entries.
            if (-not (Get-ChildItem -Path $item.FullName -Force | Select-Object -First 1)) {
                [void]$zip.CreateEntry($entryName + '/')
            }
        } else {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $item.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
} finally {
    $zip.Dispose()
}

$zipSizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Thunderstore package created: $zipPath ($zipSizeMb MB)" -ForegroundColor Green
Write-Host "Package includes: $Configuration mod DLL, Designer folder, samples." -ForegroundColor Cyan
