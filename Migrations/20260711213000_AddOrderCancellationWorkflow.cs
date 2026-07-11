using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApplication2.Models;

#nullable disable

namespace WebApplication2.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711213000_AddOrderCancellationWorkflow")]
public partial class AddOrderCancellationWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderCancellationRequests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                RequestedBy = table.Column<string>(
                    type: "nvarchar(100)",
                    maxLength: 100,
                    nullable: false),
                ReasonCode = table.Column<string>(
                    type: "nvarchar(50)",
                    maxLength: 50,
                    nullable: false),
                ReasonText = table.Column<string>(
                    type: "nvarchar(500)",
                    maxLength: 500,
                    nullable: false),
                Status = table.Column<int>(
                    type: "int",
                    nullable: false,
                    defaultValue: 0),
                IdempotencyKey = table.Column<string>(
                    type: "nvarchar(128)",
                    maxLength: 128,
                    nullable: false),
                RequestedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false,
                    defaultValueSql: "GETUTCDATE()"),
                ReviewedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                ReviewedBy = table.Column<string>(
                    type: "nvarchar(100)",
                    maxLength: 100,
                    nullable: true),
                ReviewNote = table.Column<string>(
                    type: "nvarchar(500)",
                    maxLength: 500,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true,
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false,
                    defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderCancellationRequests", x => x.Id);
                table.CheckConstraint(
                    "CK_OrderCancellationRequest_Status",
                    "[Status] IN (0,1,2)");
                table.ForeignKey(
                    name: "FK_OrderCancellationRequests_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "OrderCancellationItems",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CancellationRequestId = table.Column<long>(
                    type: "bigint",
                    nullable: false),
                OrderItemId = table.Column<int>(
                    type: "int",
                    nullable: false),
                RequestedQuantity = table.Column<int>(
                    type: "int",
                    nullable: false),
                ApprovedQuantity = table.Column<int>(
                    type: "int",
                    nullable: false,
                    defaultValue: 0),
                RefundAmount = table.Column<decimal>(
                    type: "decimal(18,2)",
                    nullable: false,
                    defaultValue: 0m),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false,
                    defaultValueSql: "GETUTCDATE()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderCancellationItems", x => x.Id);
                table.CheckConstraint(
                    "CK_OrderCancellationItem_RequestedQuantity",
                    "[RequestedQuantity] > 0");
                table.CheckConstraint(
                    "CK_OrderCancellationItem_ApprovedQuantity",
                    "[ApprovedQuantity] >= 0 AND [ApprovedQuantity] <= [RequestedQuantity]");
                table.CheckConstraint(
                    "CK_OrderCancellationItem_RefundAmount",
                    "[RefundAmount] >= 0");
                table.ForeignKey(
                    name: "FK_OrderCancellationItems_OrderCancellationRequests_CancellationRequestId",
                    column: x => x.CancellationRequestId,
                    principalTable: "OrderCancellationRequests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_OrderCancellationItems_OrderItems_OrderItemId",
                    column: x => x.OrderItemId,
                    principalTable: "OrderItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderCancellationRequests_IdempotencyKey",
            table: "OrderCancellationRequests",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderCancellationRequests_OrderId_Status_RequestedAt",
            table: "OrderCancellationRequests",
            columns: new[] { "OrderId", "Status", "RequestedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_OrderCancellationItems_CancellationRequestId_OrderItemId",
            table: "OrderCancellationItems",
            columns: new[] { "CancellationRequestId", "OrderItemId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderCancellationItems_OrderItemId",
            table: "OrderCancellationItems",
            column: "OrderItemId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OrderCancellationItems");

        migrationBuilder.DropTable(
            name: "OrderCancellationRequests");
    }
}
