using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Inventory;

public sealed class InventoryService : IInventoryService
{
    private const int IdempotencyKeyMaxLength = 128;
    private const int ActorMaxLength = 100;
    private const int ReasonMaxLength = 500;
    private const int CorrelationIdMaxLength = 64;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<InventoryService> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StockReservation>> ReserveAsync(
        int orderId,
        IReadOnlyCollection<InventoryReservationLine> lines,
        DateTime expiresAtUtc,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        var normalizedLines = ValidateReservationRequest(
            orderId,
            lines,
            expiresAtUtc,
            idempotencyKey,
            actor);

        var reservationKeys = normalizedLines
            .ToDictionary(
                line => line.OrderItemId,
                line => BuildKey(idempotencyKey, "reserve", line.OrderItemId));

        var existing = await _context.StockReservations
            .AsNoTracking()
            .Where(item => reservationKeys.Values.Contains(item.IdempotencyKey))
            .OrderBy(item => item.OrderItemId)
            .ToArrayAsync(cancellationToken);

        if (existing.Length > 0)
        {
            ValidateExistingReservations(orderId, normalizedLines, reservationKeys, existing);
            return existing;
        }

        IDbContextTransaction? localTransaction = null;
        var ownsTransaction = _context.Database.CurrentTransaction is null;

        try
        {
            if (ownsTransaction)
            {
                localTransaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (expiresAtUtc <= nowUtc)
            {
                throw new InventoryValidationException("Thời hạn giữ kho phải nằm trong tương lai.");
            }

            var orderStatus = await _context.Orders
                .AsNoTracking()
                .Where(item => item.Id == orderId)
                .Select(item => (OrderStatus?)item.OrderStatus)
                .SingleOrDefaultAsync(cancellationToken);
            if (!orderStatus.HasValue)
            {
                throw new InventoryValidationException($"Không tìm thấy đơn hàng {orderId}.");
            }

            if (orderStatus.Value is not OrderStatus.PendingPayment and not OrderStatus.Placed)
            {
                throw new InventoryConflictException(
                    $"Không thể giữ kho khi đơn hàng đang ở trạng thái {orderStatus.Value}.");
            }

            var orderItemIds = normalizedLines.Select(item => item.OrderItemId).ToArray();
            var orderItems = await _context.OrderItems
                .AsNoTracking()
                .Where(item => item.OrderId == orderId && orderItemIds.Contains(item.Id))
                .Select(item => new
                {
                    item.Id,
                    item.ProductVariantId,
                    item.Quantity
                })
                .ToDictionaryAsync(item => item.Id, cancellationToken);

            if (orderItems.Count != normalizedLines.Count)
            {
                throw new InventoryValidationException(
                    "Danh sách giữ kho chứa sản phẩm không thuộc đơn hàng hiện tại.");
            }

            foreach (var line in normalizedLines)
            {
                var orderItem = orderItems[line.OrderItemId];
                if (orderItem.ProductVariantId != line.ProductVariantId)
                {
                    throw new InventoryValidationException(
                        $"Biến thể của dòng đơn hàng {line.OrderItemId} không khớp dữ liệu giữ kho.");
                }

                if (line.Quantity > orderItem.Quantity)
                {
                    throw new InventoryValidationException(
                        $"Số lượng giữ kho của dòng {line.OrderItemId} vượt số lượng trong đơn hàng.");
                }
            }

            var variantIds = normalizedLines
                .Select(item => item.ProductVariantId)
                .Distinct()
                .OrderBy(item => item)
                .ToArray();

            var variants = await _context.ProductVariants
                .Include(item => item.Product)
                .Where(item => variantIds.Contains(item.Id))
                .OrderBy(item => item.Id)
                .ToDictionaryAsync(item => item.Id, cancellationToken);

            if (variants.Count != variantIds.Length)
            {
                throw new InventoryValidationException(
                    "Một hoặc nhiều biến thể sản phẩm không còn tồn tại.");
            }

            var reservations = new List<StockReservation>(normalizedLines.Count);

            foreach (var line in normalizedLines)
            {
                var variant = variants[line.ProductVariantId];
                if (!variant.IsActive || !variant.Product.IsActive)
                {
                    throw new InventoryConflictException(
                        $"Biến thể {variant.SKU} hiện không thể bán.");
                }

                if (variant.StockQuantity < line.Quantity)
                {
                    throw new InventoryConflictException(
                        $"Biến thể {variant.SKU} chỉ còn {variant.StockQuantity} sản phẩm.");
                }

                var quantityBefore = variant.StockQuantity;
                variant.StockQuantity -= line.Quantity;
                variant.UpdatedAt = nowUtc;

                var reservationKey = reservationKeys[line.OrderItemId];
                var reservation = new StockReservation
                {
                    OrderId = orderId,
                    OrderItemId = line.OrderItemId,
                    ProductVariantId = line.ProductVariantId,
                    Quantity = line.Quantity,
                    Status = StockReservationStatus.Reserved,
                    IdempotencyKey = reservationKey,
                    ReservedAt = nowUtc,
                    ExpiresAt = expiresAtUtc,
                    CreatedAt = nowUtc
                };

                reservations.Add(reservation);
                _context.StockReservations.Add(reservation);
                _context.InventoryMovements.Add(new InventoryMovement
                {
                    ProductVariantId = line.ProductVariantId,
                    MovementType = InventoryMovementType.ReservationCreated,
                    QuantityDelta = -line.Quantity,
                    QuantityBefore = quantityBefore,
                    QuantityAfter = variant.StockQuantity,
                    ReferenceType = "OrderItem",
                    ReferenceId = line.OrderItemId,
                    IdempotencyKey = BuildKey(idempotencyKey, "movement-reserve", line.OrderItemId),
                    Reason = "Giữ tồn kho cho đơn hàng.",
                    CreatedBy = Normalize(actor, ActorMaxLength, nameof(actor)),
                    CreatedAt = nowUtc
                });
            }

            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                Category = OrderHistoryCategory.Inventory,
                FromStatus = null,
                ToStatus = StockReservationStatus.Reserved.ToString(),
                Code = "INVENTORY_RESERVED",
                Title = "Đã giữ tồn kho",
                Description = $"Đã giữ {normalizedLines.Sum(item => item.Quantity)} sản phẩm cho đơn hàng.",
                ChangedBy = Normalize(actor, ActorMaxLength, nameof(actor)),
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = NormalizeOptional(idempotencyKey, CorrelationIdMaxLength)
            });

            await _context.SaveChangesAsync(cancellationToken);

            if (localTransaction is not null)
            {
                await localTransaction.CommitAsync(cancellationToken);
            }

            return reservations;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (localTransaction is not null)
            {
                await localTransaction.RollbackAsync(cancellationToken);
            }

            _logger.LogWarning(
                exception,
                "Inventory reservation for order {OrderId} lost an optimistic concurrency race.",
                orderId);
            throw new InventoryConflictException(
                "Tồn kho vừa thay đổi bởi giao dịch khác. Vui lòng kiểm tra lại giỏ hàng.",
                exception);
        }
        catch
        {
            if (localTransaction is not null)
            {
                await localTransaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (localTransaction is not null)
            {
                await localTransaction.DisposeAsync();
            }
        }
    }

