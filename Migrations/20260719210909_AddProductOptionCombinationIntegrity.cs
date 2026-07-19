using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductOptionCombinationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OptionCombinationKey",
                table: "ProductVariants",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_OptionCombinationKey",
                table: "ProductVariants",
                columns: new[] { "ProductId", "OptionCombinationKey" },
                unique: true,
                filter: "[OptionCombinationKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId_OptionCombinationKey",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "OptionCombinationKey",
                table: "ProductVariants");
        }
    }
}
