using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Brand> Brands { get; set; } // Thêm mới
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<ProductPromotion> ProductPromotions { get; set; }
        public DbSet<PriceCampaign> PriceCampaigns { get; set; } // Thêm mới
        public DbSet<PriceCampaignItem> PriceCampaignItems { get; set; } // Thêm mới
        public DbSet<PriceHistory> PriceHistories { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<PromotionCustomer> PromotionCustomers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =======================================================
            // 1. CẤU HÌNH ĐỊNH DANH DUY NHẤT & QUAN HỆ
            // =======================================================
            modelBuilder.Entity<Category>()
                .HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Category>().HasIndex(c => c.Slug).IsUnique();
            modelBuilder.Entity<Brand>().HasIndex(b => b.Slug).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => p.Slug).IsUnique();
            modelBuilder.Entity<ProductVariant>().HasIndex(v => v.SKU).IsUnique();

            modelBuilder.Entity<ProductPromotion>()
                .HasKey(pp => new { pp.ProductId, pp.PromotionId });

            modelBuilder.Entity<PriceCampaignItem>()
                .HasKey(pc => new { pc.CampaignId, pc.VariantId });

            modelBuilder.Entity<PriceCampaignItem>()
                .HasOne(pc => pc.Campaign)
                .WithMany(c => c.CampaignItems)
                .HasForeignKey(pc => pc.CampaignId);

            modelBuilder.Entity<PriceCampaignItem>()
                .HasOne(pc => pc.Variant)
                .WithMany(v => v.CampaignItems)
                .HasForeignKey(pc => pc.VariantId);

            modelBuilder.Entity<PriceCampaign>()
                .Property(c => c.RowVersion)
                .IsRowVersion();

            modelBuilder.Entity<ProductVariant>()
                .Property(v => v.RowVersion)
                .IsRowVersion();

            // =======================================================
            // 2. RÀNG BUỘC GIÁ TRỊ MẶC ĐỊNH
            // =======================================================
            modelBuilder.Entity<Product>().Property(p => p.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Promotion>().Property(p => p.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Brand>().Property(b => b.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<PriceCampaign>().Property(c => c.IsActive).HasDefaultValue(true);

            modelBuilder.Entity<Category>().Property(c => c.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<Brand>().Property(b => b.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<Product>().Property(p => p.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<ProductVariant>().Property(v => v.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<Promotion>().Property(p => p.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<PriceCampaign>().Property(c => c.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // =======================================================
            // 3. RÀNG BUỘC KIỂM TRA CHẶT CHẼ
            // =======================================================
            modelBuilder.Entity<ProductVariant>()
                .ToTable(t => t.HasCheckConstraint("CK_ProductVariant_Price", "[Price] > 0"))
                .ToTable(t => t.HasCheckConstraint("CK_ProductVariant_CurrentPrice", "[CurrentPrice] > 0"))
                .ToTable(t => t.HasCheckConstraint("CK_ProductVariant_StockQuantity", "[StockQuantity] >= 0"));

            modelBuilder.Entity<PriceCampaignItem>()
                .ToTable(t => t.HasCheckConstraint("CK_PriceCampaignItem_NewPrice", "[NewPrice] > 0"));

            modelBuilder.Entity<Promotion>()
                .ToTable(t => t.HasCheckConstraint("CK_Promotion_DiscountValue", "[DiscountValue] > 0"))
                .ToTable(t => t.HasCheckConstraint("CK_Promotion_Duration", "[EndDate] > [StartDate]"));

            modelBuilder.Entity<PriceCampaign>()
                .ToTable(t => t.HasCheckConstraint("CK_PriceCampaign_Duration", "[EndDate] > [StartDate]"));

            // =======================================================
            // 4. CHỈ MỤC BỔ SUNG 
            // =======================================================
            modelBuilder.Entity<Product>().HasIndex(p => new { p.CategoryId, p.BrandId, p.IsActive });
            modelBuilder.Entity<ProductVariant>().HasIndex(v => new { v.Color, v.Size });
            modelBuilder.Entity<Promotion>().HasIndex(p => new { p.StartDate, p.EndDate, p.IsActive });
            modelBuilder.Entity<PriceCampaign>().HasIndex(c => new { c.StartDate, c.EndDate, c.IsActive });

            modelBuilder.Entity<Customer>()
                .HasOne(c => c.Account)
                .WithOne(a => a.Customer)
                .HasForeignKey<Customer>(c => c.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Account>().HasIndex(a => a.Email).IsUnique();

            modelBuilder.Entity<PromotionCustomer>()
                .HasKey(pc => new { pc.PromotionId, pc.CustomerId });

            modelBuilder.Entity<PromotionCustomer>()
                .HasOne(pc => pc.Promotion)
                .WithMany(p => p.PromotionCustomers)
                .HasForeignKey(pc => pc.PromotionId);

            modelBuilder.Entity<PromotionCustomer>()
                .HasOne(pc => pc.Customer)
                .WithMany(c => c.PromotionCustomers)
                .HasForeignKey(pc => pc.CustomerId);
        }
    }
}
