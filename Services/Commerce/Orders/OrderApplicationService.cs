using System.Data;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Checkout;
using WebApplication2.Services.Commerce.Inventory;

namespace WebApplication2.Services.Commerce.Orders;

public sealed class OrderApplicationService : IOrderApplicationService
{
    public const string CodPaymentMethod = "COD";
    public const string MockOnlinePaymentMethod = "MockOnline";
    public const string MockSuccessOutcome = "Success";
    public const string MockFailureOutcome = "Failure";

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly IShippingFeeCalculator _shippingFeeCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderApplicationService> _logger;

    public OrderApplicationService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IOrderNumberGenerator orderNumberGenerator,
        IShippingFeeCalculator shippingFeeCalculator,
        TimeProvider timeProvider,
        ILogger<OrderApplicationService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _orderNumberGenerator = orderNumberGenerator;
        _shippingFeeCalculator = shippingFeeCalculator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PlaceOrderResult?> FindByClientRequestIdAsync(
        string clientRequestId,
        int customerId,
        CancellationToken cancellationToken)
    {
        if (!IsValidClientRequestId(clientRequestId) || customerId <= 0)
        {
            return null;
        }

        var scopedClientRequestId = BuildScopedClientRequestId(
            customerId,
            clientRequestId);

        var order = await _context.Orders
            .AsNoTracking()
            .Where(item =>
                item.ClientRequestId == scopedClientRequestId
                && item.CustomerId == customerId)
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.PublicToken,
                item.OrderStatus,
                item.PaymentStatus
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var variantIds = await _context.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.Id)
            .Select(item => item.ProductVariantId)
            .ToArrayAsync(cancellationToken);

        return new PlaceOrderResult(
            order.Id,
            order.Code,
            order.PublicToken,
            order.OrderStatus,
            order.PaymentStatus,
            true,
            order.OrderStatus == OrderStatus.Placed
                && order.PaymentStatus != PaymentStatus.Failed,
            variantIds);
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(command);
        var scopedClientRequestId = BuildScopedClientRequestId(
            normalized.CustomerId,
            normalized.ClientRequestId);

