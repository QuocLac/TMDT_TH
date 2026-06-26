# TMDT recovery patch installer - 2026-07-10
# Place this script beside TMDT_recovery_patch_20260710.zip in the project root.
[CmdletBinding()]
param(
    [string]$ProjectRoot = $PSScriptRoot,
    [string]$PatchZip = (Join-Path $PSScriptRoot 'TMDT_recovery_patch_20260710.zip')
)

$ErrorActionPreference = 'Stop'
$expectedZipSha256 = '3388a058ac62ee3e389a70ea8e64c9984cedc435e4a5af63832e99679de22f42'

function Get-NormalizedFullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideRoot([string]$Root, [string]$Candidate) {
    $rootWithSeparator = $Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $Candidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Duong dan nam ngoai project root: $Candidate"
    }
}

$ProjectRoot = Get-NormalizedFullPath $ProjectRoot
$PatchZip = Get-NormalizedFullPath $PatchZip
$projectFile = Join-Path $ProjectRoot 'WebApplication2.csproj'

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Khong tim thay WebApplication2.csproj tai: $ProjectRoot"
}
if (-not (Test-Path -LiteralPath $PatchZip -PathType Leaf)) {
    throw "Khong tim thay goi phuc hoi tai: $PatchZip"
}

$actualZipSha256 = (Get-FileHash -LiteralPath $PatchZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualZipSha256 -ne $expectedZipSha256) {
    throw "Checksum ZIP khong hop le. Expected: $expectedZipSha256; Actual: $actualZipSha256"
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "tmdt-recovery-$stamp"
$backupRoot = Join-Path $ProjectRoot ".recovery-backup-$stamp"
$payloadRoot = Join-Path $tempRoot 'payload'
$manifestPath = Join-Path $tempRoot 'TMDT_recovery_manifest.json'

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    Expand-Archive -LiteralPath $PatchZip -DestinationPath $tempRoot -Force

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'Goi phuc hoi khong co manifest.'
    }
    if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
        throw 'Goi phuc hoi khong co thu muc payload.'
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

    foreach ($requiredRelativePath in $manifest.required_existing_files) {
        $requiredPath = Get-NormalizedFullPath (Join-Path $ProjectRoot $requiredRelativePath)
        Assert-PathInsideRoot $ProjectRoot $requiredPath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Project chua co prerequisite bat buoc: $requiredRelativePath"
        }
    }

    foreach ($property in $manifest.files.PSObject.Properties) {
        $relativePath = [string]$property.Name
        $expectedHash = ([string]$property.Value).ToLowerInvariant()
        $sourcePath = Get-NormalizedFullPath (Join-Path $payloadRoot $relativePath)
        Assert-PathInsideRoot (Get-NormalizedFullPath $payloadRoot) $sourcePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Thieu file payload: $relativePath"
        }
        $actualHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "Checksum payload khong hop le: $relativePath"
        }
    }

    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $writtenPaths = [System.Collections.Generic.List[string]]::new()

    try {
        foreach ($property in $manifest.files.PSObject.Properties) {
            $relativePath = [string]$property.Name
            $sourcePath = Get-NormalizedFullPath (Join-Path $payloadRoot $relativePath)
            $destinationPath = Get-NormalizedFullPath (Join-Path $ProjectRoot $relativePath)
            Assert-PathInsideRoot $ProjectRoot $destinationPath

            if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
                $backupPath = Get-NormalizedFullPath (Join-Path $backupRoot $relativePath)
                Assert-PathInsideRoot $backupRoot $backupPath
                New-Item -ItemType Directory -Path (Split-Path $backupPath -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $destinationPath -Destination $backupPath -Force
            }

            New-Item -ItemType Directory -Path (Split-Path $destinationPath -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
            $writtenPaths.Add($relativePath)
        }

        foreach ($legacyRelativePath in $manifest.legacy_files_to_remove) {
            $legacyPath = Get-NormalizedFullPath (Join-Path $ProjectRoot $legacyRelativePath)
            Assert-PathInsideRoot $ProjectRoot $legacyPath
            if (Test-Path -LiteralPath $legacyPath -PathType Leaf) {
                $legacyBackup = Get-NormalizedFullPath (Join-Path $backupRoot $legacyRelativePath)
                Assert-PathInsideRoot $backupRoot $legacyBackup
                New-Item -ItemType Directory -Path (Split-Path $legacyBackup -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $legacyPath -Destination $legacyBackup -Force
                Remove-Item -LiteralPath $legacyPath -Force
            }
        }
    }
    catch {
        Write-Warning 'Ap dung patch that bai. Dang khoi phuc source tu backup...'

        $rollbackPaths = $writtenPaths.ToArray()
        [array]::Reverse($rollbackPaths)
        foreach ($relativePath in $rollbackPaths) {
            $destinationPath = Get-NormalizedFullPath (Join-Path $ProjectRoot $relativePath)
            $backupPath = Get-NormalizedFullPath (Join-Path $backupRoot $relativePath)
            if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
                New-Item -ItemType Directory -Path (Split-Path $destinationPath -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $backupPath -Destination $destinationPath -Force
            }
            elseif (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
                Remove-Item -LiteralPath $destinationPath -Force
            }
        }

        foreach ($legacyRelativePath in $manifest.legacy_files_to_remove) {
            $legacyPath = Get-NormalizedFullPath (Join-Path $ProjectRoot $legacyRelativePath)
            $legacyBackup = Get-NormalizedFullPath (Join-Path $backupRoot $legacyRelativePath)
            if (Test-Path -LiteralPath $legacyBackup -PathType Leaf) {
                New-Item -ItemType Directory -Path (Split-Path $legacyPath -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $legacyBackup -Destination $legacyPath -Force
            }
        }

        throw
    }

    Write-Host 'Da ap dung TMDT recovery patch.' -ForegroundColor Green
    Write-Host "Backup source: $backupRoot"
    Write-Host 'Database CHUA duoc cap nhat.' -ForegroundColor Yellow
    Write-Host 'Buoc tiep theo: powershell -ExecutionPolicy Bypass -File .\Verify-TMDT-Recovery.ps1'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
