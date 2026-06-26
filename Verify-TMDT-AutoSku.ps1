param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$projectRootPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$projectFile = Join-Path $projectRootPath "WebApplication2.csproj"

if (-not (Test-Path $projectFile)) {
    throw "Khong tim thay WebApplication2.csproj tai: $projectRootPath"
}

$requiredFiles = @(
    "Services\Catalog\VariantSkuGenerator.cs",
    "Areas\Admin\Controllers\ProductsController.cs",
    "Areas\Admin\ViewModels\Products\ProductCreateViewModel.cs",
    "Areas\Admin\ViewModels\Products\ProductVariantRequests.cs",
    "Areas\Admin\Views\Products\Create.cshtml",
    "Areas\Admin\Views\Products\Edit.cshtml",
    "Areas\Admin\Views\Products\Index.cshtml",
    "wwwroot\js\pages\admin\products-editor.js",
    "wwwroot\js\pages\admin\products-index.js",
    "scripts\insert-sample-catalog-data.sql"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $projectRootPath $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Thieu file: $relativePath"
    }
}

$viewFiles = @(
    (Join-Path $projectRootPath "Areas\Admin\Views\Products\Create.cshtml"),
    (Join-Path $projectRootPath "Areas\Admin\Views\Products\Edit.cshtml"),
    (Join-Path $projectRootPath "Areas\Admin\Views\Products\Index.cshtml")
)

$skuInputPattern = 'name="SKU"|asp-for="Variants\[[^]]+\]\.SKU"|data-variant-property="SKU"'
$skuInputMatches = Select-String -Path $viewFiles -Pattern $skuInputPattern

if ($skuInputMatches) {
    Write-Host "Van con truong nhap SKU trong Razor:" -ForegroundColor Red
    $skuInputMatches | ForEach-Object {
        Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
    }
    exit 1
}

$controllerPath = Join-Path $projectRootPath "Areas\Admin\Controllers\ProductsController.cs"
$requestPath = Join-Path $projectRootPath "Areas\Admin\ViewModels\Products\ProductVariantRequests.cs"
$createModelPath = Join-Path $projectRootPath "Areas\Admin\ViewModels\Products\ProductCreateViewModel.cs"

$forbiddenServerMatches = Select-String `
    -Path @($controllerPath, $requestPath, $createModelPath) `
    -Pattern 'request\.SKU|Vui lòng nhập SKU|public string SKU'

if ($forbiddenServerMatches) {
    Write-Host "Backend van con nhan SKU tu client:" -ForegroundColor Red
    $forbiddenServerMatches | ForEach-Object {
        Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
    }
    exit 1
}

$generatorPath = Join-Path $projectRootPath "Services\Catalog\VariantSkuGenerator.cs"
$generatorContent = Get-Content $generatorPath -Raw
if (
    $generatorContent -notmatch 'SKU-P' -or
    $generatorContent -notmatch 'ProductIdFormat = "D6"' -or
    $generatorContent -notmatch 'VariantIdFormat = "D8"' -or
    $generatorContent -notmatch 'CreateSku\(int productId, int variantId\)'
) {
    throw "VariantSkuGenerator khong dung format SKU-P000001-V00000001."
}

$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) {
    & node --check (Join-Path $projectRootPath "wwwroot\js\pages\admin\products-editor.js")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & node --check (Join-Path $projectRootPath "wwwroot\js\pages\admin\products-index.js")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
else {
    Write-Warning "Khong tim thay Node.js; bo qua node --check."
}

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

Write-Host "SKU da duoc cap tu dong theo ProductId + VariantId va khong con input SKU." -ForegroundColor Green
Write-Host "Build thanh cong." -ForegroundColor Green
Write-Host "SQL mau: scripts\insert-sample-catalog-data.sql" -ForegroundColor Cyan
