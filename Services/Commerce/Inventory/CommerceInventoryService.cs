using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Inventory;

public sealed record CancellationInventoryLine(
    long CancellationItemId,
    int OrderItemId,
    int ProductVariantId,
    int Quantity);

public interface ICancellationInventoryService
{
    Task CompensateCancellationAsync(
        int orderId,
        IReadOnlyCollection<CancellationInventoryLine> lines,
        string reason,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken);
}

/// <summary>
/// Public inventory facade. Existing reservation operations delegate to the
/// proven InventoryService; cancellation compensation is kept in the same
/// scoped facade so controllers and order workflows never mutate stock directly.
/// </summary>
public sealed class CommerceInventoryService : IInventoryService, ICancellationInventoryService
{
    private const string CancellationReferenceType = "CancellationItem";
    private const int IdempotencyKeyMaxLength = 128;
    private const int ActorMaxLength = 100;
    private const int ReasonMaxLength = 500;

    private readonly InventoryService _inner;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommerceInventoryService> _logger;

    public CommerceInventoryService(
        InventoryService inner,
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<CommerceInventoryService> logger)
    {
        _inner = inner;
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<IReadOnlyList<StockReservation>> ReserveAsync(
        int orderId,
        IReadOnlyCollection<InventoryReservationLine> lines,
        DateTime expiresAtUtc,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        return _inner.ReserveAsync(
            orderId,
            lines,
            expiresAtUtc,
            idempotencyKey,
            actor,
            cancellationToken);
    }

    public Task CommitAsync(
        int orderId,
        string correlationId,
        string actor,
        CancellationToken cancellationToken)
    {
        return _inner.CommitAsync(orderId, correlationId, actor, cancellationToken);
    }

    public Task ReleaseAsync(
        int orderId,
        string reason,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        return _inner.ReleaseAsync(orderId, reason, idempotencyKey, actor, cancellationToken);
    }

    public async Task CompensateCancellationAsync(
        int orderId,
        IReadOnlyCollection<CancellationInventoryLine> lines,
        string reason,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        var normalizedLines = NormalizeLines(orderId, lines);
        var normalizedReason = Normalize(reason, ReasonMaxLength, nameof(reason));
        var normalizedActor = Normalize(actor, ActorMaxLength, nameof(actor));
        var normalizedKey = Normalize(
            idempotencyKey,
            IdempotencyKeyMaxLength - 32,
            nameof(idempotencyKey));

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

            var cancellationItemIds = normalizedLines
                .Select(item => item.CancellationItemId)
                .ToArray();

            var cancellationRows = await _context.Set<OrderCancellationItem>()
                .AsNoTracking()
                .Where(item => cancellationItemIds.Contains(item.Id))
                .Select(item => new
                {
                    item.Id,
                    item.OrderItemId,
                    item.RequestedQuantity,
                    OrderId = item.CancellationRequest.OrderId,
                    item.OrderItem.ProductVariantId
                })
                .ToDictionaryAsync(item => item.Id, cancellationToken);

            if (cancellationRows.Count != normalizedLines.Count)
            {
                throw new InventoryValidationException(
                    "Danh sách hoàn kho chứa cancellation item không tồn tại.");
            }

            foreach (var line in normalizedLines)
            {
                var row = cancellationRows[line.CancellationItemId];
                if (row.OrderId != orderId
                    || row.OrderItemId != line.OrderItemId
                    || row.ProductVariantId != line.ProductVariantId
                    || line.Quantity > row.RequestedQuantity)
                {
                    throw new InventoryValidationException(
                        "Dữ liệu hoàn kho không khớp yêu cầu hủy đơn.");
                }
            }

            var movementKeys = normalizedLines.ToDictionary(
                item => item.CancellationItemId,
                item => BuildMovementKey(normalizedKey, item.CancellationItemId));

            var existingMovements = await _context.InventoryMovements
                .AsNoTracking()
                .Where(item => movementKeys.Values.Contains(item.IdempotencyKey))
                .Select(item => new
                {
                    item.IdempotencyKey,
                    item.ReferenceId,
                    item.ProductVariantId,
                    item.QuantityDelta
                })
                .ToDictionaryAsync(item => item.IdempotencyKey, cancellationToken);

            foreach (var line in normalizedLines)
            {
                var key = movementKeys[line.CancellationItemId];
                if (existingMovements.TryGetValue(key, out var existing)
                    && (existing.ReferenceId != line.CancellationItemId
                        || existing.ProductVariantId != line.ProductVariantId
                        || existing.QuantityDelta != line.Quantity))
                {
                    throw new InventoryConflictException(
                        "Idempotency key hoàn kho đã được dùng cho payload khác.");
                }
            }

            var newLines = normalizedLines
                .Where(item => !existingMovements.ContainsKey(
                    movementKeys[item.CancellationItemId]))
                .ToArray();

            if (newLines.Length == 0)
            {
                if (localTransaction is not null)
                {
                    await localTransaction.CommitAsync(cancellationToken);
                }

                return;
            }

            var orderItemIds = normalizedLines
                .Select(item => item.OrderItemId)
                .Distinct()
                .ToArray();

            var allCancellationItems = await _context.Set<OrderCancellationItem>()
                .AsNoTracking()
                .Where(item =>
                    item.CancellationRequest.OrderId == orderId
                    && orderItemIds.Contains(item.OrderItemId))
                .Select(item => new { item.Id, item.OrderItemId })
                .ToArrayAsync(cancellationToken);

            var orderItemByCancellationItem = allCancellationItems
                .ToDictionary(item => item.Id, item => item.OrderItemId);
            var allCancellationItemIds = orderItemByCancellationItem.Keys.ToArray();

            var priorMovementRows = await _context.InventoryMovements
                .AsNoTracking()
                .Where(item =>
                    item.ReferenceType == CancellationReferenceType
                    && allCancellationItemIds.Contains(item.ReferenceId))
                .Select(item => new
                {
                    item.ReferenceId,
                    item.QuantityDelta
                })
                .ToArrayAsync(cancellationToken);

            var compensatedByOrderItem = priorMovementRows
                .Where(item => item.QuantityDelta > 0)
                .GroupBy(item => orderItemByCancellationItem[item.ReferenceId])
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.QuantityDelta));

