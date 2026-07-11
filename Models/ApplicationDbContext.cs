using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<ProductPromotion> ProductPromotions => Set<ProductPromotion>();
    public DbSet<PriceCampaign> PriceCampaigns => Set<PriceCampaign>();
    public DbSet<PriceCampaignItem> PriceCampaignItems => Set<PriceCampaignItem>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<PromotionCustomer> PromotionCustomers => Set<PromotionCustomer>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<IntegrationInboxEvent> IntegrationInboxEvents => Set<IntegrationInboxEvent>();
    public DbSet<IntegrationOutboxMessage> IntegrationOutboxMessages => Set<IntegrationOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureCatalog(modelBuilder);
        ConfigurePricing(modelBuilder);
        ConfigurePromotions(modelBuilder);
        ConfigureCustomers(modelBuilder);
        modelBuilder.ConfigureCommerce();
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(item => item.Slug).IsUnique();
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(item => item.Parent)
                .WithMany(item => item.Children)
                .HasForeignKey(item => item.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.HasIndex(item => item.Slug).IsUnique();
            entity.Property(item => item.IsActive).HasDefaultValue(true);
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(item => item.Slug).IsUnique();
            entity.HasIndex(item => new { item.CategoryId, item.BrandId, item.IsActive });
            entity.Property(item => item.IsActive).HasDefaultValue(true);
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasIndex(item => item.SKU).IsUnique();
            entity.HasIndex(item => new { item.ProductId, item.IsActive });
            entity.HasIndex(item => new { item.Color, item.Size });
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();
            entity.Property(item => item.CurrentPriceSourceType)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(EffectivePriceSourceType.ListPrice);
            entity.HasIndex(item => new
            {
                item.CurrentPriceSourceType,
                item.CurrentPriceSourceId
            });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ProductVariant_Price", "[Price] > 0");
                table.HasCheckConstraint("CK_ProductVariant_CurrentPrice", "[CurrentPrice] > 0");
                table.HasCheckConstraint("CK_ProductVariant_StockQuantity", "[StockQuantity] >= 0");
                table.HasCheckConstraint(
                    "CK_ProductVariant_CurrentPriceSourceType",
                    "[CurrentPriceSourceType] IN ('ListPrice','Campaign')");
            });
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("ProductImage");

            entity.HasIndex(item => new { item.ProductId, item.IsMain })
                .IsUnique()
                .HasFilter("[IsMain] = 1");
        });
    }

    private static void ConfigurePricing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PriceCampaign>(entity =>
        {
            entity.Property(item => item.Code).HasMaxLength(50);
            entity.Property(item => item.Mode)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceCampaignMode.FixedWindow);
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceCampaignStatus.Draft);
            entity.Property(item => item.SourceType)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceChangeSourceType.Manual);
            entity.Property(item => item.ConflictPolicy)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceConflictPolicy.Reject);
            entity.Property(item => item.IsActive).HasDefaultValue(false);
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(item => item.RowVersion).IsRowVersion();

            entity.HasIndex(item => item.Code).IsUnique();
            entity.HasIndex(item => item.ClientRequestId)
                .IsUnique()
                .HasFilter("[ClientRequestId] IS NOT NULL");
            entity.HasIndex(item => new { item.Status, item.StartDate, item.EndDate });

            entity.HasOne(item => item.SupersededByCampaign)
                .WithMany(item => item.SupersededCampaigns)
                .HasForeignKey(item => item.SupersededByCampaignId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PriceCampaign_Duration",
                    "([Mode] = 'OpenEnded' AND [EndDate] IS NULL) OR " +
                    "([Mode] = 'FixedWindow' AND [EndDate] IS NOT NULL AND [EndDate] > [StartDate])");
                table.HasCheckConstraint(
                    "CK_PriceCampaign_Mode",
                    "[Mode] IN ('FixedWindow','OpenEnded')");
                table.HasCheckConstraint(
                    "CK_PriceCampaign_Status",
                    "[Status] IS NOT NULL AND " +
                    "[Status] IN ('Draft','Confirmed','Scheduled','Active','Completed','Cancelled','Superseded')");
                table.HasCheckConstraint(
                    "CK_PriceCampaign_SourceType",
                    "[SourceType] IN ('Manual','Market','Promotion','Recovery','Legacy','System')");
                table.HasCheckConstraint(
                    "CK_PriceCampaign_ConflictPolicy",
                    "[ConflictPolicy] IN ('Reject','ReplaceFromStart','SupersedeNow')");
            });
        });

        modelBuilder.Entity<PriceCampaignItem>(entity =>
        {
            entity.HasKey(item => new { item.CampaignId, item.VariantId });
            entity.HasIndex(item => item.VariantId);
            entity.Property(item => item.AdjustmentType)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceAdjustmentType.FixedPrice);
            entity.Property(item => item.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("VND");

            entity.HasOne(item => item.Campaign)
                .WithMany(item => item.CampaignItems)
                .HasForeignKey(item => item.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Variant)
                .WithMany(item => item.CampaignItems)
                .HasForeignKey(item => item.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_PriceCampaignItem_NewPrice", "[NewPrice] > 0");
                table.HasCheckConstraint("CK_PriceCampaignItem_ListPriceSnapshot", "[ListPriceSnapshot] > 0");
                table.HasCheckConstraint("CK_PriceCampaignItem_EffectivePriceSnapshot", "[EffectivePriceSnapshot] > 0");
                table.HasCheckConstraint(
                    "CK_PriceCampaignItem_PreviousEffectivePriceSnapshot",
                    "[PreviousEffectivePriceSnapshot] > 0");
                table.HasCheckConstraint(
                    "CK_PriceCampaignItem_AdjustmentType",
                    "[AdjustmentType] IN ('FixedPrice','PercentOff','AmountOff')");
            });
        });

        modelBuilder.Entity<PriceHistory>(entity =>
        {
            entity.Property(item => item.EventType)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceHistoryEventType.Legacy);
            entity.Property(item => item.SourceType)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(PriceChangeSourceType.Legacy);
            entity.HasIndex(item => new { item.ProductVariantId, item.CreatedAt });
            entity.HasIndex(item => item.CorrelationId);

            entity.HasOne(item => item.ProductVariant)
                .WithMany(item => item.PriceHistories)
                .HasForeignKey(item => item.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_PriceHistory_OldPrice", "[OldPrice] > 0");
                table.HasCheckConstraint("CK_PriceHistory_NewPrice", "[NewPrice] > 0");
                table.HasCheckConstraint(
                    "CK_PriceHistory_EventType",
                    "[EventType] IN ('Applied','Restored','Replaced','Cancelled','ListPriceChanged','Legacy')");
                table.HasCheckConstraint(
                    "CK_PriceHistory_SourceType",
                    "[SourceType] IN ('Manual','Market','Promotion','Recovery','Legacy','System')");
            });
        });
    }

    private static void ConfigurePromotions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.Property(item => item.IsActive).HasDefaultValue(true);
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.HasIndex(item => new { item.StartDate, item.EndDate, item.IsActive });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Promotion_DiscountValue", "[DiscountValue] > 0");
                table.HasCheckConstraint("CK_Promotion_Duration", "[EndDate] > [StartDate]");
            });
        });

        modelBuilder.Entity<ProductPromotion>()
            .HasKey(item => new { item.ProductId, item.PromotionId });

        modelBuilder.Entity<PromotionCustomer>(entity =>
        {
            entity.HasKey(item => new { item.PromotionId, item.CustomerId });
            entity.HasOne(item => item.Promotion)
                .WithMany(item => item.PromotionCustomers)
                .HasForeignKey(item => item.PromotionId);
            entity.HasOne(item => item.Customer)
                .WithMany(item => item.PromotionCustomers)
                .HasForeignKey(item => item.CustomerId);
        });
    }

    private static void ConfigureCustomers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().HasIndex(item => item.Email).IsUnique();

        modelBuilder.Entity<Customer>()
            .HasOne(item => item.Account)
            .WithOne(item => item.Customer)
            .HasForeignKey<Customer>(item => item.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
