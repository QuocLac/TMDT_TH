param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
$projectRootPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$projectFile = Join-Path $projectRootPath "WebApplication2.csproj"

if (-not (Test-Path $projectFile)) {
    throw "Khong tim thay WebApplication2.csproj tai: $projectRootPath"
}

Write-Host "Kiem tra Range(typeof(decimal), ...)" -ForegroundColor Cyan

$legacyMatches = Get-ChildItem $projectRootPath -Recurse -Filter *.cs -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|\.recovery-backup-[^\\/]+)[\\/]'
    } |
    Select-String -Pattern 'Range\s*\(\s*typeof\s*\(\s*decimal\s*\)' -AllMatches

if ($legacyMatches) {
    Write-Host "Van con Range(typeof(decimal), ...) trong source:" -ForegroundColor Red
    $legacyMatches | ForEach-Object {
        Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
    }
    exit 1
}

$attributeFile = Join-Path $projectRootPath "Areas\Admin\ViewModels\Validation\MoneyRangeAttribute.cs"
if (-not (Test-Path $attributeFile)) {
    throw "Thieu MoneyRangeAttribute.cs"
}

Write-Host "Khong con decimal RangeAttribute cu." -ForegroundColor Green
Write-Host "Xoa bin/obj va build lai..." -ForegroundColor Cyan

Remove-Item (Join-Path $projectRootPath "bin") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $projectRootPath "obj") -Recurse -Force -ErrorAction SilentlyContinue

Push-Location $projectRootPath
try {
    dotnet build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

Write-Host "Build thanh cong. Hay khoi dong lai ung dung." -ForegroundColor Green
