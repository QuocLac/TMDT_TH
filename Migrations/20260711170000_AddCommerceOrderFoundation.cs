using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApplication2.Models;

#nullable disable

namespace WebApplication2.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711170000_AddCommerceOrderFoundation")]
public partial class AddCommerceOrderFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IntegrationInboxEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ExternalEventId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                DeduplicationKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Received"),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ProviderOccurredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationInboxEvents", x => x.Id);
                table.CheckConstraint("CK_IntegrationInboxEvent_AttemptCount", "[AttemptCount] >= 0");
                table.CheckConstraint("CK_IntegrationInboxEvent_Status", "[Status] IN ('Received','Processing','Processed','Ignored','Failed')");
            });

        migrationBuilder.CreateTable(
            name: "IntegrationOutboxMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                MessageType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                AggregateType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                AggregateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LockedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationOutboxMessages", x => x.Id);
                table.CheckConstraint("CK_IntegrationOutboxMessage_AttemptCount", "[AttemptCount] >= 0");
                table.CheckConstraint("CK_IntegrationOutboxMessage_Status", "[Status] IN ('Pending','Processing','Completed','Failed')");
            });

        migrationBuilder.CreateTable(
            name: "InventoryMovements",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProductVariantId = table.Column<int>(type: "int", nullable: false),
                MovementType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                QuantityDelta = table.Column<int>(type: "int", nullable: false),
                QuantityBefore = table.Column<int>(type: "int", nullable: false),
                QuantityAfter = table.Column<int>(type: "int", nullable: false),
                ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ReferenceId = table.Column<long>(type: "bigint", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                table.CheckConstraint("CK_InventoryMovement_MovementType", "[MovementType] IN ('ReservationCreated','ReservationReleased','ReservationExpired','ManualIncrease','ManualDecrease','ReturnRestocked','ReturnWriteOff')");
                table.CheckConstraint("CK_InventoryMovement_QuantityAfter", "[QuantityAfter] >= 0");
                table.CheckConstraint("CK_InventoryMovement_QuantityBefore", "[QuantityBefore] >= 0");
                table.CheckConstraint("CK_InventoryMovement_QuantityDelta", "[QuantityDelta] <> 0");
                table.CheckConstraint("CK_InventoryMovement_QuantityEquation", "[QuantityAfter] = [QuantityBefore] + [QuantityDelta]");
                table.ForeignKey(
                    name: "FK_InventoryMovements_ProductVariants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "ProductVariants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Orders",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                PublicToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                ClientRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CustomerId = table.Column<int>(type: "int", nullable: true),
                CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CustomerEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ShippingAddressLine = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                ShippingWard = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ShippingDistrict = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ShippingCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ShippingProvinceId = table.Column<int>(type: "int", nullable: true),
                ShippingDistrictId = table.Column<int>(type: "int", nullable: true),
                ShippingWardCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ShippingFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                TaxTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                OrderStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "PendingPayment"),
                PaymentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                FulfillmentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Unfulfilled"),
                PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
                table.CheckConstraint("CK_Order_DiscountTotal", "[DiscountTotal] >= 0");
                table.CheckConstraint("CK_Order_FulfillmentStatus", "[FulfillmentStatus] IN ('Unfulfilled','Preparing','ReadyToShip','Shipped','Delivered','DeliveryFailed','Returning','Returned','Cancelled')");
                table.CheckConstraint("CK_Order_GrandTotal", "[GrandTotal] >= 0");
                table.CheckConstraint("CK_Order_OrderStatus", "[OrderStatus] IN ('PendingPayment','Placed','Confirmed','Processing','Completed','Cancelled','Closed')");
                table.CheckConstraint("CK_Order_PaymentStatus", "[PaymentStatus] IN ('Pending','CodPending','Paid','Failed','Cancelled','PartiallyRefunded','Refunded')");
                table.CheckConstraint("CK_Order_ShippingFee", "[ShippingFee] >= 0");
                table.CheckConstraint("CK_Order_Subtotal", "[Subtotal] >= 0");
                table.CheckConstraint("CK_Order_TaxTotal", "[TaxTotal] >= 0");
                table.ForeignKey(
                    name: "FK_Orders_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "OrderItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                ProductVariantId = table.Column<int>(type: "int", nullable: false),
                ProductName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                VariantDescription = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ListPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderItems", x => x.Id);
                table.CheckConstraint("CK_OrderItem_DiscountAmount", "[DiscountAmount] >= 0");
                table.CheckConstraint("CK_OrderItem_LineTotal", "[LineTotal] >= 0");
                table.CheckConstraint("CK_OrderItem_ListPrice", "[ListPrice] >= 0");
                table.CheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_OrderItem_TaxAmount", "[TaxAmount] >= 0");
                table.CheckConstraint("CK_OrderItem_UnitPrice", "[UnitPrice] >= 0");
                table.ForeignKey(
                    name: "FK_OrderItems_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_OrderItems_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OrderItems_ProductVariants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "ProductVariants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "OrderStatusHistories",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CustomerVisible = table.Column<bool>(type: "bit", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderStatusHistories", x => x.Id);
                table.CheckConstraint("CK_OrderStatusHistory_Category", "[Category] IN ('Order','Payment','Fulfillment','Inventory','Integration')");
                table.ForeignKey(
                    name: "FK_OrderStatusHistories_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PaymentTransactions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                AttemptNumber = table.Column<int>(type: "int", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                MerchantReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProviderTransactionId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                table.CheckConstraint("CK_PaymentTransaction_Amount", "[Amount] >= 0");
                table.CheckConstraint("CK_PaymentTransaction_AttemptNumber", "[AttemptNumber] > 0");
                table.CheckConstraint("CK_PaymentTransaction_Status", "[Status] IN ('Pending','CodPending','Paid','Failed','Cancelled','PartiallyRefunded','Refunded')");
                table.ForeignKey(
                    name: "FK_PaymentTransactions_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Shipments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                ServiceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                ServiceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ExternalOrderCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                TrackingCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CodAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                WeightGram = table.Column<int>(type: "int", nullable: false),
                LengthCm = table.Column<int>(type: "int", nullable: false),
                WidthCm = table.Column<int>(type: "int", nullable: false),
                HeightCm = table.Column<int>(type: "int", nullable: false),
                EstimatedDeliveryAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ShipperName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ShipperPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                CurrentHub = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                ProviderReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shipments", x => x.Id);
                table.CheckConstraint("CK_Shipment_CodAmount", "[CodAmount] >= 0");
                table.CheckConstraint("CK_Shipment_Fee", "[Fee] >= 0");
                table.CheckConstraint("CK_Shipment_HeightCm", "[HeightCm] >= 0");
                table.CheckConstraint("CK_Shipment_LengthCm", "[LengthCm] >= 0");
                table.CheckConstraint("CK_Shipment_Status", "[Status] IN ('Draft','PendingCreation','Created','Picking','InTransit','Delivered','DeliveryFailed','Returning','Returned','Cancelled','Exception')");
                table.CheckConstraint("CK_Shipment_WeightGram", "[WeightGram] >= 0");
                table.CheckConstraint("CK_Shipment_WidthCm", "[WidthCm] >= 0");
                table.ForeignKey(
                    name: "FK_Shipments_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "StockReservations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                OrderItemId = table.Column<int>(type: "int", nullable: false),
                ProductVariantId = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Reserved"),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ReservedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockReservations", x => x.Id);
                table.CheckConstraint("CK_StockReservation_Expiry", "[ExpiresAt] > [ReservedAt]");
                table.CheckConstraint("CK_StockReservation_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_StockReservation_Status", "[Status] IN ('Reserved','Committed','Released','Expired')");
                table.ForeignKey(
                    name: "FK_StockReservations_OrderItems_OrderItemId",
                    column: x => x.OrderItemId,
                    principalTable: "OrderItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockReservations_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_StockReservations_ProductVariants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "ProductVariants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxEvents_Provider_DeduplicationKey",
            table: "IntegrationInboxEvents",
            columns: new[] { "Provider", "DeduplicationKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxEvents_Provider_ExternalEventId",
            table: "IntegrationInboxEvents",
            columns: new[] { "Provider", "ExternalEventId" },
            filter: "[ExternalEventId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxEvents_Status_ReceivedAt",
            table: "IntegrationInboxEvents",
            columns: new[] { "Status", "ReceivedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationOutboxMessages_AggregateType_AggregateId",
            table: "IntegrationOutboxMessages",
            columns: new[] { "AggregateType", "AggregateId" });

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationOutboxMessages_Provider_IdempotencyKey",
            table: "IntegrationOutboxMessages",
            columns: new[] { "Provider", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationOutboxMessages_Status_NextAttemptAt",
            table: "IntegrationOutboxMessages",
            columns: new[] { "Status", "NextAttemptAt" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryMovements_IdempotencyKey",
            table: "InventoryMovements",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryMovements_ProductVariantId_CreatedAt",
            table: "InventoryMovements",
            columns: new[] { "ProductVariantId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryMovements_ReferenceType_ReferenceId",
            table: "InventoryMovements",
            columns: new[] { "ReferenceType", "ReferenceId" });

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_OrderId",
            table: "OrderItems",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_OrderId_ProductVariantId",
            table: "OrderItems",
            columns: new[] { "OrderId", "ProductVariantId" });

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_ProductId",
            table: "OrderItems",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_ProductVariantId",
            table: "OrderItems",
            column: "ProductVariantId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_ClientRequestId",
            table: "Orders",
            column: "ClientRequestId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_Code",
            table: "Orders",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_CustomerId",
            table: "Orders",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_FulfillmentStatus_CreatedAt",
            table: "Orders",
            columns: new[] { "FulfillmentStatus", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OrderStatus_CreatedAt",
            table: "Orders",
            columns: new[] { "OrderStatus", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Orders_PaymentStatus_CreatedAt",
            table: "Orders",
            columns: new[] { "PaymentStatus", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Orders_PublicToken",
            table: "Orders",
            column: "PublicToken",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderStatusHistories_CorrelationId",
            table: "OrderStatusHistories",
            column: "CorrelationId",
            filter: "[CorrelationId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_OrderStatusHistories_OrderId_OccurredAt",
            table: "OrderStatusHistories",
            columns: new[] { "OrderId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentTransactions_IdempotencyKey",
            table: "PaymentTransactions",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PaymentTransactions_OrderId",
            table: "PaymentTransactions",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentTransactions_Provider_MerchantReference",
            table: "PaymentTransactions",
            columns: new[] { "Provider", "MerchantReference" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentTransactions_Provider_ProviderTransactionId",
            table: "PaymentTransactions",
            columns: new[] { "Provider", "ProviderTransactionId" },
            unique: true,
            filter: "[ProviderTransactionId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentTransactions_Status_CreatedAt",
            table: "PaymentTransactions",
            columns: new[] { "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_OrderId",
            table: "Shipments",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_Provider_ExternalOrderCode",
            table: "Shipments",
            columns: new[] { "Provider", "ExternalOrderCode" },
            unique: true,
            filter: "[ExternalOrderCode] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_Status_CreatedAt",
            table: "Shipments",
            columns: new[] { "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_Provider_TrackingCode",
            table: "Shipments",
            columns: new[] { "Provider", "TrackingCode" },
            unique: true,
            filter: "[TrackingCode] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_IdempotencyKey",
            table: "StockReservations",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_OrderId",
            table: "StockReservations",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_OrderItemId",
            table: "StockReservations",
            column: "OrderItemId",
            unique: true,
            filter: "[Status] IN ('Reserved','Committed')");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_ProductVariantId",
            table: "StockReservations",
            column: "ProductVariantId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_Status_ExpiresAt",
            table: "StockReservations",
            columns: new[] { "Status", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IntegrationInboxEvents");
        migrationBuilder.DropTable(name: "IntegrationOutboxMessages");
        migrationBuilder.DropTable(name: "InventoryMovements");
        migrationBuilder.DropTable(name: "OrderStatusHistories");
        migrationBuilder.DropTable(name: "PaymentTransactions");
        migrationBuilder.DropTable(name: "Shipments");
        migrationBuilder.DropTable(name: "StockReservations");
        migrationBuilder.DropTable(name: "OrderItems");
        migrationBuilder.DropTable(name: "Orders");
    }
}
