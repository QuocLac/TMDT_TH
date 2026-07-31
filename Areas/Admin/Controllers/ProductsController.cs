using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Products;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;
using WebApplication2.Services.Media;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class ProductsController : Controller
{
    private const int PageSize = 20;

    private readonly ApplicationDbContext _context;
    private readonly IProductImageStorage _imageStorage;
    private readonly IVariantListPriceService _variantListPriceService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        ApplicationDbContext context,
        IProductImageStorage imageStorage,
        IVariantListPriceService variantListPriceService,
        TimeProvider timeProvider,
        ILogger<ProductsController> logger)
    {
        _context = context;
        _imageStorage = imageStorage;
        _variantListPriceService = variantListPriceService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? query,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        var normalizedQuery = query?.Trim() ?? string.Empty;

        var productsQuery = _context.Products
            .AsNoTracking()
            .Where(product => string.IsNullOrEmpty(normalizedQuery)
                || product.Name.Contains(normalizedQuery)
                || product.Slug.Contains(normalizedQuery)
                || product.Variants.Any(variant => variant.SKU.Contains(normalizedQuery)));

        var totalItems = await productsQuery.CountAsync(cancellationToken);
        var rows = await productsQuery
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Slug,
                product.IsActive,
                product.CreatedAt,
                CategoryName = product.Category.Name,
                BrandName = product.Brand != null ? product.Brand.Name : null,
                ImageUrl = product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                Variants = product.Variants
                    .OrderBy(variant => variant.SKU)
                    .Select(variant => new
                    {
                        variant.Id,
                        variant.SKU,
                        variant.Color,
                        variant.Size,
                        variant.Price,
                        variant.CurrentPrice,
                        variant.StockQuantity,
                        variant.IsActive,
                        variant.ImageUrl,
                        variant.RowVersion
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var model = new ProductIndexPageViewModel
        {
            Query = normalizedQuery,
            Page = page,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)PageSize),
            Items = rows.Select(product => new ProductIndexItemViewModel
            {
                Id = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                IsActive = product.IsActive,
                CreatedAt = product.CreatedAt,
                CategoryName = product.CategoryName,
                BrandName = product.BrandName,
                ImageUrl = string.IsNullOrWhiteSpace(product.ImageUrl)
                    ? "/images/no-image.png"
                    : product.ImageUrl,
                Variants = product.Variants.Select(variant => new ProductVariantItemViewModel
                {
                    Id = variant.Id,
                    SKU = variant.SKU,
                    Color = variant.Color,
                    Size = variant.Size,
                    Price = variant.Price,
                    CurrentPrice = variant.CurrentPrice,
                    StockQuantity = variant.StockQuantity,
                    IsActive = variant.IsActive,
                    ImageUrl = variant.ImageUrl,
                    RowVersion = Convert.ToBase64String(variant.RowVersion)
                }).ToList()
            }).ToList()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Category)
            .Include(item => item.Brand)
            .Include(item => item.Images)
            .Include(item => item.Variants)
            .Include(item => item.ProductPromotions)
                .ThenInclude(item => item.Promotion)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return product is null ? NotFound() : View(product);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new ProductCreateViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        ProductCreateViewModel model,
        CancellationToken cancellationToken)
    {
        Normalize(model);
        await ValidateCreateModelAsync(model, cancellationToken);

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var storedImages = new List<StoredProductImage>();
        try
        {
            var mainImage = await StoreAsync(model.MainImage!, cancellationToken);
            storedImages.Add(mainImage);

            var galleryImages = new List<StoredProductImage>();
            foreach (var file in model.GalleryImages.Where(file => file.Length > 0))
            {
                var stored = await StoreAsync(file, cancellationToken);
                galleryImages.Add(stored);
                storedImages.Add(stored);
            }

            var variantImages = new Dictionary<int, StoredProductImage>();
            for (var index = 0; index < model.Variants.Count; index++)
            {
                var file = model.Variants[index].Image;
                if (file is null || file.Length == 0)
                {
                    continue;
                }

                var stored = await StoreAsync(file, cancellationToken);
                variantImages[index] = stored;
                storedImages.Add(stored);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var product = new Product
            {
                Name = model.Name,
                Slug = model.Slug,
                Description = CleanNullable(model.Description),
                CategoryId = model.CategoryId,
                BrandId = model.BrandId,
                IsActive = model.IsActive,
                MetaTitle = CleanNullable(model.MetaTitle),
                MetaDescription = CleanNullable(model.MetaDescription),
                MetaKeywords = null,
                Images =
                [
                    new ProductImage { ImageUrl = mainImage.PublicPath, IsMain = true },
                    .. galleryImages.Select(image => new ProductImage
                    {
                        ImageUrl = image.PublicPath,
                        IsMain = false
                    })
                ],
                Variants = model.Variants.Select((variant, index) => new ProductVariant
                {
                    SKU = VariantSkuGenerator.CreateTemporarySku(),
                    Color = CleanNullable(variant.Color),
                    Size = CleanNullable(variant.Size),
                    Price = variant.Price,
                    CurrentPrice = variant.Price,
                    StockQuantity = variant.StockQuantity,
                    IsActive = variant.IsActive,
                    ImageUrl = variantImages.TryGetValue(index, out var image)
                        ? image.PublicPath
                        : null
                }).ToList(),
                ProductPromotions = model.SelectedPromotionIds
                    .Distinct()
                    .Select(promotionId => new ProductPromotion { PromotionId = promotionId })
                    .ToList()
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var variant in product.Variants)
            {
                variant.SKU = VariantSkuGenerator.CreateSku(variant.ProductId, variant.Id);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] = "Đã tạo sản phẩm và các biến thể.";
            return RedirectToAction(nameof(Edit), new { id = product.Id });
        }
        catch (ProductImageValidationException exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            ModelState.AddModelError(string.Empty, exception.UserMessage);
        }
        catch (ProductImageStorageException exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            _logger.LogError(exception, "Failed to store images while creating product.");
            ModelState.AddModelError(string.Empty, exception.UserMessage);
        }
        catch (DbUpdateException exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            _logger.LogWarning(exception, "Database rejected product creation for slug {Slug}.", model.Slug);
            ModelState.AddModelError(string.Empty, "Không thể lưu sản phẩm. Slug có thể đã tồn tại.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            _logger.LogError(exception, "Failed to create product.");
            ModelState.AddModelError(string.Empty, "Không thể tạo sản phẩm lúc này. Vui lòng thử lại.");
        }

        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Images)
            .Include(item => item.Variants)
            .Include(item => item.ProductPromotions)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var model = MapEditModel(product);
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(
        int id,
        ProductEditViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return NotFound();
        }

        Normalize(model);
        await ValidateEditModelAsync(model, cancellationToken);

        var product = await _context.Products
            .Include(item => item.Images)
            .Include(item => item.Variants)
            .Include(item => item.ProductPromotions)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            CopyCurrentState(model, product);
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var storedImages = new List<StoredProductImage>();
        var oldImagesToDelete = new List<string>();

        try
        {
            StoredProductImage? newMainImage = null;
            if (model.MainImage is { Length: > 0 })
            {
                newMainImage = await StoreAsync(model.MainImage, cancellationToken);
                storedImages.Add(newMainImage);
            }

            var newGalleryImages = new List<StoredProductImage>();
            foreach (var file in model.GalleryImages.Where(file => file.Length > 0))
            {
                var stored = await StoreAsync(file, cancellationToken);
                newGalleryImages.Add(stored);
                storedImages.Add(stored);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            product.Name = model.Name;
            product.Slug = model.Slug;
            product.Description = CleanNullable(model.Description);
            product.CategoryId = model.CategoryId;
            product.BrandId = model.BrandId;
            product.IsActive = model.IsActive;
            product.MetaTitle = CleanNullable(model.MetaTitle);
            product.MetaDescription = CleanNullable(model.MetaDescription);
            product.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            if (newMainImage is not null)
            {
                var mainImage = product.Images.FirstOrDefault(image => image.IsMain);
                if (mainImage is null)
                {
                    product.Images.Add(new ProductImage
                    {
                        ImageUrl = newMainImage.PublicPath,
                        IsMain = true
                    });
                }
                else
                {
                    oldImagesToDelete.Add(mainImage.ImageUrl);
                    mainImage.ImageUrl = newMainImage.PublicPath;
                    mainImage.UpdatedAt = product.UpdatedAt;
                }
            }

            foreach (var image in newGalleryImages)
            {
                product.Images.Add(new ProductImage
                {
                    ImageUrl = image.PublicPath,
                    IsMain = false
                });
            }

            _context.ProductPromotions.RemoveRange(product.ProductPromotions);
            foreach (var promotionId in model.SelectedPromotionIds.Distinct())
            {
                product.ProductPromotions.Add(new ProductPromotion
                {
                    ProductId = product.Id,
                    PromotionId = promotionId
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await DeleteReferencesQuietlyAsync(oldImagesToDelete, CancellationToken.None);
            TempData["SuccessMessage"] = "Đã cập nhật thông tin sản phẩm.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        catch (ProductImageValidationException exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            ModelState.AddModelError(string.Empty, exception.UserMessage);
        }
        catch (ProductImageStorageException exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            _logger.LogError(exception, "Failed to store images while editing product {ProductId}.", id);
            ModelState.AddModelError(string.Empty, exception.UserMessage);
        }
        catch (DbUpdateException exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            _logger.LogWarning(exception, "Database rejected product edit {ProductId}.", id);
            ModelState.AddModelError(string.Empty, "Không thể lưu sản phẩm. Slug có thể đã tồn tại.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await DeleteStoredImagesQuietlyAsync(storedImages, CancellationToken.None);
            _logger.LogError(exception, "Failed to edit product {ProductId}.", id);
            ModelState.AddModelError(string.Empty, "Không thể cập nhật sản phẩm lúc này.");
        }

        CopyCurrentState(model, product);
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FindAsync(new object[] { id }, cancellationToken);
        if (product is null)
        {
            return Json(new { success = false, message = "Không tìm thấy sản phẩm." });
        }

        product.IsActive = !product.IsActive;
        product.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = true,
            isActive = product.IsActive,
            message = product.IsActive ? "Đã hiển thị sản phẩm." : "Đã ẩn sản phẩm."
        });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleVariantStatus(
        [FromBody] ToggleVariantStatusRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new { success = false, message = "Dữ liệu biến thể không hợp lệ." });
        }

        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = FirstModelError() });
        }

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(item => item.Id == request.VariantId, cancellationToken);
        if (variant is null)
        {
            return Json(new { success = false, message = "Không tìm thấy biến thể." });
        }

        if (!TryApplyExpectedRowVersion(variant, request.RowVersion, out var rowVersionError))
        {
            return Json(new { success = false, message = rowVersionError });
        }

        try
        {
            variant.IsActive = !variant.IsActive;
            variant.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _context.SaveChangesAsync(cancellationToken);

            return Json(new
            {
                success = true,
                isActive = variant.IsActive,
                rowVersion = Convert.ToBase64String(variant.RowVersion)
            });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(exception, "Concurrent variant status update {VariantId}.", request.VariantId);
            return Json(new { success = false, message = ConcurrencyMessage });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateVariantPrice(
        [FromBody] UpdateVariantPriceRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new { success = false, message = "Dữ liệu giá không hợp lệ." });
        }

        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = FirstModelError() });
        }

        try
        {
            var correlationId = Guid.NewGuid().ToString("N");
            Response.Headers["X-Correlation-ID"] = correlationId;

            var result = await _variantListPriceService.ChangeAsync(
                new VariantListPriceChangeCommand(
                    request.VariantId,
                    request.NewPrice,
                    request.RowVersion,
                    GetActor(),
                    string.IsNullOrWhiteSpace(request.Note)
                        ? "Cập nhật giá niêm yết"
                        : request.Note.Trim(),
                    correlationId),
                cancellationToken);

            if (!result.Success || result.Snapshot is null)
            {
                return Json(new
                {
                    success = false,
                    message = result.Message,
                    errorCode = result.ErrorCode,
                    correlationId
                });
            }

            var snapshot = result.Snapshot;
            return Json(new
            {
                success = true,
                message = result.Message,
                listPrice = snapshot.ListPrice,
                currentPrice = snapshot.CurrentPrice,
                rowVersion = Convert.ToBase64String(snapshot.RowVersion),
                correlationId
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(exception, "Failed to update variant price {VariantId}.", request.VariantId);
            return Json(new { success = false, message = "Không thể cập nhật giá lúc này." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveQuickVariant(
        [FromForm] SaveProductVariantRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = FirstModelError() });
        }

        request.Color = CleanNullable(request.Color);
        request.Size = CleanNullable(request.Size);

        StoredProductImage? newImage = null;
        string? oldImageReference = null;

        var correlationId = Guid.NewGuid().ToString("N");
        Response.Headers["X-Correlation-ID"] = correlationId;

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            if (!await _context.Products.AnyAsync(
                    item => item.Id == request.ProductId,
                    cancellationToken))
            {
                return Json(new { success = false, message = "Không tìm thấy sản phẩm." });
            }

            var isNewVariant = request.VariantId == 0;
            ProductVariant variant;
            var listPriceChanged = false;

            if (isNewVariant)
            {
                variant = new ProductVariant
                {
                    ProductId = request.ProductId,
                    SKU = VariantSkuGenerator.CreateTemporarySku(),
                    Color = request.Color,
                    Size = request.Size,
                    Price = request.Price,
                    CurrentPrice = request.Price,
                    StockQuantity = request.StockQuantity,
                    IsActive = request.IsActive
                };
            }
            else
            {
                variant = await _context.ProductVariants
                    .FirstOrDefaultAsync(item => item.Id == request.VariantId, cancellationToken);
                if (variant is null)
                {
                    return Json(new { success = false, message = "Không tìm thấy biến thể." });
                }

                if (variant.ProductId != request.ProductId)
                {
                    return Json(new { success = false, message = "Biến thể không thuộc sản phẩm này." });
                }

                if (!TryApplyExpectedRowVersion(variant, request.RowVersion, out var rowVersionError))
                {
                    return Json(new { success = false, message = rowVersionError });
                }

                listPriceChanged = variant.Price != request.Price;
            }

            if (request.VariantImage is { Length: > 0 })
            {
                newImage = await StoreAsync(request.VariantImage, cancellationToken);
            }

            if (isNewVariant)
            {
                variant.ImageUrl = newImage?.PublicPath;
                _context.ProductVariants.Add(variant);
            }
            else
            {
                variant.Color = request.Color;
                variant.Size = request.Size;
                variant.StockQuantity = request.StockQuantity;
                variant.IsActive = request.IsActive;
                variant.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

                if (newImage is not null)
                {
                    oldImageReference = variant.ImageUrl;
                    variant.ImageUrl = newImage.PublicPath;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (isNewVariant)
            {
                variant.SKU = VariantSkuGenerator.CreateSku(variant.ProductId, variant.Id);
                await _context.SaveChangesAsync(cancellationToken);
            }

            VariantListPriceSnapshot? priceSnapshot = null;
            if (!isNewVariant && listPriceChanged)
            {
                var priceResult =
                    await _variantListPriceService.ChangeAsync(
                        new VariantListPriceChangeCommand(
                            variant.Id,
                            request.Price,
                            Convert.ToBase64String(variant.RowVersion),
                            GetActor(),
                            "Cập nhật biến thể và giá niêm yết",
                            correlationId),
                        cancellationToken);

                if (!priceResult.Success
                    || priceResult.Snapshot is null)
                {
                    await transaction.RollbackAsync(
                        CancellationToken.None);

                    return Json(new
                    {
                        success = false,
                        message = priceResult.Message,
                        errorCode = priceResult.ErrorCode,
                        correlationId
                    });
                }

                priceSnapshot = priceResult.Snapshot;
            }

            if (priceSnapshot is null)
            {
                priceSnapshot = new VariantListPriceSnapshot(
                    variant.Id,
                    variant.SKU,
                    variant.Price,
                    variant.CurrentPrice,
                    variant.RowVersion);
            }

            await transaction.CommitAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(oldImageReference))
            {
                await DeleteReferencesQuietlyAsync(
                    [oldImageReference],
                    CancellationToken.None);
            }

            return Json(new
            {
                success = true,
                message = isNewVariant
                    ? $"Đã thêm biến thể {variant.SKU}."
                    : $"Đã cập nhật biến thể {variant.SKU}.",
                data = new
                {
                    id = variant.Id,
                    sku = variant.SKU,
                    color = variant.Color,
                    size = variant.Size,
                    price = priceSnapshot.ListPrice,
                    currentPrice = priceSnapshot.CurrentPrice,
                    stockQuantity = variant.StockQuantity,
                    isActive = variant.IsActive,
                    imageUrl = variant.ImageUrl,
                    rowVersion =
                        Convert.ToBase64String(
                            priceSnapshot.RowVersion)
                },
                correlationId
            });
        }
        catch (ProductImageValidationException exception)
        {
            if (newImage is not null)
            {
                await DeleteStoredImagesQuietlyAsync([newImage], CancellationToken.None);
            }
            return Json(new { success = false, message = exception.UserMessage });
        }
        catch (ProductImageStorageException exception)
        {
            if (newImage is not null)
            {
                await DeleteStoredImagesQuietlyAsync([newImage], CancellationToken.None);
            }
            _logger.LogError(exception, "Failed to store variant image {VariantId}.", request.VariantId);
            return Json(new { success = false, message = exception.UserMessage });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (newImage is not null)
            {
                await DeleteStoredImagesQuietlyAsync([newImage], CancellationToken.None);
            }
            _logger.LogWarning(exception, "Concurrent quick variant update {VariantId}.", request.VariantId);
            return Json(new { success = false, message = ConcurrencyMessage });
        }
        catch (DbUpdateException exception)
        {
            if (newImage is not null)
            {
                await DeleteStoredImagesQuietlyAsync([newImage], CancellationToken.None);
            }
            _logger.LogWarning(exception, "Database rejected variant update {VariantId}.", request.VariantId);
            return Json(new { success = false, message = "Không thể lưu biến thể do dữ liệu không hợp lệ hoặc xung đột." });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (newImage is not null)
            {
                await DeleteStoredImagesQuietlyAsync([newImage], CancellationToken.None);
            }
            throw;
        }
        catch (Exception exception)
        {
            if (newImage is not null)
            {
                await DeleteStoredImagesQuietlyAsync([newImage], CancellationToken.None);
            }
            _logger.LogError(exception, "Failed to save variant {VariantId}.", request.VariantId);
            return Json(new { success = false, message = "Không thể lưu biến thể lúc này." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteProductImage(
        int imageId,
        CancellationToken cancellationToken)
    {
        var image = await _context.ProductImages
            .FirstOrDefaultAsync(item => item.Id == imageId, cancellationToken);

        if (image is null || image.IsMain)
        {
            return Json(new { success = false, message = "Không tìm thấy ảnh phụ hợp lệ." });
        }

        var imageReference = image.ImageUrl;
        _context.ProductImages.Remove(image);
        await _context.SaveChangesAsync(cancellationToken);
        await DeleteReferencesQuietlyAsync([imageReference], CancellationToken.None);

        return Json(new { success = true, message = "Đã xóa ảnh phụ." });
    }

    private async Task ValidateCreateModelAsync(
        ProductCreateViewModel model,
        CancellationToken cancellationToken)
    {
        await ValidateProductReferencesAsync(
            model.CategoryId,
            model.BrandId,
            model.SelectedPromotionIds,
            cancellationToken);

        if (await _context.Products.AnyAsync(item => item.Slug == model.Slug, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Slug), "Slug đã tồn tại.");
        }

        if (model.Variants.Count == 0)
        {
            ModelState.AddModelError(nameof(model.Variants), "Sản phẩm phải có ít nhất một biến thể.");
            return;
        }

    }

    private async Task ValidateEditModelAsync(
        ProductEditViewModel model,
        CancellationToken cancellationToken)
    {
        await ValidateProductReferencesAsync(
            model.CategoryId,
            model.BrandId,
            model.SelectedPromotionIds,
            cancellationToken);

        if (await _context.Products.AnyAsync(
                item => item.Slug == model.Slug && item.Id != model.Id,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Slug), "Slug đã tồn tại trên sản phẩm khác.");
        }
    }

    private async Task ValidateProductReferencesAsync(
        int categoryId,
        int? brandId,
        IReadOnlyCollection<int> promotionIds,
        CancellationToken cancellationToken)
    {
        if (!await _context.Categories.AnyAsync(item => item.Id == categoryId, cancellationToken))
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.CategoryId), "Danh mục không tồn tại.");
        }

        if (brandId.HasValue && !await _context.Brands.AnyAsync(
                item => item.Id == brandId.Value,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.BrandId), "Thương hiệu không hợp lệ.");
        }

        var distinctPromotionIds = promotionIds.Where(id => id > 0).Distinct().ToArray();
        if (distinctPromotionIds.Length == 0)
        {
            return;
        }

        var existingCount = await _context.Promotions.CountAsync(
            item => distinctPromotionIds.Contains(item.Id),
            cancellationToken);
        if (existingCount != distinctPromotionIds.Length)
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.SelectedPromotionIds), "Danh sách khuyến mãi không hợp lệ.");
        }
    }

    private async Task PopulateOptionsAsync(
        ProductCreateViewModel model,
        CancellationToken cancellationToken)
    {
        model.CategoryOptions = await LoadCategoryOptionsAsync(model.CategoryId, cancellationToken);
        model.BrandOptions = await LoadBrandOptionsAsync(model.BrandId, cancellationToken);
        model.PromotionOptions = await LoadPromotionOptionsAsync(model.SelectedPromotionIds, cancellationToken);
    }

    private async Task PopulateOptionsAsync(
        ProductEditViewModel model,
        CancellationToken cancellationToken)
    {
        model.CategoryOptions = await LoadCategoryOptionsAsync(model.CategoryId, cancellationToken);
        model.BrandOptions = await LoadBrandOptionsAsync(model.BrandId, cancellationToken);
        model.PromotionOptions = await LoadPromotionOptionsAsync(model.SelectedPromotionIds, cancellationToken);
    }

    private async Task<IReadOnlyList<SelectListItem>> LoadCategoryOptionsAsync(
        int selectedId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Categories
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name })
            .ToListAsync(cancellationToken);

        return rows
            .Select(item => new SelectListItem(item.Name, item.Id.ToString(), item.Id == selectedId))
            .ToList();
    }

    private async Task<IReadOnlyList<SelectListItem>> LoadBrandOptionsAsync(
        int? selectedId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Brands
            .AsNoTracking()
            .Where(item => item.IsActive || item.Id == selectedId)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name })
            .ToListAsync(cancellationToken);

        var options = rows
            .Select(item => new SelectListItem(item.Name, item.Id.ToString(), item.Id == selectedId))
            .ToList();
        options.Insert(0, new SelectListItem("Không có thương hiệu", string.Empty, selectedId is null));
        return options;
    }

    private async Task<IReadOnlyList<SelectListItem>> LoadPromotionOptionsAsync(
        IReadOnlyCollection<int> selectedIds,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var rows = await _context.Promotions
            .AsNoTracking()
            .Where(item => (item.IsActive && item.EndDate > nowUtc) || selectedIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.Name,
                IsSelectable = item.IsActive && item.EndDate > nowUtc
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(item => new SelectListItem(
                item.IsSelectable ? item.Name : $"{item.Name} (đã hết hiệu lực)",
                item.Id.ToString(),
                selectedIds.Contains(item.Id)))
            .ToList();
    }

    private ProductEditViewModel MapEditModel(Product product)
    {
        return new ProductEditViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            Description = product.Description,
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            IsActive = product.IsActive,
            MetaTitle = product.MetaTitle,
            MetaDescription = product.MetaDescription,
            CurrentMainImageUrl = product.Images.FirstOrDefault(image => image.IsMain)?.ImageUrl,
            CurrentGalleryImages = product.Images
                .Where(image => !image.IsMain)
                .OrderBy(image => image.Id)
                .Select(image => new ProductImageItemViewModel { Id = image.Id, Url = image.ImageUrl })
                .ToList(),
            SelectedPromotionIds = product.ProductPromotions.Select(item => item.PromotionId).ToList(),
            Variants = product.Variants
                .OrderBy(variant => variant.SKU)
                .Select(variant => new ProductVariantItemViewModel
                {
                    Id = variant.Id,
                    SKU = variant.SKU,
                    Color = variant.Color,
                    Size = variant.Size,
                    Price = variant.Price,
                    CurrentPrice = variant.CurrentPrice,
                    StockQuantity = variant.StockQuantity,
                    IsActive = variant.IsActive,
                    ImageUrl = variant.ImageUrl,
                    RowVersion = Convert.ToBase64String(variant.RowVersion)
                })
                .ToList()
        };
    }

    private void CopyCurrentState(ProductEditViewModel model, Product product)
    {
        model.CurrentMainImageUrl = product.Images.FirstOrDefault(image => image.IsMain)?.ImageUrl;
        model.CurrentGalleryImages = product.Images
            .Where(image => !image.IsMain)
            .OrderBy(image => image.Id)
            .Select(image => new ProductImageItemViewModel { Id = image.Id, Url = image.ImageUrl })
            .ToList();
        model.Variants = product.Variants
            .OrderBy(variant => variant.SKU)
            .Select(variant => new ProductVariantItemViewModel
            {
                Id = variant.Id,
                SKU = variant.SKU,
                Color = variant.Color,
                Size = variant.Size,
                Price = variant.Price,
                CurrentPrice = variant.CurrentPrice,
                StockQuantity = variant.StockQuantity,
                IsActive = variant.IsActive,
                ImageUrl = variant.ImageUrl,
                RowVersion = Convert.ToBase64String(variant.RowVersion)
            })
            .ToList();
    }

    private async Task<StoredProductImage> StoreAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return await _imageStorage.SaveAsync(
            new ProductImageUpload(stream, file.Length, file.FileName, file.ContentType),
            cancellationToken);
    }

    private async Task DeleteStoredImagesQuietlyAsync(
        IEnumerable<StoredProductImage> images,
        CancellationToken cancellationToken)
    {
        await DeleteReferencesQuietlyAsync(images.Select(image => image.StorageKey), cancellationToken);
    }

    private async Task DeleteReferencesQuietlyAsync(
        IEnumerable<string> references,
        CancellationToken cancellationToken)
    {
        foreach (var reference in references.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                await _imageStorage.DeleteAsync(reference, cancellationToken);
            }
            catch (Exception exception) when (exception is ProductImageStorageException or ProductImageValidationException)
            {
                _logger.LogWarning(exception, "Could not delete product image {ImageReference}.", reference);
            }
        }
    }

    private bool TryApplyExpectedRowVersion(
        ProductVariant variant,
        string? encodedRowVersion,
        out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(encodedRowVersion))
        {
            errorMessage = "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.";
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(encodedRowVersion);
            if (expected.Length != 8)
            {
                errorMessage = "Phiên bản dữ liệu không hợp lệ.";
                return false;
            }

            _context.Entry(variant).Property(item => item.RowVersion).OriginalValue = expected;
            return true;
        }
        catch (FormatException)
        {
            errorMessage = "Phiên bản dữ liệu không hợp lệ.";
            return false;
        }
    }

    private string FirstModelError()
    {
        return ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? "Dữ liệu không hợp lệ.";
    }

    private string GetActor()
    {
        return string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name.Trim();
    }

    private static void Normalize(ProductCreateViewModel model)
    {
        model.Name = model.Name.Trim();
        model.Slug = model.Slug.Trim().ToLowerInvariant();
        foreach (var variant in model.Variants)
        {
            variant.Color = CleanNullable(variant.Color);
            variant.Size = CleanNullable(variant.Size);
        }
    }

    private static void Normalize(ProductEditViewModel model)
    {
        model.Name = model.Name.Trim();
        model.Slug = model.Slug.Trim().ToLowerInvariant();
    }

    private static string? CleanNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private const string ConcurrencyMessage =
        "Dữ liệu vừa được thay đổi ở nơi khác. Vui lòng tải lại trang và thử lại.";
}
