using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApplication2.Models;

#nullable disable

namespace WebApplication2.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711230000_AddShipmentLifecycleMetadata")]
public partial class AddShipmentLifecycleMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_Status",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_OrderStatusHistory_Category",
            table: "OrderStatusHistories");

        migrationBuilder.AddColumn<DateTime>(
            name: "CancelledAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CancelRequestedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CarrierHandoffAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveredAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Direction",
            table: "Shipments",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Outbound");

        migrationBuilder.AddColumn<DateTime>(
            name: "LastSyncedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "ParentShipmentId",
            table: "Shipments",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProviderStatus",
            table: "Shipments",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProviderUpdatedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.Sql(
            """
            ;WITH LegacyProviderCancellation AS
            (
                SELECT DISTINCT s.Id, s.OrderId
                FROM Shipments AS s
                INNER JOIN IntegrationOutboxMessages AS o
                    ON o.AggregateType = 'Shipment'
                    AND TRY_CONVERT(bigint, o.AggregateId) = s.Id
                WHERE o.Provider = 'GHN'
                    AND o.MessageType = 'CancelShipmentRequested'
                    AND o.Status IN ('Pending','Processing','Failed')
                    AND s.ExternalOrderCode IS NOT NULL
                    AND s.Status = 'Cancelled'
            )
            UPDATE replacement
            SET replacement.Status = 'Cancelled',
                replacement.CancelledAt = COALESCE(replacement.UpdatedAt, GETUTCDATE()),
                replacement.ProviderStatus = 'legacy_replacement_waiting_provider_cancellation'
            FROM Shipments AS replacement
            INNER JOIN LegacyProviderCancellation AS legacy
                ON legacy.OrderId = replacement.OrderId
                AND legacy.Id <> replacement.Id
            WHERE replacement.ExternalOrderCode IS NULL
                AND replacement.Status IN ('Draft','PendingCreation');

            UPDATE s
            SET s.Status = 'CancelRequested',
                s.CancelRequestedAt = COALESCE(s.UpdatedAt, GETUTCDATE()),
                s.CancelledAt = NULL,
                s.ProviderStatus = 'legacy_cancel_pending_provider_confirmation'
            FROM Shipments AS s
            INNER JOIN IntegrationOutboxMessages AS o
                ON o.AggregateType = 'Shipment'
                AND TRY_CONVERT(bigint, o.AggregateId) = s.Id
            WHERE o.Provider = 'GHN'
                AND o.MessageType = 'CancelShipmentRequested'
                AND o.Status IN ('Pending','Processing','Failed')
                AND s.ExternalOrderCode IS NOT NULL
                AND s.Status = 'Cancelled';
            """);

        migrationBuilder.Sql(
            """
            ;WITH RankedActiveOutbound AS
            (
                SELECT
                    s.Id,
                    s.OrderId,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY s.OrderId
                        ORDER BY
                            CASE
                                WHEN s.ExternalOrderCode IS NOT NULL
                                    OR s.TrackingCode IS NOT NULL THEN 0
                                WHEN EXISTS
                                (
                                    SELECT 1
                                    FROM IntegrationOutboxMessages AS queued
                                    WHERE queued.AggregateType = 'Shipment'
                                        AND TRY_CONVERT(bigint, queued.AggregateId) = s.Id
                                        AND queued.Provider = 'GHN'
                                        AND queued.MessageType IN
                                        (
                                            'ShipmentCreateRequested',
                                            'CreateShipmentRequested'
                                        )
                                        AND queued.Status IN ('Pending','Processing','Completed')
                                ) THEN 1
                                ELSE 2
                            END,
                            CASE s.Status
                                WHEN 'Delivered' THEN 0
                                WHEN 'InTransit' THEN 1
                                WHEN 'Picking' THEN 2
                                WHEN 'Created' THEN 3
                                WHEN 'CancelRequested' THEN 4
                                WHEN 'DeliveryFailed' THEN 5
                                WHEN 'Exception' THEN 6
                                WHEN 'Returning' THEN 7
                                WHEN 'PendingCreation' THEN 8
                                WHEN 'Draft' THEN 9
                                ELSE 10
                            END,
                            COALESCE(
                                s.ProviderUpdatedAt,
                                s.UpdatedAt,
                                s.CreatedAt) DESC,
                            s.Id DESC
                    ) AS DuplicateRank
                FROM Shipments AS s
                WHERE s.Direction = 'Outbound'
                    AND s.Status NOT IN ('Cancelled','Returned')
            ),
            SafeLocalDuplicates AS
            (
                SELECT ranked.Id
                FROM RankedActiveOutbound AS ranked
                INNER JOIN Shipments AS shipment ON shipment.Id = ranked.Id
                WHERE ranked.DuplicateRank > 1
                    AND shipment.ExternalOrderCode IS NULL
                    AND shipment.TrackingCode IS NULL
                    AND shipment.Status IN ('Draft','PendingCreation')
            )
            UPDATE shipment
            SET shipment.Status = 'Cancelled',
                shipment.CancelledAt = COALESCE(
                    shipment.UpdatedAt,
                    shipment.CreatedAt,
                    GETUTCDATE()),
                shipment.ProviderStatus =
                    'legacy_duplicate_local_outbound_closed_by_phase5_migration',
                shipment.UpdatedAt = GETUTCDATE()
            FROM Shipments AS shipment
            INNER JOIN SafeLocalDuplicates AS duplicate
                ON duplicate.Id = shipment.Id;

            IF EXISTS
            (
                SELECT 1
                FROM Shipments AS shipment
                WHERE shipment.Direction = 'Outbound'
                    AND shipment.Status NOT IN ('Cancelled','Returned')
                GROUP BY shipment.OrderId
                HAVING COUNT_BIG(*) > 1
            )
            BEGIN
                DECLARE @ConflictingOrders nvarchar(1500);
                DECLARE @ConflictMessage nvarchar(2048);

                SELECT @ConflictingOrders = STRING_AGG(
                    CONVERT(nvarchar(20), conflicts.OrderId),
                    ',')
                FROM
                (
                    SELECT TOP (50) shipment.OrderId
                    FROM Shipments AS shipment
                    WHERE shipment.Direction = 'Outbound'
                        AND shipment.Status NOT IN ('Cancelled','Returned')
                    GROUP BY shipment.OrderId
                    HAVING COUNT_BIG(*) > 1
                    ORDER BY shipment.OrderId
                ) AS conflicts;

                SET @ConflictMessage = CONCAT(
                    'SHIPMENT_ACTIVE_OUTBOUND_CONFLICT: Các OrderId sau vẫn có ',
                    'nhiều outbound shipment đang hoạt động: ',
                    COALESCE(@ConflictingOrders, 'unknown'),
                    '. Hệ thống chỉ tự đóng shipment local Draft/PendingCreation ',
                    'không có mã GHN. Hãy kiểm tra dữ liệu provider trước khi chạy lại migration.');

                THROW 51051, @ConflictMessage, 1;
            END;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_Direction",
            table: "Shipments",
            sql: "[Direction] IN ('Outbound','Return')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_ParentDirection",
            table: "Shipments",
            sql: "([Direction] = 'Outbound' AND [ParentShipmentId] IS NULL) OR ([Direction] = 'Return' AND [ParentShipmentId] IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_ReturnCodAmount",
            table: "Shipments",
            sql: "[Direction] = 'Outbound' OR [CodAmount] = 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_Status",
            table: "Shipments",
            sql: "[Status] IN ('Draft','PendingCreation','Created','CancelRequested','Picking','InTransit','Delivered','DeliveryFailed','Returning','Returned','Cancelled','Exception')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_DeliveredAt",
            table: "Shipments",
            sql: "[DeliveredAt] IS NULL OR [Status] IN ('Delivered','Returning','Returned')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_CancelledAt",
            table: "Shipments",
            sql: "[CancelledAt] IS NULL OR [Status] = 'Cancelled'");

        migrationBuilder.AddCheckConstraint(
            name: "CK_OrderStatusHistory_Category",
            table: "OrderStatusHistories",
            sql: "[Category] IN ('Order','Payment','Fulfillment','Inventory','Integration','Return')");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_ParentShipmentId",
            table: "Shipments",
            column: "ParentShipmentId");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_Direction_Status_CreatedAt",
            table: "Shipments",
            columns: new[] { "Direction", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_OrderId_Direction",
            table: "Shipments",
            columns: new[] { "OrderId", "Direction" },
            unique: true,
            filter: "[Direction] = 'Outbound' AND [Status] <> 'Cancelled' AND [Status] <> 'Returned'");

        migrationBuilder.AddForeignKey(
            name: "FK_Shipments_Shipments_ParentShipmentId",
            table: "Shipments",
            column: "ParentShipmentId",
            principalTable: "Shipments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Shipments_Shipments_ParentShipmentId",
            table: "Shipments");

        migrationBuilder.DropIndex(
            name: "IX_Shipments_ParentShipmentId",
            table: "Shipments");

        migrationBuilder.DropIndex(
            name: "IX_Shipments_Direction_Status_CreatedAt",
            table: "Shipments");

        migrationBuilder.DropIndex(
            name: "IX_Shipments_OrderId_Direction",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_Direction",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_ParentDirection",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_ReturnCodAmount",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_Status",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_DeliveredAt",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Shipment_CancelledAt",
            table: "Shipments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_OrderStatusHistory_Category",
            table: "OrderStatusHistories");

        migrationBuilder.DropColumn(name: "CancelledAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "CancelRequestedAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "CarrierHandoffAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "DeliveredAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "Direction", table: "Shipments");
        migrationBuilder.DropColumn(name: "LastSyncedAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "ParentShipmentId", table: "Shipments");
        migrationBuilder.DropColumn(name: "ProviderStatus", table: "Shipments");
        migrationBuilder.DropColumn(name: "ProviderUpdatedAt", table: "Shipments");

        migrationBuilder.Sql(
            "UPDATE Shipments SET Status = 'Created' WHERE Status = 'CancelRequested';");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Shipment_Status",
            table: "Shipments",
            sql: "[Status] IN ('Draft','PendingCreation','Created','Picking','InTransit','Delivered','DeliveryFailed','Returning','Returned','Cancelled','Exception')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_OrderStatusHistory_Category",
            table: "OrderStatusHistories",
            sql: "[Category] IN ('Order','Payment','Fulfillment','Inventory','Integration')");
    }
}
