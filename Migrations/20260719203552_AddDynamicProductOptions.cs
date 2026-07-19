using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicProductOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductOptionGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductOptionGroups", x => x.Id);
                    table.CheckConstraint("CK_ProductOptionGroup_DisplayOrder", "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
                    table.ForeignKey(
                        name: "FK_ProductOptionGroups_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductOptionValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OptionGroupId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductOptionValues", x => x.Id);
                    table.UniqueConstraint("AK_ProductOptionValues_OptionGroupId_Id", x => new { x.OptionGroupId, x.Id });
                    table.CheckConstraint("CK_ProductOptionValue_DisplayOrder", "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
                    table.ForeignKey(
                        name: "FK_ProductOptionValues_ProductOptionGroups_OptionGroupId",
                        column: x => x.OptionGroupId,
                        principalTable: "ProductOptionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantOptionSelections",
                columns: table => new
                {
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    OptionGroupId = table.Column<int>(type: "int", nullable: false),
                    OptionValueId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantOptionSelections", x => new { x.VariantId, x.OptionGroupId });
                    table.ForeignKey(
                        name: "FK_ProductVariantOptionSelections_ProductOptionGroups_OptionGroupId",
                        column: x => x.OptionGroupId,
                        principalTable: "ProductOptionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantOptionSelections_ProductOptionValues_OptionGroupId_OptionValueId",
                        columns: x => new { x.OptionGroupId, x.OptionValueId },
                        principalTable: "ProductOptionValues",
                        principalColumns: new[] { "OptionGroupId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantOptionSelections_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionGroups_ProductId_Code",
                table: "ProductOptionGroups",
                columns: new[] { "ProductId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionGroups_ProductId_IsActive_DisplayOrder",
                table: "ProductOptionGroups",
                columns: new[] { "ProductId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionValues_OptionGroupId_Code",
                table: "ProductOptionValues",
                columns: new[] { "OptionGroupId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionValues_OptionGroupId_IsActive_DisplayOrder",
                table: "ProductOptionValues",
                columns: new[] { "OptionGroupId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantOptionSelections_OptionGroupId_OptionValueId",
                table: "ProductVariantOptionSelections",
                columns: new[] { "OptionGroupId", "OptionValueId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductVariantOptionSelections");

            migrationBuilder.DropTable(
                name: "ProductOptionValues");

            migrationBuilder.DropTable(
                name: "ProductOptionGroups");
        }
    }
}