        var existing = await FindByClientRequestIdAsync(
            normalized.ClientRequestId,
            normalized.CustomerId,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            existing = await FindByClientRequestIdAsync(
                normalized.ClientRequestId,
                normalized.CustomerId,
                cancellationToken);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var customerIsAvailable = await _context.Customers
                .AsNoTracking()
                .AnyAsync(item =>
                    item.Id == normalized.CustomerId
                    && item.Account.IsActive,
                    cancellationToken);

            if (!customerIsAvailable)
            {
                throw new CheckoutValidationException(
                    "Tài khoản khách hàng không còn khả dụng để đặt hàng.");
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var variantIds = normalized.Lines
                .Select(line => line.VariantId)
                .OrderBy(id => id)
                .ToArray();

            var variants = await _context.ProductVariants
                .Include(variant => variant.Product)
                .Where(variant => variantIds.Contains(variant.Id))
                .OrderBy(variant => variant.Id)
                .ToDictionaryAsync(variant => variant.Id, cancellationToken);

            if (variants.Count != variantIds.Length)
            {
                throw new CheckoutConflictException(
                    "Một hoặc nhiều sản phẩm trong giỏ không còn tồn tại.");
            }

            foreach (var line in normalized.Lines)
            {
                var variant = variants[line.VariantId];
                if (!variant.IsActive || !variant.Product.IsActive)
                {
                    throw new CheckoutConflictException(
                        $"Sản phẩm {variant.SKU} đang tạm ngừng bán.");
                }

                if (variant.CurrentPrice <= 0 || variant.Price <= 0)
                {
                    throw new CheckoutConflictException(
                        $"Giá của sản phẩm {variant.SKU} không hợp lệ.");
                }

                if (variant.CurrentPrice != line.ExpectedUnitPrice)
                {
                    throw new CheckoutConflictException(
                        $"Giá của sản phẩm {variant.SKU} vừa thay đổi. Vui lòng kiểm tra lại giỏ hàng.");
                }

                if (variant.StockQuantity < line.Quantity)
                {
                    throw new CheckoutConflictException(
                        $"Sản phẩm {variant.SKU} chỉ còn {variant.StockQuantity} trong kho.");
                }
            }

            var subtotal = normalized.Lines.Sum(line =>
                variants[line.VariantId].CurrentPrice * line.Quantity);
            var totalQuantity = normalized.Lines.Sum(line => line.Quantity);
            var shippingFee = _shippingFeeCalculator.Calculate(subtotal, totalQuantity);
            var grandTotal = subtotal + shippingFee;

            var isCod = normalized.PaymentMethod == CodPaymentMethod;
            var paymentSucceeded = isCod
                || normalized.MockPaymentOutcome == MockSuccessOutcome;

            var order = new Order
            {
                Code = await _orderNumberGenerator.GenerateAsync(cancellationToken),
                PublicToken = Guid.NewGuid(),
                ClientRequestId = scopedClientRequestId,
                CustomerId = normalized.CustomerId,
                CustomerName = normalized.CustomerName,
                CustomerEmail = normalized.CustomerEmail,
                CustomerPhone = normalized.CustomerPhone,
                ShippingAddressLine = normalized.ShippingAddressLine,
                ShippingWard = normalized.ShippingWardName,
                ShippingDistrict = normalized.ShippingDistrictName,
                ShippingCity = normalized.ShippingProvinceName,
                ShippingProvinceId = normalized.ShippingProvinceId,
                ShippingDistrictId = normalized.ShippingDistrictId,
                ShippingWardCode = normalized.ShippingWardCode,
                Subtotal = subtotal,
                ShippingFee = shippingFee,
                DiscountTotal = 0m,
                TaxTotal = 0m,
                GrandTotal = grandTotal,
                Currency = "VND",
                OrderStatus = paymentSucceeded ? OrderStatus.Placed : OrderStatus.Cancelled,
                PaymentStatus = isCod
                    ? PaymentStatus.CodPending
                    : paymentSucceeded
                        ? PaymentStatus.Paid
                        : PaymentStatus.Failed,
                FulfillmentStatus = paymentSucceeded
                    ? FulfillmentStatus.Unfulfilled
                    : FulfillmentStatus.Cancelled,
                PlacedAt = paymentSucceeded ? nowUtc : null,
                CancelledAt = paymentSucceeded ? null : nowUtc,
                CancelReason = paymentSucceeded
                    ? null
                    : "Thanh toán trực tuyến thử nghiệm không thành công.",
                CreatedAt = nowUtc
            };

            foreach (var line in normalized.Lines)
            {
                var variant = variants[line.VariantId];
                var description = BuildVariantDescription(
                    variant.Color,
                    variant.Size,
                    variant.SKU);

                order.Items.Add(new OrderItem
                {
                    ProductId = variant.ProductId,
                    ProductVariantId = variant.Id,
                    ProductName = variant.Product.Name,
                    Sku = variant.SKU,
                    VariantDescription = description,
                    ImageUrl = variant.ImageUrl,
                    ListPrice = variant.Price,
                    UnitPrice = variant.CurrentPrice,
                    DiscountAmount = Math.Max(0m, variant.Price - variant.CurrentPrice)
                        * line.Quantity,
                    TaxAmount = 0m,
                    Quantity = line.Quantity,
                    LineTotal = variant.CurrentPrice * line.Quantity,
                    CreatedAt = nowUtc
                });
            }

            order.PaymentTransactions.Add(new PaymentTransaction
            {
                Provider = isCod ? "Internal" : "Mock",
                Method = isCod ? CodPaymentMethod : "Online",
                Status = order.PaymentStatus,
                AttemptNumber = 1,
                Amount = grandTotal,
                Currency = "VND",
                MerchantReference = order.Code,
                ProviderTransactionId = !isCod && paymentSucceeded
                    ? $"MOCK-{Guid.NewGuid():N}"
                    : null,
                IdempotencyKey = $"{scopedClientRequestId}:payment:1",
                FailureCode = !isCod && !paymentSucceeded
                    ? "MOCK_PAYMENT_FAILED"
                    : null,
                FailureMessage = !isCod && !paymentSucceeded
                    ? "Giao dịch trực tuyến thử nghiệm được đặt ở trạng thái thất bại."
                    : null,
                CompletedAt = isCod ? null : nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

            if (paymentSucceeded)
            {
                order.Shipments.Add(new Shipment
                {
                    Provider = "GHN",
                    Status = ShipmentStatus.Draft,
                    Fee = shippingFee,
                    CodAmount = isCod ? grandTotal : 0m,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            }

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Order,
                FromStatus = null,
                ToStatus = order.OrderStatus.ToString(),
                Code = paymentSucceeded ? "ORDER_PLACED" : "ORDER_PAYMENT_FAILED",
                Title = paymentSucceeded ? "Đã đặt hàng" : "Đặt hàng chưa thành công",
                Description = paymentSucceeded
                    ? "Đơn hàng đã được tạo và giá đã được xác nhận."
                    : order.CancelReason,
                ChangedBy = "Checkout",
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = scopedClientRequestId
            });

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Payment,
                FromStatus = null,
                ToStatus = order.PaymentStatus.ToString(),
                Code = $"PAYMENT_{order.PaymentStatus.ToString().ToUpperInvariant()}",
                Title = isCod
                    ? "Thanh toán khi nhận hàng"
                    : paymentSucceeded
                        ? "Thanh toán thành công"
                        : "Thanh toán thất bại",
                ChangedBy = "Checkout",
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = scopedClientRequestId
            });

            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);

            if (paymentSucceeded)
            {
                var reservationLines = order.Items
                    .Select(item => new InventoryReservationLine(
                        item.Id,
                        item.ProductVariantId,
                        item.Quantity))
                    .ToArray();

                await _inventoryService.ReserveAsync(
                    order.Id,
                    reservationLines,
                    nowUtc.AddMinutes(30),
                    $"{scopedClientRequestId}:inventory",
                    "Checkout",
                    cancellationToken);

                await _inventoryService.CommitAsync(
                    order.Id,
                    scopedClientRequestId,
                    "Checkout",
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return new PlaceOrderResult(
                order.Id,
                order.Code,
                order.PublicToken,
                order.OrderStatus,
                order.PaymentStatus,
                false,
                paymentSucceeded,
                normalized.Lines.Select(line => line.VariantId).ToArray());
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw new CheckoutConflictException(
                "Giá hoặc tồn kho vừa được thay đổi bởi giao dịch khác. Vui lòng thử lại.",
                exception);
        }
        catch (DbUpdateException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                transaction = null;
            }

            _context.ChangeTracker.Clear();
            var duplicate = await FindByClientRequestIdAsync(
                normalized.ClientRequestId,
                normalized.CustomerId,
                cancellationToken);

            if (duplicate is not null)
            {
                return duplicate;
            }

            _logger.LogError(
                exception,
                "Checkout database update failed for request {ClientRequestId}.",
                normalized.ClientRequestId);
            throw;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<OrderReceipt?> GetReceiptAsync(
        Guid publicToken,
        int customerId,
        CancellationToken cancellationToken)
    {
        if (publicToken == Guid.Empty || customerId <= 0)
        {
            return null;
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Where(item =>
                item.PublicToken == publicToken
                && item.CustomerId == customerId)
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.PublicToken,
                item.OrderStatus,
                item.PaymentStatus,
                item.FulfillmentStatus,
                item.CustomerName,
                item.CustomerEmail,
                item.CustomerPhone,
                item.ShippingAddressLine,
                item.ShippingWard,
                item.ShippingDistrict,
                item.ShippingCity,
                item.Subtotal,
                item.ShippingFee,
                item.DiscountTotal,
                item.TaxTotal,
                item.GrandTotal,
                item.Currency,
                item.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = await _context.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.Id)
            .Select(item => new OrderReceiptLine(
                item.ProductName,
                item.VariantDescription ?? item.Sku,
                item.Sku,
                item.ImageUrl,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal))
            .ToArrayAsync(cancellationToken);

        return new OrderReceipt(
            order.Code,
            order.PublicToken,
            order.OrderStatus,
            order.PaymentStatus,
            order.FulfillmentStatus,
            order.CustomerName,
            order.CustomerEmail,
            order.CustomerPhone,
            order.ShippingAddressLine + ", "
                + order.ShippingWard + ", "
                + order.ShippingDistrict + ", "
                + order.ShippingCity,
            order.Subtotal,
            order.ShippingFee,
            order.DiscountTotal,
            order.TaxTotal,
            order.GrandTotal,
            order.Currency,
            order.CreatedAt,
            items);
    }

    private static PlaceOrderCommand Normalize(PlaceOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CustomerId <= 0)
        {
            throw new CheckoutValidationException("Tài khoản khách hàng không hợp lệ.");
        }

        if (!IsValidClientRequestId(command.ClientRequestId))
        {
            throw new CheckoutValidationException("ClientRequestId không hợp lệ.");
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new CheckoutValidationException("Đơn hàng phải có ít nhất một sản phẩm.");
        }

        var groupedLines = command.Lines
            .GroupBy(line => line.VariantId)
            .ToArray();

        if (groupedLines.Any(group => group.Key <= 0 || group.Count() != 1))
        {
            throw new CheckoutValidationException(
                "Danh sách sản phẩm đặt hàng không hợp lệ hoặc có biến thể trùng lặp.");
        }

        var lines = groupedLines
            .Select(group => group.Single())
            .OrderBy(line => line.VariantId)
            .ToArray();

        if (lines.Any(line => line.Quantity <= 0 || line.Quantity > 99))
        {
            throw new CheckoutValidationException("Số lượng sản phẩm đặt hàng không hợp lệ.");
        }

        if (lines.Any(line => line.ExpectedUnitPrice <= 0))
        {
            throw new CheckoutValidationException("Giá xác nhận của sản phẩm không hợp lệ.");
        }

        var paymentMethod = command.PaymentMethod?.Trim() ?? string.Empty;
        if (paymentMethod is not CodPaymentMethod and not MockOnlinePaymentMethod)
        {
            throw new CheckoutValidationException("Phương thức thanh toán không được hỗ trợ.");
        }

        var outcome = command.MockPaymentOutcome?.Trim() ?? MockSuccessOutcome;
        if (outcome is not MockSuccessOutcome and not MockFailureOutcome)
        {
            throw new CheckoutValidationException("Kết quả thanh toán thử nghiệm không hợp lệ.");
        }

        if (command.ShippingProvinceId <= 0 || command.ShippingDistrictId <= 0)
        {
            throw new CheckoutValidationException("Mã địa chỉ giao hàng không hợp lệ.");
        }

        var email = Required(command.CustomerEmail, 150, "Email");
        try
        {
            if (!string.Equals(
                    new MailAddress(email).Address,
                    email,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CheckoutValidationException("Email người nhận không hợp lệ.");
            }
        }
        catch (FormatException)
        {
            throw new CheckoutValidationException("Email người nhận không hợp lệ.");
        }

        var phone = Required(command.CustomerPhone, 20, "Số điện thoại");
        if (!Regex.IsMatch(phone, "^[0-9]{9,15}$", RegexOptions.CultureInvariant))
        {
            throw new CheckoutValidationException("Số điện thoại người nhận không hợp lệ.");
        }

        return command with
        {
            ClientRequestId = command.ClientRequestId.Trim(),
            CustomerName = Required(command.CustomerName, 100, "Họ tên người nhận"),
            CustomerEmail = email,
            CustomerPhone = phone,
            ShippingAddressLine = Required(command.ShippingAddressLine, 255, "Địa chỉ cụ thể"),
            ShippingProvinceName = Required(command.ShippingProvinceName, 100, "Tỉnh/thành phố"),
            ShippingDistrictName = Required(command.ShippingDistrictName, 100, "Quận/huyện"),
            ShippingWardCode = Required(command.ShippingWardCode, 30, "Mã phường/xã"),
            ShippingWardName = Required(command.ShippingWardName, 100, "Phường/xã"),
            PaymentMethod = paymentMethod,
            MockPaymentOutcome = outcome,
            Lines = lines
        };
    }

    private static string BuildScopedClientRequestId(
        int customerId,
        string clientRequestId)
    {
        if (customerId <= 0 || !IsValidClientRequestId(clientRequestId))
        {
            throw new CheckoutValidationException(
                "Định danh chống trùng của checkout không hợp lệ.");
        }

        return $"{customerId}:{clientRequestId}";
    }

    private static bool IsValidClientRequestId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length == 32
            && Guid.TryParseExact(value, "N", out _);
    }

    private static string Required(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CheckoutValidationException($"{fieldName} không được để trống.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new CheckoutValidationException(
                $"{fieldName} không được vượt {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string BuildVariantDescription(
        string? color,
        string? size,
        string sku)
    {
        var parts = new[] { color, size }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        return parts.Length == 0 ? sku : string.Join(" · ", parts);
    }
}