    public async Task CommitAsync(
        int orderId,
        string correlationId,
        string actor,
        CancellationToken cancellationToken)
    {
        ValidateOperationInput(orderId, correlationId, actor);

        var reservations = await _context.StockReservations
            .Where(item => item.OrderId == orderId)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

        if (reservations.Length == 0)
        {
            throw new InventoryValidationException(
                $"Đơn hàng {orderId} chưa có bản ghi giữ kho.");
        }

        if (reservations.All(item => item.Status == StockReservationStatus.Committed))
        {
            return;
        }

        if (reservations.Any(item =>
                item.Status is StockReservationStatus.Released or StockReservationStatus.Expired))
        {
            throw new InventoryConflictException(
                "Không thể commit vì tồn kho của đơn hàng đã được giải phóng hoặc hết hạn.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var reservation in reservations)
        {
            reservation.Status = StockReservationStatus.Committed;
            reservation.CommittedAt = nowUtc;
            reservation.UpdatedAt = nowUtc;
        }

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Category = OrderHistoryCategory.Inventory,
            FromStatus = StockReservationStatus.Reserved.ToString(),
            ToStatus = StockReservationStatus.Committed.ToString(),
            Code = "INVENTORY_COMMITTED",
            Title = "Đã xác nhận xuất kho",
            ChangedBy = Normalize(actor, ActorMaxLength, nameof(actor)),
            CustomerVisible = false,
            OccurredAt = nowUtc,
            CorrelationId = NormalizeOptional(correlationId, CorrelationIdMaxLength)
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InventoryConflictException(
                "Trạng thái giữ kho vừa được cập nhật bởi giao dịch khác.",
                exception);
        }
    }

    public async Task ReleaseAsync(
        int orderId,
        string reason,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        ValidateOperationInput(orderId, idempotencyKey, actor);
        var normalizedReason = Normalize(reason, ReasonMaxLength, nameof(reason));

        IDbContextTransaction? localTransaction = null;
        var ownsTransaction = _context.Database.CurrentTransaction is null;

        try
        {
            if (ownsTransaction)
            {
                localTransaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var reservations = await _context.StockReservations
                .Where(item => item.OrderId == orderId)
                .OrderBy(item => item.ProductVariantId)
                .ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);

            if (reservations.Length == 0)
            {
                throw new InventoryValidationException(
                    $"Đơn hàng {orderId} chưa có bản ghi giữ kho.");
            }

            var releasable = reservations
                .Where(item => item.Status is StockReservationStatus.Reserved or StockReservationStatus.Committed)
                .ToArray();

            if (releasable.Length == 0)
            {
                return;
            }

            var variantIds = releasable
                .Select(item => item.ProductVariantId)
                .Distinct()
                .OrderBy(item => item)
                .ToArray();

            var variants = await _context.ProductVariants
                .Where(item => variantIds.Contains(item.Id))
                .OrderBy(item => item.Id)
                .ToDictionaryAsync(item => item.Id, cancellationToken);

            if (variants.Count != variantIds.Length)
            {
                throw new InventoryValidationException(
                    "Không thể hoàn kho vì một hoặc nhiều biến thể không còn tồn tại.");
            }

            var movementKeys = releasable.ToDictionary(
                item => item.Id,
                item => BuildKey(idempotencyKey, "movement-release", item.Id));
            var existingMovementKeys = await _context.InventoryMovements
                .AsNoTracking()
                .Where(item => movementKeys.Values.Contains(item.IdempotencyKey))
                .Select(item => item.IdempotencyKey)
                .ToHashSetAsync(cancellationToken);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var reservation in releasable)
            {
                var movementKey = movementKeys[reservation.Id];
                if (existingMovementKeys.Contains(movementKey))
                {
                    reservation.Status = StockReservationStatus.Released;
                    reservation.ReleasedAt ??= nowUtc;
                    reservation.ReleaseReason ??= normalizedReason;
                    reservation.UpdatedAt = nowUtc;
                    continue;
                }

                var variant = variants[reservation.ProductVariantId];
                var quantityBefore = variant.StockQuantity;
                variant.StockQuantity += reservation.Quantity;
                variant.UpdatedAt = nowUtc;

                reservation.Status = StockReservationStatus.Released;
                reservation.ReleasedAt = nowUtc;
                reservation.ReleaseReason = normalizedReason;
                reservation.UpdatedAt = nowUtc;

                _context.InventoryMovements.Add(new InventoryMovement
                {
                    ProductVariantId = reservation.ProductVariantId,
                    MovementType = InventoryMovementType.ReservationReleased,
                    QuantityDelta = reservation.Quantity,
                    QuantityBefore = quantityBefore,
                    QuantityAfter = variant.StockQuantity,
                    ReferenceType = "StockReservation",
                    ReferenceId = reservation.Id,
                    IdempotencyKey = movementKey,
                    Reason = normalizedReason,
                    CreatedBy = Normalize(actor, ActorMaxLength, nameof(actor)),
                    CreatedAt = nowUtc
                });
            }

            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                Category = OrderHistoryCategory.Inventory,
                FromStatus = "ReservedOrCommitted",
                ToStatus = StockReservationStatus.Released.ToString(),
                Code = "INVENTORY_RELEASED",
                Title = "Đã hoàn tồn kho",
                Description = normalizedReason,
                ChangedBy = Normalize(actor, ActorMaxLength, nameof(actor)),
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = NormalizeOptional(idempotencyKey, CorrelationIdMaxLength)
            });

