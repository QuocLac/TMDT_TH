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
    public const string VnPayPaymentMethod = "VNPAY";

    private const int OnlinePaymentReservationMinutes = 65;

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderApplicationService> _logger;

    public OrderApplicationService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IOrderNumberGenerator orderNumberGenerator,
        TimeProvider timeProvider,
        ILogger<OrderApplicationService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _orderNumberGenerator = orderNumberGenerator;
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
                item.PaymentStatus,
                PaymentTransactionId = item.PaymentTransactions
                    .Where(transaction =>
                        transaction.Provider == "VNPAY"
                        && transaction.Status == PaymentStatus.Pending)
                    .OrderByDescending(transaction => transaction.AttemptNumber)
                    .ThenByDescending(transaction => transaction.Id)
                    .Select(transaction => (long?)transaction.Id)
                    .FirstOrDefault()
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
            order.OrderStatus is (
                    OrderStatus.Placed
                    or OrderStatus.Confirmed
                    or OrderStatus.Processing
                    or OrderStatus.Completed
                    or OrderStatus.Closed)
                && order.PaymentStatus is (
                    PaymentStatus.CodPending
                    or PaymentStatus.Paid),
            order.OrderStatus == OrderStatus.PendingPayment
                && order.PaymentStatus == PaymentStatus.Pending,
            order.PaymentTransactionId,
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
            var shippingFee = normalized.ShippingFee;
            var grandTotal = subtotal + shippingFee;
            var isCod = normalized.PaymentMethod == CodPaymentMethod;

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
                OrderStatus = isCod
                    ? OrderStatus.Placed
                    : OrderStatus.PendingPayment,
                PaymentStatus = isCod
                    ? PaymentStatus.CodPending
                    : PaymentStatus.Pending,
                FulfillmentStatus = FulfillmentStatus.Unfulfilled,
                PlacedAt = isCod ? nowUtc : null,
                CancelledAt = null,
                CancelReason = null,
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
                    DiscountAmount = Math.Max(
                        0m,
                        variant.Price - variant.CurrentPrice) * line.Quantity,
                    TaxAmount = 0m,
                    Quantity = line.Quantity,
                    LineTotal = variant.CurrentPrice * line.Quantity,
                    CreatedAt = nowUtc
                });
            }

            var paymentTransaction = new PaymentTransaction
            {
                Provider = isCod ? "Internal" : "VNPAY",
                Method = normalized.PaymentMethod,
                Status = order.PaymentStatus,
                AttemptNumber = 1,
                Amount = grandTotal,
                Currency = "VND",
                MerchantReference = order.Code,
                ProviderTransactionId = null,
                IdempotencyKey = $"{scopedClientRequestId}:payment:1",
                FailureCode = null,
                FailureMessage = null,
                CompletedAt = null,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            order.PaymentTransactions.Add(paymentTransaction);

            order.Shipments.Add(new Shipment
            {
                Provider = "GHN",
                Direction = ShipmentDirection.Outbound,
                Status = ShipmentStatus.Draft,
                ServiceCode = normalized.ShippingServiceId > 0
                    ? $"{normalized.ShippingServiceTypeId}:{normalized.ShippingServiceId}"
                    : normalized.ShippingServiceTypeId.ToString(),
                ServiceName = normalized.ShippingServiceName,
                Fee = shippingFee,
                CodAmount = isCod ? grandTotal : 0m,
                WeightGram = normalized.ShippingWeightGram,
                LengthCm = normalized.ShippingLengthCm,
                WidthCm = normalized.ShippingWidthCm,
                HeightCm = normalized.ShippingHeightCm,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Order,
                FromStatus = null,
                ToStatus = order.OrderStatus.ToString(),
                Code = isCod
                    ? "ORDER_PLACED"
                    : "ORDER_PENDING_VNPAY",
                Title = isCod
                    ? "Đơn hàng đã được ghi nhận"
                    : "Đơn hàng đang chờ thanh toán",
                Description = isCod
                    ? "Giá, tồn kho và phí giao hàng đã được xác nhận."
                    : "Tồn kho đang được giữ trong thời gian khách hàng thanh toán qua VNPay.",
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
                Code = isCod
                    ? "PAYMENT_COD_PENDING"
                    : "PAYMENT_VNPAY_PENDING",
                Title = isCod
                    ? "Thanh toán khi nhận hàng"
                    : "Chờ thanh toán qua VNPay",
                Description = isCod
                    ? "Số tiền sẽ được thu khi giao hàng."
                    : "FastBuy đang chờ VNPay xác nhận kết quả giao dịch.",
                ChangedBy = "Checkout",
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = scopedClientRequestId
            });

            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);

            var reservationLines = order.Items
                .Select(item => new InventoryReservationLine(
                    item.Id,
                    item.ProductVariantId,
                    item.Quantity))
                .ToArray();

            await _inventoryService.ReserveAsync(
                order.Id,
                reservationLines,
                nowUtc.AddMinutes(
                    isCod
                        ? 30
                        : OnlinePaymentReservationMinutes),
                $"{scopedClientRequestId}:inventory",
                "Checkout",
                cancellationToken);

            if (isCod)
            {
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
                isCod,
                !isCod,
                isCod ? null : paymentTransaction.Id,
                normalized.Lines
                    .Select(line => line.VariantId)
                    .ToArray());
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
        if (paymentMethod is not CodPaymentMethod and not VnPayPaymentMethod)
        {
            throw new CheckoutValidationException(
                "Phương thức thanh toán không được hỗ trợ.");
        }

        if (command.ShippingProvinceId <= 0
            || command.ShippingDistrictId <= 0)
        {
            throw new CheckoutValidationException(
                "Mã địa chỉ giao hàng không hợp lệ.");
        }

        if (command.ShippingFee < 0m
            || command.ShippingServiceId < 0
            || command.ShippingServiceTypeId <= 0
            || command.ShippingWeightGram <= 0
            || command.ShippingLengthCm <= 0
            || command.ShippingWidthCm <= 0
            || command.ShippingHeightCm <= 0)
        {
            throw new CheckoutValidationException(
                "Thông tin phí và gói giao hàng không hợp lệ.");
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
            ShippingServiceName = Required(
                command.ShippingServiceName,
                100,
                "Tên gói giao hàng"),
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
