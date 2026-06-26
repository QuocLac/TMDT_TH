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
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<ProductPromotion> ProductPromotions { get; set; }
        public DbSet<PriceHistory> PriceHistories { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<PromotionCustomer> PromotionCustomers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =======================================================
            // 1. CẤU HÌNH ĐỊNH DANH DUY NHẤT (UNIQUE INDEXES) & QUAN HỆ
            // =======================================================
            modelBuilder.Entity<Category>()
                .HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Category>().HasIndex(c => c.Slug).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => p.Slug).IsUnique();
            modelBuilder.Entity<ProductVariant>().HasIndex(v => v.SKU).IsUnique();

            modelBuilder.Entity<ProductPromotion>()
                .HasKey(pp => new { pp.ProductId, pp.PromotionId });

            // =======================================================
            // 2. RÀNG BUỘC GIÁ TRỊ MẶC ĐỊNH
            // =======================================================
            modelBuilder.Entity<Product>().Property(p => p.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Promotion>().Property(p => p.IsActive).HasDefaultValue(true);

            modelBuilder.Entity<Category>().Property(c => c.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<Product>().Property(p => p.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<ProductVariant>().Property(v => v.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            modelBuilder.Entity<Promotion>().Property(p => p.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // =======================================================
            // 3. RÀNG BUỘC KIỂM TRA CHẶT CHẼ
            // =======================================================
            modelBuilder.Entity<ProductVariant>()
                .ToTable(t => t.HasCheckConstraint("CK_ProductVariant_Price", "[Price] > 0"))
                .ToTable(t => t.HasCheckConstraint("CK_ProductVariant_StockQuantity", "[StockQuantity] >= 0"));

            modelBuilder.Entity<Promotion>()
                .ToTable(t => t.HasCheckConstraint("CK_Promotion_DiscountValue", "[DiscountValue] > 0"))
                .ToTable(t => t.HasCheckConstraint("CK_Promotion_Duration", "[EndDate] > [StartDate]"));

            // =======================================================
            // 4. CHỈ MỤC BỔ SUNG & CRM
            // =======================================================
            modelBuilder.Entity<Product>().HasIndex(p => new { p.CategoryId, p.IsActive });
            modelBuilder.Entity<ProductVariant>().HasIndex(v => new { v.Color, v.Size });
            modelBuilder.Entity<Promotion>().HasIndex(p => new { p.StartDate, p.EndDate, p.IsActive });

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