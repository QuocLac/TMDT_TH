using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductInformationArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductAttributeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsFilterable = table.Column<bool>(type: "bit", nullable: false),
                    IsComparable = table.Column<bool>(type: "bit", nullable: false),
                    IsCustomerVisible = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeDefinitions", x => x.Id);
                    table.CheckConstraint("CK_ProductAttributeDefinition_DataType", "[DataType] IN ('ShortText','LongText','Number','Boolean','Date','SingleChoice')");
                });

            migrationBuilder.CreateTable(
                name: "CategoryProductAttributes",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    AttributeDefinitionId = table.Column<int>(type: "int", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryProductAttributes", x => new { x.CategoryId, x.AttributeDefinitionId });
                    table.CheckConstraint("CK_CategoryProductAttribute_DisplayOrder", "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
                    table.ForeignKey(
                        name: "FK_CategoryProductAttributes_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategoryProductAttributes_ProductAttributeDefinitions_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "ProductAttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttributeDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeOptions", x => x.Id);
                    table.UniqueConstraint("AK_ProductAttributeOptions_AttributeDefinitionId_Id", x => new { x.AttributeDefinitionId, x.Id });
                    table.CheckConstraint("CK_ProductAttributeOption_DisplayOrder", "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
                    table.ForeignKey(
                        name: "FK_ProductAttributeOptions_ProductAttributeDefinitions_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "ProductAttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    AttributeDefinitionId = table.Column<int>(type: "int", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NumberValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    DateValue = table.Column<DateTime>(type: "date", nullable: true),
                    OptionId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValues", x => x.Id);
                    table.CheckConstraint("CK_ProductAttributeValue_OneValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [NumberValue] IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END) + (CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_ProductAttributeDefinitions_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "ProductAttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_ProductAttributeOptions_AttributeDefinitionId_OptionId",
                        columns: x => new { x.AttributeDefinitionId, x.OptionId },
                        principalTable: "ProductAttributeOptions",
                        principalColumns: new[] { "AttributeDefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProductAttributes_AttributeDefinitionId",
                table: "CategoryProductAttributes",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProductAttributes_CategoryId_DisplayOrder",
                table: "CategoryProductAttributes",
                columns: new[] { "CategoryId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeDefinitions_Code",
                table: "ProductAttributeDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeDefinitions_IsActive_IsCustomerVisible",
                table: "ProductAttributeDefinitions",
                columns: new[] { "IsActive", "IsCustomerVisible" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeOptions_AttributeDefinitionId_DisplayOrder",
                table: "ProductAttributeOptions",
                columns: new[] { "AttributeDefinitionId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeOptions_AttributeDefinitionId_Value",
                table: "ProductAttributeOptions",
                columns: new[] { "AttributeDefinitionId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_AttributeDefinitionId_OptionId",
                table: "ProductAttributeValues",
                columns: new[] { "AttributeDefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_ProductId_AttributeDefinitionId",
                table: "ProductAttributeValues",
                columns: new[] { "ProductId", "AttributeDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryProductAttributes");

            migrationBuilder.DropTable(
                name: "ProductAttributeValues");

            migrationBuilder.DropTable(
                name: "ProductAttributeOptions");

            migrationBuilder.DropTable(
                name: "ProductAttributeDefinitions");
        }
    }
}
