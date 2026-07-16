using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryMerchandising : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentId",
                table: "Categories");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Categories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IconKey",
                table: "Categories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "folder");

            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "Categories",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId_IsVisible_DisplayOrder",
                table: "Categories",
                columns: new[] { "ParentId", "IsVisible", "DisplayOrder" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Category_DisplayOrder",
                table: "Categories",
                sql: "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentId_IsVisible_DisplayOrder",
                table: "Categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Category_DisplayOrder",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IconKey",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsVisible",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");
        }
    }
}
