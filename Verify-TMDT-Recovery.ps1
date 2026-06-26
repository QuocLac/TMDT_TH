# Verifies the recovery patch without updating the database.
[CmdletBinding()]
param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$projectFile = Join-Path $ProjectRoot 'WebApplication2.csproj'
$solutionFile = Join-Path $ProjectRoot 'WebApplication2.sln'
$artifactDirectory = Join-Path $ProjectRoot 'artifacts\recovery-verification'

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Khong tim thay WebApplication2.csproj tai: $ProjectRoot"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Khong tim thay .NET SDK. Hay cai .NET 9 SDK truoc khi xac minh.'
}

$versionText = (& dotnet --version).Trim()
$majorVersion = [int]($versionText.Split('.')[0])
if ($majorVersion -lt 9) {
    throw "Can .NET SDK 9 tro len. SDK hien tai: $versionText"
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$buildTarget = if (Test-Path -LiteralPath $solutionFile) { $solutionFile } else { $projectFile }
$sqlOutput = Join-Path $artifactDirectory 'CompletePriceCampaignConsistency.idempotent.sql'

Push-Location $ProjectRoot
try {
    Write-Host "SDK: $versionText" -ForegroundColor Cyan

    & dotnet restore $buildTarget
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore that bai.' }

    & dotnet build $buildTarget --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build that bai.' }

    $testProjects = @(Get-ChildItem -Path $ProjectRoot -Recurse -Filter '*.Tests.csproj' -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|\.recovery-backup-)[\\/]' })

    if ($testProjects.Count -eq 0) {
        Write-Warning 'Repository chua co test project. Khong co automated test de chay.'
    }
    else {
        foreach ($testProject in $testProjects) {
            & dotnet test $testProject.FullName --no-restore
            if ($LASTEXITCODE -ne 0) { throw "Test that bai: $($testProject.FullName)" }
        }
    }

    $migrationList = @(& dotnet ef migrations list --project $projectFile 2>&1)
    $migrationList | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw 'Khong chay duoc dotnet ef. Kiem tra dotnet-ef tool va package EF Tools.'
    }
    if (($migrationList -join "`n") -notmatch '20260710084500_CompletePriceCampaignConsistency') {
        throw 'Khong tim thay migration CompletePriceCampaignConsistency sau khi build.'
    }

    & dotnet ef migrations script --idempotent --project $projectFile --output $sqlOutput
    if ($LASTEXITCODE -ne 0) { throw 'Khong sinh duoc migration SQL.' }

    Write-Host "SQL review file: $sqlOutput" -ForegroundColor Green
    Write-Host 'Khong co lenh database update nao duoc chay.' -ForegroundColor Yellow

    if (Get-Command git -ErrorAction SilentlyContinue) {
        & git status --short
        & git diff --stat
    }
}
finally {
    Pop-Location
}
