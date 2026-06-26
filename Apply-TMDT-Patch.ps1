# TMDT continuation patch installer - 2026-07-10
# Place TMDT_patch_payload.zip beside this script, then run from PowerShell.
[CmdletBinding()]
param(
    [string]$ProjectRoot = $PSScriptRoot,
    [string]$PatchZip = (Join-Path $PSScriptRoot 'TMDT_patch_payload.zip')
)


$ErrorActionPreference = 'Stop'
$expectedSha256 = '19b11fe7eb9e4112b61478471c45f918e0c6d6adba264c36bc53f226919a1559'


$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$PatchZip = [System.IO.Path]::GetFullPath($PatchZip)
$projectFile = Join-Path $ProjectRoot 'WebApplication2.csproj'


if (-not (Test-Path $projectFile)) {
    throw "Khong tim thay WebApplication2.csproj tai: $ProjectRoot"
}


if (-not (Test-Path $PatchZip)) {
    throw "Khong tim thay goi va TMDT_patch_payload.zip tai: $PatchZip"
}


$actualSha256 = (Get-FileHash -Path $PatchZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) {
    throw "Checksum goi va khong hop le. Expected: $expectedSha256; Actual: $actualSha256"
}


$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $ProjectRoot ".patch-backup-$stamp"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "tmdt-patch-$stamp"


New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null


try {
    Expand-Archive -Path $PatchZip -DestinationPath $tempRoot -Force


    $files = Get-ChildItem -Path $tempRoot -File -Recurse
    foreach ($file in $files) {
        $relativePath = [System.IO.Path]::GetRelativePath($tempRoot, $file.FullName)
        $destination = Join-Path $ProjectRoot $relativePath


        if (Test-Path $destination) {
            $backupPath = Join-Path $backupRoot $relativePath
            New-Item -ItemType Directory -Path (Split-Path $backupPath -Parent) -Force | Out-Null
            Copy-Item -Path $destination -Destination $backupPath -Force
        }


        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -Path $file.FullName -Destination $destination -Force
    }


    $legacyFiles = @(
        'Models/ViewModels/ProductCreateViewModel.cs',
        'Models/ViewModels/ProductEditViewModel.cs'
    )


    foreach ($legacyRelativePath in $legacyFiles) {
        $legacyPath = Join-Path $ProjectRoot $legacyRelativePath
        if (Test-Path $legacyPath) {
            $backupPath = Join-Path $backupRoot $legacyRelativePath
            New-Item -ItemType Directory -Path (Split-Path $backupPath -Parent) -Force | Out-Null
            Copy-Item -Path $legacyPath -Destination $backupPath -Force
            Remove-Item -Path $legacyPath -Force
        }
    }


    Write-Host "Da ap dung goi va TMDT." -ForegroundColor Green
    Write-Host "Backup: $backupRoot"
    Write-Host "Khong tu dong cap nhat database. Hay review migration truoc." -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Lenh kiem tra de xuat:'
    Write-Host '  dotnet restore'
    Write-Host '  dotnet build'
    Write-Host '  dotnet ef migrations script'
    Write-Host '  dotnet ef migrations list'
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}