            await _context.SaveChangesAsync(cancellationToken);

            if (localTransaction is not null)
            {
                await localTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (localTransaction is not null)
            {
                await localTransaction.RollbackAsync(cancellationToken);
            }

            throw new InventoryConflictException(
                "Tồn kho vừa thay đổi bởi giao dịch khác. Không thể hoàn kho an toàn.",
                exception);
        }
        catch
        {
            if (localTransaction is not null)
            {
                await localTransaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (localTransaction is not null)
            {
                await localTransaction.DisposeAsync();
            }
        }
    }

    private static IReadOnlyList<InventoryReservationLine> ValidateReservationRequest(
        int orderId,
        IReadOnlyCollection<InventoryReservationLine> lines,
        DateTime expiresAtUtc,
        string idempotencyKey,
        string actor)
    {
        ValidateOperationInput(orderId, idempotencyKey, actor);

        if (lines.Count == 0)
        {
            throw new InventoryValidationException("Danh sách giữ kho không được để trống.");
        }

        if (expiresAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InventoryValidationException("Thời hạn giữ kho phải dùng UTC.");
        }

        if (lines.Any(item =>
                item.OrderItemId <= 0
                || item.ProductVariantId <= 0
                || item.Quantity <= 0))
        {
            throw new InventoryValidationException(
                "Dữ liệu giữ kho chứa mã dòng đơn hàng, biến thể hoặc số lượng không hợp lệ.");
        }

        var normalized = lines
            .GroupBy(item => item.OrderItemId)
            .Select(group =>
            {
                if (group.Select(item => item.ProductVariantId).Distinct().Count() != 1)
                {
                    throw new InventoryValidationException(
                        $"Dòng đơn hàng {group.Key} được gán nhiều biến thể khác nhau.");
                }

                return new InventoryReservationLine(
                    group.Key,
                    group.First().ProductVariantId,
                    group.Sum(item => item.Quantity));
            })
            .OrderBy(item => item.ProductVariantId)
            .ThenBy(item => item.OrderItemId)
            .ToArray();

        return normalized;
    }

