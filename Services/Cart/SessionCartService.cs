using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.ViewModels.Storefront.Cart;

namespace WebApplication2.Services.Cart;

public sealed partial class SessionCartService : ISessionCartService
{
    private const string CartSessionKey = "FastBuy.Cart.v1";
    private const string CustomerSessionKey = "FastBuy.MockCustomer.v1";
    private const int MaximumQuantityPerLine = 99;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionCartService> _logger;

    public SessionCartService(
        ApplicationDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionCartService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private ISession Session => _httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("Session cart requires an active HTTP request.");

    public CartHeaderSnapshot GetHeaderSnapshot()
    {
        var state = ReadCartState();
        return new CartHeaderSnapshot
        {
            LineCount = state.Lines.Count,
            TotalQuantity = state.Lines.Sum(line => Math.Max(0, line.Quantity))
        };
    }

    public MockCustomerViewModel GetMockCustomer()
    {
        var raw = Session.GetString(CustomerSessionKey);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredMockCustomer>(raw, JsonOptions);
                if (stored is not null)
                {
                    return ToCustomerViewModel(stored);
                }
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Mock customer session payload was invalid and has been reset.");
            }
        }

        var defaultCustomer = CreateDefaultCustomer();
        SaveCustomer(defaultCustomer);
        return ToCustomerViewModel(defaultCustomer);
    }

    public async Task<CartPageViewModel> GetCartAsync(CancellationToken cancellationToken)
    {
        var state = ReadCartState();
        var customer = GetMockCustomer();

        if (state.Lines.Count == 0)
        {
            return new CartPageViewModel
            {
                Customer = customer
            };
        }

        var variantIds = state.Lines
            .Select(line => line.VariantId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var rows = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new CartVariantDatabaseRow
            {
                ProductId = variant.ProductId,
                VariantId = variant.Id,
                ProductName = variant.Product.Name,
                ProductSlug = variant.Product.Slug,
                ProductIsActive = variant.Product.IsActive,
                Sku = variant.SKU,
                Color = variant.Color,
                Size = variant.Size,
                OriginalPrice = variant.Price,
                EffectivePrice = variant.CurrentPrice,
                StockQuantity = variant.StockQuantity,
                VariantIsActive = variant.IsActive,
                VariantImageUrl = variant.ImageUrl,
                ProductImageUrl = variant.Product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var rowByVariantId = rows.ToDictionary(row => row.VariantId);
        var viewLines = new List<CartLineViewModel>(state.Lines.Count);
        var stateChanged = false;

        foreach (var storedLine in state.Lines
                     .OrderBy(line => line.AddedAtUtc)
                     .ThenBy(line => line.VariantId))
        {
            if (!rowByVariantId.TryGetValue(storedLine.VariantId, out var row))
            {
                if (storedLine.IsSelected)
                {
                    storedLine.IsSelected = false;
                    stateChanged = true;
                }

                viewLines.Add(new CartLineViewModel
                {
                    ProductId = storedLine.ProductId,
                    VariantId = storedLine.VariantId,
                    ProductName = EmptyFallback(storedLine.ProductNameSnapshot, "Sản phẩm không còn tồn tại"),
                    ProductSlug = storedLine.ProductSlugSnapshot,
                    Sku = EmptyFallback(storedLine.SkuSnapshot, $"Variant #{storedLine.VariantId}"),
                    ImageUrl = NormalizeImage(storedLine.ImageUrlSnapshot),
                    VariantDescription = "Dữ liệu biến thể không còn tồn tại",
                    OriginalPrice = storedLine.LastKnownOriginalPrice,
                    EffectivePrice = storedLine.LastKnownEffectivePrice,
                    LineTotal = storedLine.LastKnownEffectivePrice * storedLine.Quantity,
                    Quantity = storedLine.Quantity,
                    MaxQuantity = 0,
                    StockQuantity = 0,
                    IsSelected = false,
                    CanSelect = false,
                    IsAvailable = false,
                    IssueCode = "VARIANT_NOT_FOUND",
                    IssueMessage = "Biến thể đã bị xóa hoặc không còn tồn tại."
                });

                continue;
            }

            var priceChanged = storedLine.LastKnownEffectivePrice > 0
                && storedLine.LastKnownEffectivePrice != row.EffectivePrice;

            var snapshotsChanged =
                storedLine.ProductId != row.ProductId
                || !string.Equals(storedLine.ProductNameSnapshot, row.ProductName, StringComparison.Ordinal)
                || !string.Equals(storedLine.ProductSlugSnapshot, row.ProductSlug, StringComparison.Ordinal)
                || !string.Equals(storedLine.SkuSnapshot, row.Sku, StringComparison.Ordinal)
                || storedLine.LastKnownOriginalPrice != row.OriginalPrice
                || storedLine.LastKnownEffectivePrice != row.EffectivePrice
                || !string.Equals(storedLine.ImageUrlSnapshot, ResolveImage(row), StringComparison.Ordinal);

            if (snapshotsChanged)
            {
                storedLine.ProductId = row.ProductId;
                storedLine.ProductNameSnapshot = row.ProductName;
                storedLine.ProductSlugSnapshot = row.ProductSlug;
                storedLine.SkuSnapshot = row.Sku;
                storedLine.LastKnownOriginalPrice = row.OriginalPrice;
                storedLine.LastKnownEffectivePrice = row.EffectivePrice;
                storedLine.ImageUrlSnapshot = ResolveImage(row);
                stateChanged = true;
            }

            var maxQuantity = Math.Min(Math.Max(row.StockQuantity, 0), MaximumQuantityPerLine);
            var productAvailable = row.ProductIsActive;
            var variantAvailable = row.VariantIsActive;
            var hasStock = row.StockQuantity > 0;
            var quantityWithinStock = storedLine.Quantity <= maxQuantity;
            var isAvailable = productAvailable && variantAvailable && hasStock;
            var canSelect = isAvailable && quantityWithinStock;

            string? issueCode = null;
            string? issueMessage = null;

            if (!productAvailable)
            {
                issueCode = "PRODUCT_INACTIVE";
                issueMessage = "Sản phẩm đang tạm ngừng bán.";
            }
            else if (!variantAvailable)
            {
                issueCode = "VARIANT_INACTIVE";
                issueMessage = "Biến thể đang tạm ngừng bán.";
            }
            else if (!hasStock)
            {
                issueCode = "OUT_OF_STOCK";
                issueMessage = "Biến thể đã hết hàng.";
            }
            else if (!quantityWithinStock)
            {
                issueCode = "QUANTITY_EXCEEDS_STOCK";
                issueMessage = $"Chỉ còn {row.StockQuantity} sản phẩm trong kho. Hãy giảm số lượng.";
            }

            if (!canSelect && storedLine.IsSelected)
            {
                storedLine.IsSelected = false;
                stateChanged = true;
            }

            viewLines.Add(new CartLineViewModel
            {
                ProductId = row.ProductId,
                VariantId = row.VariantId,
                ProductName = row.ProductName,
                ProductSlug = row.ProductSlug,
                Sku = row.Sku,
                ImageUrl = ResolveImage(row),
                VariantDescription = BuildVariantDescription(row.Color, row.Size, row.Sku),
                OriginalPrice = row.OriginalPrice,
                EffectivePrice = row.EffectivePrice,
                LineTotal = row.EffectivePrice * storedLine.Quantity,
                Quantity = storedLine.Quantity,
                MaxQuantity = maxQuantity,
                StockQuantity = row.StockQuantity,
                IsSelected = storedLine.IsSelected && canSelect,
                CanSelect = canSelect,
                IsAvailable = isAvailable,
                PriceChanged = priceChanged,
                IssueCode = issueCode,
                IssueMessage = issueMessage
            });
        }

        if (stateChanged)
        {
            SaveCartState(state);
        }

        var selectableLines = viewLines.Where(line => line.CanSelect).ToArray();
        var selectedLines = selectableLines.Where(line => line.IsSelected).ToArray();

        return new CartPageViewModel
        {
            Customer = customer,
            Items = viewLines,
            TotalLineCount = viewLines.Count,
            TotalQuantity = viewLines.Sum(line => line.Quantity),
            SelectedLineCount = selectedLines.Length,
            SelectedQuantity = selectedLines.Sum(line => line.Quantity),
            SelectedSubtotal = selectedLines.Sum(line => line.LineTotal),
            UnavailableLineCount = viewLines.Count(line => !line.CanSelect),
            AllAvailableSelected = selectableLines.Length > 0
                && selectableLines.All(line => line.IsSelected)
        };
    }

    public async Task<ProductOptionPickerViewModel?> GetProductOptionsAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId && item.IsActive)
            .Select(item => new ProductPickerDatabaseRow
            {
                ProductId = item.Id,
                ProductName = item.Name,
                ProductSlug = item.Slug,
                ProductImageUrl = item.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.ProductId == productId
                && variant.IsActive
                && variant.Price > 0
                && variant.CurrentPrice > 0)
            .OrderBy(variant => variant.Id)
            .Select(variant => new QuickVariantDatabaseRow
            {
                Id = variant.Id,
                Sku = variant.SKU,
                Color = variant.Color,
                Size = variant.Size,
                OriginalPrice = variant.Price,
                EffectivePrice = variant.CurrentPrice,
                StockQuantity = variant.StockQuantity,
                ImageUrl = variant.ImageUrl
            })
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            return new ProductOptionPickerViewModel
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                ProductSlug = product.ProductSlug,
                ImageUrl = NormalizeImage(product.ProductImageUrl),
                Variants = []
            };
        }

        // Contract AttributeGroups/Attributes và JavaScript phía Home xử lý
        // số lượng nhóm thuộc tính bất kỳ. Schema hiện tại có Color/Size;
        // thuộc tính mới chỉ cần bổ sung thêm một AttributeDefinition tại đây.
        var attributeDefinitions = new List<AttributeDefinition>();
        if (variants.Any(variant => !string.IsNullOrWhiteSpace(variant.Color)))
        {
            attributeDefinitions.Add(new AttributeDefinition(
                "color",
                "Màu sắc",
                variant => NormalizeAttributeValue(variant.Color)));
        }

        if (variants.Any(variant => !string.IsNullOrWhiteSpace(variant.Size)))
        {
            attributeDefinitions.Add(new AttributeDefinition(
                "size",
                "Kích thước / phiên bản",
                variant => NormalizeAttributeValue(variant.Size)));
        }

        var attributeMaps = variants.ToDictionary(
            variant => variant.Id,
            variant => attributeDefinitions.ToDictionary(
                definition => definition.Key,
                definition => definition.ValueSelector(variant),
                StringComparer.OrdinalIgnoreCase));

        var hasDuplicateCombinations = attributeDefinitions.Count > 0
            && attributeMaps.Values
                .GroupBy(
                    attributes => string.Join(
                        "\u001f",
                        attributeDefinitions.Select(definition => attributes[definition.Key])),
                    StringComparer.Ordinal)
                .Any(group => group.Count() > 1);

        if (attributeDefinitions.Count == 0 || hasDuplicateCombinations)
        {
            var skuDefinition = new AttributeDefinition(
                "variant",
                "Phiên bản",
                variant => variant.Sku);
            attributeDefinitions.Add(skuDefinition);

            foreach (var variant in variants)
            {
                attributeMaps[variant.Id][skuDefinition.Key] = skuDefinition.ValueSelector(variant);
            }
        }

        var groups = attributeDefinitions
            .Select(definition => new VariantAttributeGroupViewModel
            {
                Key = definition.Key,
                Label = definition.Label,
                Values = variants
                    .Select(definition.ValueSelector)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        var pickerVariants = variants
            .Select(variant => new QuickVariantViewModel
            {
                Id = variant.Id,
                Sku = variant.Sku,
                ImageUrl = NormalizeImage(
                    string.IsNullOrWhiteSpace(variant.ImageUrl)
                        ? product.ProductImageUrl
                        : variant.ImageUrl),
                OriginalPrice = variant.OriginalPrice,
                EffectivePrice = variant.EffectivePrice,
                StockQuantity = Math.Max(0, variant.StockQuantity),
                Attributes = new Dictionary<string, string>(
                    attributeMaps[variant.Id],
                    StringComparer.OrdinalIgnoreCase)
            })
            .ToArray();

        return new ProductOptionPickerViewModel
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            ProductSlug = product.ProductSlug,
            ImageUrl = NormalizeImage(product.ProductImageUrl),
            MinimumPrice = pickerVariants.Min(variant => variant.EffectivePrice),
            AvailableVariantCount = pickerVariants.Count(variant => variant.IsAvailable),
            AttributeGroups = groups,
            Variants = pickerVariants
        };
    }

    public async Task<CartOperationResult> AddAsync(
        AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProductId <= 0 || request.VariantId <= 0)
        {
            return CartOperationResult.Failure(
                "Sản phẩm hoặc biến thể không hợp lệ.",
                "INVALID_PRODUCT_VARIANT");
        }

        if (request.Quantity is < 1 or > MaximumQuantityPerLine)
        {
            return CartOperationResult.Failure(
                $"Số lượng phải từ 1 đến {MaximumQuantityPerLine}.",
                "INVALID_QUANTITY");
        }

        var row = await LoadVariantAsync(request.VariantId, cancellationToken);
        var validation = ValidatePurchasableVariant(row, request.ProductId, request.Quantity);
        if (validation is not null)
        {
            return validation;
        }

        var state = ReadCartState();
        if (request.BuyNow)
        {
            foreach (var line in state.Lines)
            {
                line.IsSelected = false;
            }
        }

        var existing = state.Lines.SingleOrDefault(line => line.VariantId == request.VariantId);
        if (existing is null)
        {
            state.Lines.Add(CreateStoredLine(row!, request.Quantity, true));
        }
        else
        {
            var desiredQuantity = request.BuyNow
                ? request.Quantity
                : existing.Quantity + request.Quantity;
            var maximumAllowed = Math.Min(row!.StockQuantity, MaximumQuantityPerLine);
            if (desiredQuantity > maximumAllowed)
            {
                return CartOperationResult.Failure(
                    request.BuyNow
                        ? $"Số lượng mua ngay là {desiredQuantity}, nhưng kho chỉ còn {row.StockQuantity}."
                        : $"Không thể thêm {request.Quantity} sản phẩm. Giỏ hàng đã có {existing.Quantity}, kho chỉ còn {row.StockQuantity}.",
                    "QUANTITY_EXCEEDS_STOCK",
                    await GetCartAsync(cancellationToken));
            }

            existing.Quantity = desiredQuantity;
            existing.IsSelected = true;
            RefreshStoredLine(existing, row);
        }

        state.Version++;
        SaveCartState(state);
        var cart = await GetCartAsync(cancellationToken);

        return CartOperationResult.Ok(
            request.BuyNow
                ? "Đã chuẩn bị sản phẩm mua ngay và chuyển đến giỏ hàng."
                : "Đã thêm sản phẩm vào giỏ hàng.",
            cart,
            request.BuyNow ? "/cart?mode=buy-now" : null);
    }

    public async Task<CartOperationResult> UpdateQuantityAsync(
        UpdateCartQuantityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.VariantId <= 0 || request.Quantity is < 1 or > MaximumQuantityPerLine)
        {
            return CartOperationResult.Failure(
                $"Số lượng phải từ 1 đến {MaximumQuantityPerLine}.",
                "INVALID_QUANTITY");
        }

        var state = ReadCartState();
        var line = state.Lines.SingleOrDefault(item => item.VariantId == request.VariantId);
        if (line is null)
        {
            return CartOperationResult.Failure(
                "Sản phẩm không còn trong giỏ hàng.",
                "CART_LINE_NOT_FOUND",
                await GetCartAsync(cancellationToken));
        }

        var row = await LoadVariantAsync(request.VariantId, cancellationToken);
        var validation = ValidatePurchasableVariant(row, line.ProductId, request.Quantity);
        if (validation is not null)
        {
            return WithCart(validation, await GetCartAsync(cancellationToken));
        }

        line.Quantity = request.Quantity;
        line.IsSelected = true;
        RefreshStoredLine(line, row!);
        state.Version++;
        SaveCartState(state);

        return CartOperationResult.Ok(
            "Đã cập nhật số lượng.",
            await GetCartAsync(cancellationToken));
    }

    public async Task<CartOperationResult> SetSelectionAsync(
        SetCartSelectionRequest request,
        CancellationToken cancellationToken)
    {
        var state = ReadCartState();
        var line = state.Lines.SingleOrDefault(item => item.VariantId == request.VariantId);
        if (line is null)
        {
            return CartOperationResult.Failure(
                "Sản phẩm không còn trong giỏ hàng.",
                "CART_LINE_NOT_FOUND",
                await GetCartAsync(cancellationToken));
        }

        if (request.IsSelected)
        {
            var row = await LoadVariantAsync(request.VariantId, cancellationToken);
            var validation = ValidatePurchasableVariant(row, line.ProductId, line.Quantity);
            if (validation is not null)
            {
                return WithCart(validation, await GetCartAsync(cancellationToken));
            }

            RefreshStoredLine(line, row!);
        }

        line.IsSelected = request.IsSelected;
        state.Version++;
        SaveCartState(state);

        return CartOperationResult.Ok(
            request.IsSelected ? "Đã chọn sản phẩm." : "Đã bỏ chọn sản phẩm.",
            await GetCartAsync(cancellationToken));
    }

    public async Task<CartOperationResult> SetAllSelectionAsync(
        SetAllCartSelectionRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await GetCartAsync(cancellationToken);
        var selectableIds = cart.Items
            .Where(item => item.CanSelect)
            .Select(item => item.VariantId)
            .ToHashSet();

        var state = ReadCartState();
        foreach (var line in state.Lines)
        {
            line.IsSelected = request.IsSelected && selectableIds.Contains(line.VariantId);
        }

        state.Version++;
        SaveCartState(state);

        return CartOperationResult.Ok(
            request.IsSelected
                ? "Đã chọn toàn bộ sản phẩm còn hàng."
                : "Đã bỏ chọn toàn bộ sản phẩm.",
            await GetCartAsync(cancellationToken));
    }

    public async Task<CartOperationResult> RemoveAsync(
        RemoveCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var state = ReadCartState();
        var removed = state.Lines.RemoveAll(line => line.VariantId == request.VariantId) > 0;
        if (!removed)
        {
            return CartOperationResult.Failure(
                "Sản phẩm không còn trong giỏ hàng.",
                "CART_LINE_NOT_FOUND",
                await GetCartAsync(cancellationToken));
        }

        state.Version++;
        SaveCartState(state);

        return CartOperationResult.Ok(
            "Đã xóa sản phẩm khỏi giỏ hàng.",
            await GetCartAsync(cancellationToken));
    }

    public async Task<CartOperationResult> UpdateMockCustomerAsync(
        UpdateMockCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();
        var phone = request.Phone.Trim();
        var addressLine = request.AddressLine.Trim();
        var ward = request.Ward.Trim();
        var district = request.District.Trim();
        var city = request.City.Trim();

        if (fullName.Length is < 2 or > 100)
        {
            return CartOperationResult.Failure(
                "Họ tên khách hàng phải từ 2 đến 100 ký tự.",
                "INVALID_CUSTOMER_NAME",
                await GetCartAsync(cancellationToken));
        }

        if (!IsValidEmail(email))
        {
            return CartOperationResult.Failure(
                "Email thử nghiệm không hợp lệ.",
                "INVALID_CUSTOMER_EMAIL",
                await GetCartAsync(cancellationToken));
        }

        if (!PhoneRegex().IsMatch(phone))
        {
            return CartOperationResult.Failure(
                "Số điện thoại phải có từ 9 đến 15 chữ số.",
                "INVALID_CUSTOMER_PHONE",
                await GetCartAsync(cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(addressLine)
            || string.IsNullOrWhiteSpace(district)
            || string.IsNullOrWhiteSpace(city))
        {
            return CartOperationResult.Failure(
                "Địa chỉ, quận/huyện và tỉnh/thành phố không được để trống.",
                "INVALID_CUSTOMER_ADDRESS",
                await GetCartAsync(cancellationToken));
        }

        var customer = new StoredMockCustomer
        {
            FullName = Truncate(fullName, 100),
            Email = Truncate(email, 150),
            Phone = Truncate(phone, 20),
            AddressLine = Truncate(addressLine, 200),
            Ward = Truncate(ward, 100),
            District = Truncate(district, 100),
            City = Truncate(city, 100)
        };

        SaveCustomer(customer);

        return CartOperationResult.Ok(
            "Đã lưu thông tin khách hàng thử nghiệm vào Session.",
            await GetCartAsync(cancellationToken));
    }

    public async Task<CartOperationResult> PrepareCheckoutAsync(
        CancellationToken cancellationToken)
    {
        var cart = await GetCartAsync(cancellationToken);
        if (!cart.HasSelectedItems)
        {
            return CartOperationResult.Failure(
                "Hãy chọn ít nhất một sản phẩm còn hàng trước khi tiếp tục.",
                "NO_SELECTED_ITEMS",
                cart);
        }

        var invalidSelected = cart.Items.Any(item => item.IsSelected && !item.CanSelect);
        if (invalidSelected)
        {
            return CartOperationResult.Failure(
                "Có sản phẩm được chọn không còn đủ điều kiện mua. Hãy kiểm tra lại giỏ hàng.",
                "SELECTED_ITEM_INVALID",
                cart);
        }

        var customer = cart.Customer;
        if (string.IsNullOrWhiteSpace(customer.FullName)
            || string.IsNullOrWhiteSpace(customer.Phone)
            || string.IsNullOrWhiteSpace(customer.DisplayAddress))
        {
            return CartOperationResult.Failure(
                "Thông tin khách hàng thử nghiệm chưa đầy đủ.",
                "MOCK_CUSTOMER_INCOMPLETE",
                cart);
        }

        return CartOperationResult.Ok(
            $"Giỏ hàng hợp lệ: {cart.SelectedQuantity} sản phẩm, tạm tính {cart.SelectedSubtotal:N0} ₫. Module đặt hàng sẽ sử dụng dữ liệu này ở phase tiếp theo.",
            cart);
    }

    private async Task<CartVariantDatabaseRow?> LoadVariantAsync(
        int variantId,
        CancellationToken cancellationToken)
    {
        return await _context.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.Id == variantId)
            .Select(variant => new CartVariantDatabaseRow
            {
                ProductId = variant.ProductId,
                VariantId = variant.Id,
                ProductName = variant.Product.Name,
                ProductSlug = variant.Product.Slug,
                ProductIsActive = variant.Product.IsActive,
                Sku = variant.SKU,
                Color = variant.Color,
                Size = variant.Size,
                OriginalPrice = variant.Price,
                EffectivePrice = variant.CurrentPrice,
                StockQuantity = variant.StockQuantity,
                VariantIsActive = variant.IsActive,
                VariantImageUrl = variant.ImageUrl,
                ProductImageUrl = variant.Product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static CartOperationResult? ValidatePurchasableVariant(
        CartVariantDatabaseRow? row,
        int expectedProductId,
        int quantity)
    {
        if (row is null)
        {
            return CartOperationResult.Failure(
                "Không tìm thấy biến thể sản phẩm.",
                "VARIANT_NOT_FOUND");
        }

        if (row.ProductId != expectedProductId)
        {
            return CartOperationResult.Failure(
                "Biến thể không thuộc sản phẩm đã chọn.",
                "VARIANT_PRODUCT_MISMATCH");
        }

        if (!row.ProductIsActive)
        {
            return CartOperationResult.Failure(
                "Sản phẩm đang tạm ngừng bán.",
                "PRODUCT_INACTIVE");
        }

        if (!row.VariantIsActive)
        {
            return CartOperationResult.Failure(
                "Biến thể đang tạm ngừng bán.",
                "VARIANT_INACTIVE");
        }

        if (row.OriginalPrice <= 0 || row.EffectivePrice <= 0)
        {
            return CartOperationResult.Failure(
                "Giá của biến thể không hợp lệ.",
                "VARIANT_PRICE_INVALID");
        }

        if (row.StockQuantity <= 0)
        {
            return CartOperationResult.Failure(
                "Biến thể đã hết hàng.",
                "OUT_OF_STOCK");
        }

        if (quantity > row.StockQuantity)
        {
            return CartOperationResult.Failure(
                $"Số lượng yêu cầu là {quantity}, nhưng kho chỉ còn {row.StockQuantity}.",
                "QUANTITY_EXCEEDS_STOCK");
        }

        return null;
    }

    private StoredCartState ReadCartState()
    {
        var raw = Session.GetString(CartSessionKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new StoredCartState();
        }

        try
        {
            var state = JsonSerializer.Deserialize<StoredCartState>(raw, JsonOptions)
                ?? new StoredCartState();

            state.Lines = state.Lines
                .Where(line => line.VariantId > 0 && line.Quantity > 0)
                .GroupBy(line => line.VariantId)
                .Select(group => group
                    .OrderByDescending(line => line.AddedAtUtc)
                    .First())
                .ToList();

            foreach (var line in state.Lines)
            {
                line.Quantity = Math.Clamp(line.Quantity, 1, MaximumQuantityPerLine);
            }

            return state;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Cart session payload was invalid and has been reset.");
            Session.Remove(CartSessionKey);
            return new StoredCartState();
        }
    }

    private void SaveCartState(StoredCartState state)
    {
        if (state.Lines.Count == 0)
        {
            Session.Remove(CartSessionKey);
            return;
        }

        Session.SetString(
            CartSessionKey,
            JsonSerializer.Serialize(state, JsonOptions));
    }

    private void SaveCustomer(StoredMockCustomer customer)
    {
        Session.SetString(
            CustomerSessionKey,
            JsonSerializer.Serialize(customer, JsonOptions));
    }

    private static StoredCartLine CreateStoredLine(
        CartVariantDatabaseRow row,
        int quantity,
        bool isSelected)
    {
        return new StoredCartLine
        {
            ProductId = row.ProductId,
            VariantId = row.VariantId,
            Quantity = quantity,
            IsSelected = isSelected,
            AddedAtUtc = DateTime.UtcNow,
            ProductNameSnapshot = row.ProductName,
            ProductSlugSnapshot = row.ProductSlug,
            SkuSnapshot = row.Sku,
            ImageUrlSnapshot = ResolveImage(row),
            LastKnownOriginalPrice = row.OriginalPrice,
            LastKnownEffectivePrice = row.EffectivePrice
        };
    }

    private static void RefreshStoredLine(
        StoredCartLine line,
        CartVariantDatabaseRow row)
    {
        line.ProductId = row.ProductId;
        line.ProductNameSnapshot = row.ProductName;
        line.ProductSlugSnapshot = row.ProductSlug;
        line.SkuSnapshot = row.Sku;
        line.ImageUrlSnapshot = ResolveImage(row);
        line.LastKnownOriginalPrice = row.OriginalPrice;
        line.LastKnownEffectivePrice = row.EffectivePrice;
    }

    private static StoredMockCustomer CreateDefaultCustomer()
    {
        return new StoredMockCustomer
        {
            FullName = "Lê Quốc Lạc",
            Email = "quoclac.test@fastbuy.local",
            Phone = "0901234567",
            AddressLine = "123 Đường Test FastBuy",
            Ward = "Phường 10",
            District = "Quận 10",
            City = "TP. Hồ Chí Minh"
        };
    }

    private static MockCustomerViewModel ToCustomerViewModel(StoredMockCustomer customer)
    {
        return new MockCustomerViewModel
        {
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            AddressLine = customer.AddressLine,
            Ward = customer.Ward,
            District = customer.District,
            City = customer.City
        };
    }

    private static string ResolveImage(CartVariantDatabaseRow row)
    {
        return NormalizeImage(
            string.IsNullOrWhiteSpace(row.VariantImageUrl)
                ? row.ProductImageUrl
                : row.VariantImageUrl);
    }

    private static string NormalizeImage(string? imageUrl)
    {
        return string.IsNullOrWhiteSpace(imageUrl)
            ? "/images/no-image.png"
            : imageUrl;
    }

    private static string NormalizeAttributeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "Mặc định"
            : value.Trim();
    }

    private static string BuildVariantDescription(
        string? color,
        string? size,
        string sku)
    {
        var values = new[] { color, size }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        return values.Length == 0
            ? sku
            : string.Join(" · ", values);
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            return new MailAddress(value).Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string EmptyFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static CartOperationResult WithCart(
        CartOperationResult result,
        CartPageViewModel cart)
    {
        return new CartOperationResult
        {
            Success = result.Success,
            Message = result.Message,
            ErrorCode = result.ErrorCode,
            RedirectUrl = result.RedirectUrl,
            Cart = cart
        };
    }

    [GeneratedRegex("^[0-9]{9,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    private sealed class StoredCartState
    {
        public long Version { get; set; }

        public List<StoredCartLine> Lines { get; set; } = [];
    }

    private sealed class StoredCartLine
    {
        public int ProductId { get; set; }

        public int VariantId { get; set; }

        public int Quantity { get; set; }

        public bool IsSelected { get; set; }

        public DateTime AddedAtUtc { get; set; }

        public string ProductNameSnapshot { get; set; } = string.Empty;

        public string ProductSlugSnapshot { get; set; } = string.Empty;

        public string SkuSnapshot { get; set; } = string.Empty;

        public string ImageUrlSnapshot { get; set; } = string.Empty;

        public decimal LastKnownOriginalPrice { get; set; }

        public decimal LastKnownEffectivePrice { get; set; }
    }

    private sealed class StoredMockCustomer
    {
        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string AddressLine { get; set; } = string.Empty;

        public string Ward { get; set; } = string.Empty;

        public string District { get; set; } = string.Empty;

        public string City { get; set; } = string.Empty;
    }

    private sealed class CartVariantDatabaseRow
    {
        public int ProductId { get; init; }

        public int VariantId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public string ProductSlug { get; init; } = string.Empty;

        public bool ProductIsActive { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string? Color { get; init; }

        public string? Size { get; init; }

        public decimal OriginalPrice { get; init; }

        public decimal EffectivePrice { get; init; }

        public int StockQuantity { get; init; }

        public bool VariantIsActive { get; init; }

        public string? VariantImageUrl { get; init; }

        public string? ProductImageUrl { get; init; }
    }

    private sealed class ProductPickerDatabaseRow
    {
        public int ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public string ProductSlug { get; init; } = string.Empty;

        public string? ProductImageUrl { get; init; }
    }

    private sealed class QuickVariantDatabaseRow
    {
        public int Id { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string? Color { get; init; }

        public string? Size { get; init; }

        public decimal OriginalPrice { get; init; }

        public decimal EffectivePrice { get; init; }

        public int StockQuantity { get; init; }

        public string? ImageUrl { get; init; }
    }

    private sealed record AttributeDefinition(
        string Key,
        string Label,
        Func<QuickVariantDatabaseRow, string> ValueSelector);
}
