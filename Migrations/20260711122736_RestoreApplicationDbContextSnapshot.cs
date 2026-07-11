using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations;

public partial class RestoreApplicationDbContextSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty.
        // The database already matches the current EF Core model.
        // This migration restores migration metadata and the model snapshot.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty.
        // Removing the metadata baseline must not alter the database.
    }
}