            var reservations = await _context.StockReservations
                .Where(item =>
                    item.OrderId == orderId
                    && orderItemIds.Contains(item.OrderItemId))
                .OrderBy(item => item.OrderItemId)
                .ToDictionaryAsync(item => item.OrderItemId, cancellationToken);

            if (reservations.Count != orderItemIds.Length)
            {
                throw new InventoryValidationException(
                    "Không thể hoàn kho vì thiếu bản ghi reservation của đơn hàng.");
            }

            var variantIds = newLines
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

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var line in newLines)
            {
                var reservation = reservations[line.OrderItemId];
                var compensated = compensatedByOrderItem.GetValueOrDefault(line.OrderItemId);

                if ((reservation.Status is
                        StockReservationStatus.Released
                        or StockReservationStatus.Expired)
                    && compensated < reservation.Quantity)
                {
                    throw new InventoryConflictException(
                        $"Reservation của order item {line.OrderItemId} đã được giải phóng bởi luồng khác.");
                }

                var availableToCompensate = reservation.Quantity - compensated;

                if (line.Quantity <= 0 || line.Quantity > availableToCompensate)
                {
                    throw new InventoryConflictException(
                        $"Số lượng hoàn kho của order item {line.OrderItemId} vượt phần đã trừ kho còn lại.");
                }

                var variant = variants[line.ProductVariantId];
                var quantityBefore = variant.StockQuantity;
                variant.StockQuantity += line.Quantity;
                variant.UpdatedAt = nowUtc;

                _context.InventoryMovements.Add(new InventoryMovement
                {
                    ProductVariantId = line.ProductVariantId,
                    MovementType = InventoryMovementType.ReservationReleased,
                    QuantityDelta = line.Quantity,
                    QuantityBefore = quantityBefore,
                    QuantityAfter = variant.StockQuantity,
                    ReferenceType = CancellationReferenceType,
                    ReferenceId = line.CancellationItemId,
                    IdempotencyKey = movementKeys[line.CancellationItemId],
                    Reason = normalizedReason,
                    CreatedBy = normalizedActor,
                    CreatedAt = nowUtc
                });

                compensatedByOrderItem[line.OrderItemId] = compensated + line.Quantity;
            }

            foreach (var reservation in reservations.Values)
            {
                var compensated = compensatedByOrderItem.GetValueOrDefault(
                    reservation.OrderItemId);
                if (compensated >= reservation.Quantity)
                {
                    reservation.Status = StockReservationStatus.Released;
                    reservation.ReleasedAt ??= nowUtc;
                    reservation.ReleaseReason = normalizedReason;
                    reservation.UpdatedAt = nowUtc;
                }
            }

            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                Category = OrderHistoryCategory.Inventory,
                FromStatus = "Committed",
                ToStatus = "CancellationCompensated",
                Code = "INVENTORY_CANCELLATION_COMPENSATED",
                Title = "Đã hoàn kho theo yêu cầu hủy",
                Description = $"Đã hoàn {newLines.Sum(item => item.Quantity)} sản phẩm. {normalizedReason}",
                ChangedBy = normalizedActor,
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = ToCorrelationId(normalizedKey)
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

            _logger.LogWarning(
                exception,
                "Cancellation inventory compensation for order {OrderId} lost a concurrency race.",
                orderId);

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

    private static IReadOnlyList<CancellationInventoryLine> NormalizeLines(
        int orderId,
        IReadOnlyCollection<CancellationInventoryLine> lines)
    {
        if (orderId <= 0)
        {
            throw new InventoryValidationException("Mã đơn hàng không hợp lệ.");
        }

        if (lines.Count == 0)
        {
            throw new InventoryValidationException("Danh sách hoàn kho không được để trống.");
        }

        if (lines.Any(item =>
                item.CancellationItemId <= 0
                || item.OrderItemId <= 0
                || item.ProductVariantId <= 0
                || item.Quantity <= 0))
        {
            throw new InventoryValidationException(
                "Danh sách hoàn kho chứa mã hoặc số lượng không hợp lệ.");
        }

        var duplicate = lines
            .GroupBy(item => item.CancellationItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InventoryValidationException(
                $"Cancellation item {duplicate.Key} xuất hiện nhiều lần.");
        }

        return lines
            .OrderBy(item => item.ProductVariantId)
            .ThenBy(item => item.OrderItemId)
            .ToArray();
    }

    private static string BuildMovementKey(string root, long cancellationItemId)
    {
        var key = $"{root}:cancel-release:{cancellationItemId}";
        if (key.Length > IdempotencyKeyMaxLength)
        {
            throw new InventoryValidationException(
                $"Idempotency key sau khi mở rộng vượt {IdempotencyKeyMaxLength} ký tự.");
        }

        return key;
    }

    private static string ToCorrelationId(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 64
            ? normalized
            : normalized[..64];
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
}
