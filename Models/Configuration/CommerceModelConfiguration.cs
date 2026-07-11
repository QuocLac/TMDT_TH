using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models.Configuration;

public static class CommerceModelConfiguration
{
    public static void ConfigureCommerce(this ModelBuilder modelBuilder)
    {
        ConfigureOrders(modelBuilder);
        ConfigureOrderItems(modelBuilder);
        ConfigurePayments(modelBuilder);
        ConfigureShipments(modelBuilder);
        ConfigureInventory(modelBuilder);
        ConfigureTimeline(modelBuilder);
        ConfigureIntegrationInbox(modelBuilder);
        ConfigureIntegrationOutbox(modelBuilder);
    }

    private static void ConfigureOrders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(item => item.PublicToken)
                .HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(item => item.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("VND");
            entity.Property(item => item.OrderStatus)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(OrderStatus.PendingPayment);
            entity.Property(item => item.PaymentStatus)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PaymentStatus.Pending);
            entity.Property(item => item.FulfillmentStatus)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(FulfillmentStatus.Unfulfilled);
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => item.Code).IsUnique();
            entity.HasIndex(item => item.PublicToken).IsUnique();
            entity.HasIndex(item => item.ClientRequestId).IsUnique();
            entity.HasIndex(item => new { item.OrderStatus, item.CreatedAt });
            entity.HasIndex(item => new { item.PaymentStatus, item.CreatedAt });
            entity.HasIndex(item => new { item.FulfillmentStatus, item.CreatedAt });
            entity.HasIndex(item => item.CustomerId);

            entity.HasOne(item => item.Customer)
                .WithMany()
                .HasForeignKey(item => item.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Order_Subtotal", "[Subtotal] >= 0");
                table.HasCheckConstraint("CK_Order_ShippingFee", "[ShippingFee] >= 0");
                table.HasCheckConstraint("CK_Order_DiscountTotal", "[DiscountTotal] >= 0");
                table.HasCheckConstraint("CK_Order_TaxTotal", "[TaxTotal] >= 0");
                table.HasCheckConstraint("CK_Order_GrandTotal", "[GrandTotal] >= 0");
                table.HasCheckConstraint(
                    "CK_Order_OrderStatus",
                    "[OrderStatus] IN ('PendingPayment','Placed','Confirmed','Processing','Completed','Cancelled','Closed')");
                table.HasCheckConstraint(
                    "CK_Order_PaymentStatus",
                    "[PaymentStatus] IN ('Pending','CodPending','Paid','Failed','Cancelled','PartiallyRefunded','Refunded')");
                table.HasCheckConstraint(
                    "CK_Order_FulfillmentStatus",
                    "[FulfillmentStatus] IN ('Unfulfilled','Preparing','ReadyToShip','Shipped','Delivered','DeliveryFailed','Returning','Returned','Cancelled')");
            });
        });
    }

    private static void ConfigureOrderItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(item => item.OrderId);
            entity.HasIndex(item => item.ProductId);
            entity.HasIndex(item => item.ProductVariantId);
            entity.HasIndex(item => new { item.OrderId, item.ProductVariantId });

            entity.HasOne(item => item.Order)
                .WithMany(item => item.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ProductVariant)
                .WithMany()
                .HasForeignKey(item => item.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0");
                table.HasCheckConstraint("CK_OrderItem_ListPrice", "[ListPrice] >= 0");
                table.HasCheckConstraint("CK_OrderItem_UnitPrice", "[UnitPrice] >= 0");
                table.HasCheckConstraint("CK_OrderItem_DiscountAmount", "[DiscountAmount] >= 0");
                table.HasCheckConstraint("CK_OrderItem_TaxAmount", "[TaxAmount] >= 0");
                table.HasCheckConstraint("CK_OrderItem_LineTotal", "[LineTotal] >= 0");
            });
        });
    }

    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PaymentStatus.Pending);
            entity.Property(item => item.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("VND");
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.HasIndex(item => new { item.Provider, item.ProviderTransactionId })
                .IsUnique()
                .HasFilter("[ProviderTransactionId] IS NOT NULL");
            entity.HasIndex(item => new { item.Provider, item.MerchantReference });
            entity.HasIndex(item => new { item.Status, item.CreatedAt });
            entity.HasIndex(item => item.OrderId);

            entity.HasOne(item => item.Order)
                .WithMany(item => item.PaymentTransactions)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_PaymentTransaction_Amount", "[Amount] >= 0");
                table.HasCheckConstraint("CK_PaymentTransaction_AttemptNumber", "[AttemptNumber] > 0");
                table.HasCheckConstraint(
                    "CK_PaymentTransaction_Status",
                    "[Status] IN ('Pending','CodPending','Paid','Failed','Cancelled','PartiallyRefunded','Refunded')");
            });
        });
    }

    private static void ConfigureShipments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.Property(item => item.Direction)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ShipmentDirection.Outbound);
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(ShipmentStatus.Draft);
            entity.Property(item => item.ProviderStatus)
                .HasMaxLength(100);
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => new { item.Provider, item.ExternalOrderCode })
                .IsUnique()
                .HasFilter("[ExternalOrderCode] IS NOT NULL");
            entity.HasIndex(item => new { item.Provider, item.TrackingCode })
                .IsUnique()
                .HasFilter("[TrackingCode] IS NOT NULL");
            entity.HasIndex(item => new { item.Direction, item.Status, item.CreatedAt });
            entity.HasIndex(item => item.ParentShipmentId);
            entity.HasIndex(item => new { item.OrderId, item.Direction })
                .IsUnique()
                .HasFilter("[Direction] = 'Outbound' AND [Status] <> 'Cancelled' AND [Status] <> 'Returned'");

            entity.HasOne(item => item.Order)
                .WithMany(item => item.Shipments)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ParentShipment)
                .WithMany(item => item.ChildShipments)
                .HasForeignKey(item => item.ParentShipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Shipment_Fee", "[Fee] >= 0");
                table.HasCheckConstraint("CK_Shipment_CodAmount", "[CodAmount] >= 0");
                table.HasCheckConstraint("CK_Shipment_WeightGram", "[WeightGram] >= 0");
                table.HasCheckConstraint("CK_Shipment_LengthCm", "[LengthCm] >= 0");
                table.HasCheckConstraint("CK_Shipment_WidthCm", "[WidthCm] >= 0");
                table.HasCheckConstraint("CK_Shipment_HeightCm", "[HeightCm] >= 0");
                table.HasCheckConstraint(
                    "CK_Shipment_Direction",
                    "[Direction] IN ('Outbound','Return')");
                table.HasCheckConstraint(
                    "CK_Shipment_ParentDirection",
                    "([Direction] = 'Outbound' AND [ParentShipmentId] IS NULL) OR " +
                    "([Direction] = 'Return' AND [ParentShipmentId] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Shipment_ReturnCodAmount",
                    "[Direction] = 'Outbound' OR [CodAmount] = 0");
                table.HasCheckConstraint(
                    "CK_Shipment_Status",
                    "[Status] IN ('Draft','PendingCreation','Created','CancelRequested','Picking','InTransit','Delivered','DeliveryFailed','Returning','Returned','Cancelled','Exception')");
                table.HasCheckConstraint(
                    "CK_Shipment_DeliveredAt",
                    "[DeliveredAt] IS NULL OR [Status] IN ('Delivered','Returning','Returned')");
                table.HasCheckConstraint(
                    "CK_Shipment_CancelledAt",
                    "[CancelledAt] IS NULL OR [Status] = 'Cancelled'");
            });
        });
    }

    private static void ConfigureInventory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(StockReservationStatus.Reserved);
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.HasIndex(item => new { item.Status, item.ExpiresAt });
            entity.HasIndex(item => item.OrderId);
            entity.HasIndex(item => item.OrderItemId)
                .IsUnique()
                .HasFilter("[Status] IN ('Reserved','Committed')");
            entity.HasIndex(item => item.ProductVariantId);

            entity.HasOne(item => item.Order)
                .WithMany(item => item.StockReservations)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.OrderItem)
                .WithMany(item => item.StockReservations)
                .HasForeignKey(item => item.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ProductVariant)
                .WithMany()
                .HasForeignKey(item => item.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_StockReservation_Quantity", "[Quantity] > 0");
                table.HasCheckConstraint(
                    "CK_StockReservation_Status",
                    "[Status] IN ('Reserved','Committed','Released','Expired')");
                table.HasCheckConstraint(
                    "CK_StockReservation_Expiry",
                    "[ExpiresAt] > [ReservedAt]");
            });
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.Property(item => item.MovementType)
                .HasConversion<string>()
                .HasMaxLength(40);
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.HasIndex(item => new { item.ProductVariantId, item.CreatedAt });
            entity.HasIndex(item => new { item.ReferenceType, item.ReferenceId });

            entity.HasOne(item => item.ProductVariant)
                .WithMany()
                .HasForeignKey(item => item.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_InventoryMovement_QuantityDelta", "[QuantityDelta] <> 0");
                table.HasCheckConstraint("CK_InventoryMovement_QuantityBefore", "[QuantityBefore] >= 0");
                table.HasCheckConstraint("CK_InventoryMovement_QuantityAfter", "[QuantityAfter] >= 0");
                table.HasCheckConstraint(
                    "CK_InventoryMovement_QuantityEquation",
                    "[QuantityAfter] = [QuantityBefore] + [QuantityDelta]");
                table.HasCheckConstraint(
                    "CK_InventoryMovement_MovementType",
                    "[MovementType] IN ('ReservationCreated','ReservationReleased','ReservationExpired','ManualIncrease','ManualDecrease','ReturnRestocked','ReturnWriteOff')");
            });
        });
    }

    private static void ConfigureTimeline(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.Property(item => item.Category)
                .HasConversion<string>()
                .HasMaxLength(30);
            entity.Property(item => item.OccurredAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(item => new { item.OrderId, item.OccurredAt });
            entity.HasIndex(item => item.CorrelationId)
                .HasFilter("[CorrelationId] IS NOT NULL");

            entity.HasOne(item => item.Order)
                .WithMany(item => item.StatusHistory)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_OrderStatusHistory_Category",
                    "[Category] IN ('Order','Payment','Fulfillment','Inventory','Integration','Return')");
            });
        });
    }

    private static void ConfigureIntegrationInbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationInboxEvent>(entity =>
        {
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(IntegrationEventStatus.Received);
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.ReceivedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => new { item.Provider, item.DeduplicationKey }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.ReceivedAt });
            entity.HasIndex(item => new { item.Provider, item.ExternalEventId })
                .HasFilter("[ExternalEventId] IS NOT NULL");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_IntegrationInboxEvent_AttemptCount", "[AttemptCount] >= 0");
                table.HasCheckConstraint(
                    "CK_IntegrationInboxEvent_Status",
                    "[Status] IN ('Received','Processing','Processed','Ignored','Failed')");
            });
        });
    }

    private static void ConfigureIntegrationOutbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationOutboxMessage>(entity =>
        {
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(IntegrationOutboxStatus.Pending);
            entity.Property(item => item.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => new { item.Provider, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.NextAttemptAt });
            entity.HasIndex(item => new { item.AggregateType, item.AggregateId });

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_IntegrationOutboxMessage_AttemptCount", "[AttemptCount] >= 0");
                table.HasCheckConstraint(
                    "CK_IntegrationOutboxMessage_Status",
                    "[Status] IN ('Pending','Processing','Completed','Failed')");
            });
        });
    }
}