    private static void ValidateExistingReservations(
        int orderId,
        IReadOnlyList<InventoryReservationLine> requested,
        IReadOnlyDictionary<int, string> reservationKeys,
        IReadOnlyCollection<StockReservation> existing)
    {
        if (existing.Count != requested.Count)
        {
            throw new InventoryConflictException(
                "Idempotency key đã được dùng cho một payload giữ kho khác.");
        }

        var existingByKey = existing.ToDictionary(item => item.IdempotencyKey);
        foreach (var line in requested)
        {
            var key = reservationKeys[line.OrderItemId];
            if (!existingByKey.TryGetValue(key, out var reservation)
                || reservation.OrderId != orderId
                || reservation.OrderItemId != line.OrderItemId
                || reservation.ProductVariantId != line.ProductVariantId
                || reservation.Quantity != line.Quantity)
            {
                throw new InventoryConflictException(
                    "Idempotency key đã được dùng cho một payload giữ kho khác.");
            }
        }
    }

    private static void ValidateOperationInput(int orderId, string idempotencyKey, string actor)
    {
        if (orderId <= 0)
        {
            throw new InventoryValidationException("Mã đơn hàng không hợp lệ.");
        }

        Normalize(idempotencyKey, IdempotencyKeyMaxLength - 32, nameof(idempotencyKey));
        Normalize(actor, ActorMaxLength, nameof(actor));
    }

    private static string BuildKey(string root, string operation, long referenceId)
    {
        var value = $"{root.Trim()}:{operation}:{referenceId}";
        if (value.Length > IdempotencyKeyMaxLength)
        {
            throw new InventoryValidationException(
                $"Idempotency key sau khi mở rộng vượt {IdempotencyKeyMaxLength} ký tự.");
        }

        return value;
    }

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InventoryValidationException($"{parameterName} không được để trống.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new InventoryValidationException(
                $"{parameterName} không được vượt {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
