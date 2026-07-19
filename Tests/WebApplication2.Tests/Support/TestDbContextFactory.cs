using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebApplication2.Models;

namespace WebApplication2.Tests.Support;

internal static class TestDbContextFactory
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"fastbuy-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .EnableDetailedErrors()
            .Options;

        return new ApplicationDbContext(options);
    }

    public static ApplicationDbContext CreateSqlServerModelContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;"
                + "Database=FastBuyModelInspection;"
                + "Trusted_Connection=True;"
                + "TrustServerCertificate=True")
            .Options;

        return new ApplicationDbContext(options);
    }
}
