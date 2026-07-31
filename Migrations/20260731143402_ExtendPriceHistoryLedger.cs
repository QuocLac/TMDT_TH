using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations
{
    /// <inheritdoc />
    public partial class ExtendPriceHistoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceHistories_ProductVariants_ProductVariantId",
                table: "PriceHistories");

            migrationBuilder.DropIndex(
                name: "IX_PriceHistories_ProductVariantId_CreatedAt",
                table: "PriceHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PriceHistory_EventType",
                table: "PriceHistories");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "PriceHistories",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PriceHistories",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<string>(
                name: "PriceKind",
                table: "PriceHistories",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "EffectivePrice");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_ProductVariantId_PriceKind_EffectiveFrom_CreatedAt",
                table: "PriceHistories",
                columns: new[] { "ProductVariantId", "PriceKind", "EffectiveFrom", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PriceHistory_Currency",
                table: "PriceHistories",
                sql: "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PriceHistory_EventType",
                table: "PriceHistories",
                sql: "[EventType] IN ('Applied','Restored','Replaced','Cancelled','ListPriceChanged','EffectivePriceChanged','Legacy')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PriceHistory_PriceKind",
                table: "PriceHistories",
                sql: "[PriceKind] IN ('ListPrice','EffectivePrice')");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceHistories_ProductVariants_ProductVariantId",
                table: "PriceHistories",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceHistories_ProductVariants_ProductVariantId",
                table: "PriceHistories");

            migrationBuilder.DropIndex(
                name: "IX_PriceHistories_ProductVariantId_PriceKind_EffectiveFrom_CreatedAt",
                table: "PriceHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PriceHistory_Currency",
                table: "PriceHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PriceHistory_EventType",
                table: "PriceHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PriceHistory_PriceKind",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "PriceKind",
                table: "PriceHistories");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "PriceHistories",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_ProductVariantId_CreatedAt",
                table: "PriceHistories",
                columns: new[] { "ProductVariantId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PriceHistory_EventType",
                table: "PriceHistories",
                sql: "[EventType] IN ('Applied','Restored','Replaced','Cancelled','ListPriceChanged','Legacy')");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceHistories_ProductVariants_ProductVariantId",
                table: "PriceHistories",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
