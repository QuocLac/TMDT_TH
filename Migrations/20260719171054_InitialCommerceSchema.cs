using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication2.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommerceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Avt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Customer"),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAccessCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Account_FailedAccessCount", "[FailedAccessCount] >= 0");
                    table.CheckConstraint("CK_Account_Role", "[Role] IN ('Customer','Admin')");
                });

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IconKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "folder"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsVisible = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    MetaTitle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MetaDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.CheckConstraint("CK_Category_DisplayOrder", "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationInboxEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Received"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderOccurredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationInboxEvents", x => x.Id);
                    table.CheckConstraint("CK_IntegrationInboxEvent_AttemptCount", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_IntegrationInboxEvent_Status", "[Status] IN ('Received','Processing','Processed','Ignored','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_IntegrationOutboxMessage_AttemptCount", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_IntegrationOutboxMessage_Status", "[Status] IN ('Pending','Processing','Completed','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "PriceCampaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "FixedWindow"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Manual"),
                    ConflictPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Reject"),
                    ClientRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupersededByCampaignId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCampaigns", x => x.Id);
                    table.CheckConstraint("CK_PriceCampaign_ConflictPolicy", "[ConflictPolicy] IN ('Reject','ReplaceFromStart','SupersedeNow')");
                    table.CheckConstraint("CK_PriceCampaign_Duration", "([Mode] = 'OpenEnded' AND [EndDate] IS NULL) OR ([Mode] = 'FixedWindow' AND [EndDate] IS NOT NULL AND [EndDate] > [StartDate])");
                    table.CheckConstraint("CK_PriceCampaign_Mode", "[Mode] IN ('FixedWindow','OpenEnded')");
                    table.CheckConstraint("CK_PriceCampaign_SourceType", "[SourceType] IN ('Manual','Market','Promotion','Recovery','Legacy','System')");
                    table.CheckConstraint("CK_PriceCampaign_Status", "[Status] IS NOT NULL AND [Status] IN ('Draft','Confirmed','Scheduled','Active','Completed','Cancelled','Superseded')");
                    table.ForeignKey(
                        name: "FK_PriceCampaigns_PriceCampaigns_SupersededByCampaignId",
                        column: x => x.SupersededByCampaignId,
                        principalTable: "PriceCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CouponCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    TargetTier = table.Column<int>(type: "int", nullable: true),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    UsageLimit = table.Column<int>(type: "int", nullable: true),
                    UsedCount = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.CheckConstraint("CK_Promotion_DiscountValue", "[DiscountValue] > 0");
                    table.CheckConstraint("CK_Promotion_Duration", "[EndDate] > [StartDate]");
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<int>(type: "int", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MetaKeywords = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Addresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Ward = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProvinceId = table.Column<int>(type: "int", nullable: true),
                    DistrictId = table.Column<int>(type: "int", nullable: true),
                    WardCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ValidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Addresses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PublicToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClientRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ShippingAddressLine = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ShippingWard = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShippingDistrict = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShippingCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShippingProvinceId = table.Column<int>(type: "int", nullable: true),
                    ShippingDistrictId = table.Column<int>(type: "int", nullable: true),
                    ShippingWardCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShippingFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    OrderStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "PendingPayment"),
                    PaymentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    FulfillmentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Unfulfilled"),
                    PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Order_DiscountTotal", "[DiscountTotal] >= 0");
                    table.CheckConstraint("CK_Order_FulfillmentStatus", "[FulfillmentStatus] IN ('Unfulfilled','Preparing','ReadyToShip','Shipped','Delivered','DeliveryFailed','Returning','Returned','Cancelled')");
                    table.CheckConstraint("CK_Order_GrandTotal", "[GrandTotal] >= 0");
                    table.CheckConstraint("CK_Order_OrderStatus", "[OrderStatus] IN ('PendingPayment','Placed','Confirmed','Processing','Completed','Cancelled','Closed')");
                    table.CheckConstraint("CK_Order_PaymentStatus", "[PaymentStatus] IN ('Pending','CodPending','Paid','Failed','Cancelled','PartiallyRefunded','Refunded')");
                    table.CheckConstraint("CK_Order_ShippingFee", "[ShippingFee] >= 0");
                    table.CheckConstraint("CK_Order_Subtotal", "[Subtotal] >= 0");
                    table.CheckConstraint("CK_Order_TaxTotal", "[TaxTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PromotionCustomers",
                columns: table => new
                {
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionCustomers", x => new { x.PromotionId, x.CustomerId });
                    table.ForeignKey(
                        name: "FK_PromotionCustomers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromotionCustomers_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsMain = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImage_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductPromotions",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    PromotionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPromotions", x => new { x.ProductId, x.PromotionId });
                    table.ForeignKey(
                        name: "FK_ProductPromotions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductPromotions_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SKU = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrentPriceSourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "ListPrice"),
                    CurrentPriceSourceId = table.Column<int>(type: "int", nullable: true),
                    CurrentPriceEffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPriceEffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.CheckConstraint("CK_ProductVariant_CurrentPrice", "[CurrentPrice] > 0");
                    table.CheckConstraint("CK_ProductVariant_CurrentPriceSourceType", "[CurrentPriceSourceType] IN ('ListPrice','Campaign')");
                    table.CheckConstraint("CK_ProductVariant_Price", "[Price] > 0");
                    table.CheckConstraint("CK_ProductVariant_StockQuantity", "[StockQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderCancellationRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCancellationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCancellationRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerVisible = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_OrderStatusHistory_Category", "[Category] IN ('Order','Payment','Fulfillment','Inventory','Integration','Return')");
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    MerchantReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.CheckConstraint("CK_PaymentTransaction_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_PaymentTransaction_AttemptNumber", "[AttemptNumber] > 0");
                    table.CheckConstraint("CK_PaymentTransaction_Status", "[Status] IN ('Pending','CodPending','Paid','Failed','Cancelled','PartiallyRefunded','Refunded')");
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReturnWindowExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InspectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InspectionResult = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequest_InspectionResult", "[InspectionResult] IS NULL OR [InspectionResult] IN ('Accepted','PartiallyAccepted','Rejected')");
                    table.CheckConstraint("CK_ReturnRequest_Status", "[Status] IN ('Requested','UnderReview','Approved','Rejected','AwaitingReturnShipment','AwaitingPickup','ReturnInTransit','ReceivedAtWarehouse','Inspecting','RefundPending','RejectedAfterInspection','Refunded','Closed','Cancelled')");
                    table.CheckConstraint("CK_ReturnRequest_Window", "[RequestedAt] <= [ReturnWindowExpiresAt]");
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    MovementType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    QuantityDelta = table.Column<int>(type: "int", nullable: false),
                    QuantityBefore = table.Column<int>(type: "int", nullable: false),
                    QuantityAfter = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.CheckConstraint("CK_InventoryMovement_MovementType", "[MovementType] IN ('ReservationCreated','ReservationReleased','ReservationExpired','ManualIncrease','ManualDecrease','ReturnReceived','ReturnRestocked','ReturnWriteOff')");
                    table.CheckConstraint("CK_InventoryMovement_QuantityAfter", "[QuantityAfter] >= 0");
                    table.CheckConstraint("CK_InventoryMovement_QuantityBefore", "[QuantityBefore] >= 0");
                    table.CheckConstraint("CK_InventoryMovement_QuantityDelta", "([MovementType] IN ('ReturnReceived','ReturnWriteOff') AND [QuantityDelta] = 0) OR ([MovementType] NOT IN ('ReturnReceived','ReturnWriteOff') AND [QuantityDelta] <> 0)");
                    table.CheckConstraint("CK_InventoryMovement_QuantityEquation", "[QuantityAfter] = [QuantityBefore] + [QuantityDelta]");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VariantDescription = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItem_DiscountAmount", "[DiscountAmount] >= 0");
                    table.CheckConstraint("CK_OrderItem_LineTotal", "[LineTotal] >= 0");
                    table.CheckConstraint("CK_OrderItem_ListPrice", "[ListPrice] >= 0");
                    table.CheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_OrderItem_TaxAmount", "[TaxAmount] >= 0");
                    table.CheckConstraint("CK_OrderItem_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceCampaignItems",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    ListPriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectivePriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PreviousEffectivePriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "FixedPrice"),
                    AdjustmentValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "VND")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCampaignItems", x => new { x.CampaignId, x.VariantId });
                    table.CheckConstraint("CK_PriceCampaignItem_AdjustmentType", "[AdjustmentType] IN ('FixedPrice','PercentOff','AmountOff')");
                    table.CheckConstraint("CK_PriceCampaignItem_EffectivePriceSnapshot", "[EffectivePriceSnapshot] > 0");
                    table.CheckConstraint("CK_PriceCampaignItem_ListPriceSnapshot", "[ListPriceSnapshot] > 0");
                    table.CheckConstraint("CK_PriceCampaignItem_NewPrice", "[NewPrice] > 0");
                    table.CheckConstraint("CK_PriceCampaignItem_PreviousEffectivePriceSnapshot", "[PreviousEffectivePriceSnapshot] > 0");
                    table.ForeignKey(
                        name: "FK_PriceCampaignItems_PriceCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "PriceCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceCampaignItems_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Legacy"),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Legacy"),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceHistories", x => x.Id);
                    table.CheckConstraint("CK_PriceHistory_EventType", "[EventType] IN ('Applied','Restored','Replaced','Cancelled','ListPriceChanged','Legacy')");
                    table.CheckConstraint("CK_PriceHistory_NewPrice", "[NewPrice] > 0");
                    table.CheckConstraint("CK_PriceHistory_OldPrice", "[OldPrice] > 0");
                    table.CheckConstraint("CK_PriceHistory_SourceType", "[SourceType] IN ('Manual','Market','Promotion','Recovery','Legacy','System')");
                    table.ForeignKey(
                        name: "FK_PriceHistories_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnEvidence",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnEvidence", x => x.Id);
                    table.CheckConstraint("CK_ReturnEvidence_Type", "[Type] IN ('Image','Video')");
                    table.ForeignKey(
                        name: "FK_ReturnEvidence_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInspections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    Inspector = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInspections", x => x.Id);
                    table.CheckConstraint("CK_ReturnInspection_Result", "[Result] IN ('Accepted','PartiallyAccepted','Rejected')");
                    table.ForeignKey(
                        name: "FK_ReturnInspections_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Outbound"),
                    ParentShipmentId = table.Column<long>(type: "bigint", nullable: true),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    ProviderStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CarrierHandoffAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ServiceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ServiceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalOrderCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TrackingCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CodAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WeightGram = table.Column<int>(type: "int", nullable: false),
                    LengthCm = table.Column<int>(type: "int", nullable: false),
                    WidthCm = table.Column<int>(type: "int", nullable: false),
                    HeightCm = table.Column<int>(type: "int", nullable: false),
                    EstimatedDeliveryAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShipperName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShipperPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CurrentHub = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ProviderReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipment_CancelledAt", "[CancelledAt] IS NULL OR [Status] = 'Cancelled'");
                    table.CheckConstraint("CK_Shipment_CodAmount", "[CodAmount] >= 0");
                    table.CheckConstraint("CK_Shipment_DeliveredAt", "[DeliveredAt] IS NULL OR [Status] IN ('Delivered','Returning','Returned')");
                    table.CheckConstraint("CK_Shipment_Direction", "[Direction] IN ('Outbound','Return')");
                    table.CheckConstraint("CK_Shipment_DirectionOwnership", "([Direction] = 'Outbound' AND [ParentShipmentId] IS NULL AND [ReturnRequestId] IS NULL) OR ([Direction] = 'Return' AND [ParentShipmentId] IS NOT NULL AND [ReturnRequestId] IS NOT NULL)");
                    table.CheckConstraint("CK_Shipment_Fee", "[Fee] >= 0");
                    table.CheckConstraint("CK_Shipment_HeightCm", "[HeightCm] >= 0");
                    table.CheckConstraint("CK_Shipment_LengthCm", "[LengthCm] >= 0");
                    table.CheckConstraint("CK_Shipment_ReturnCodAmount", "[Direction] = 'Outbound' OR [CodAmount] = 0");
                    table.CheckConstraint("CK_Shipment_Status", "[Status] IN ('Draft','PendingCreation','Created','CancelRequested','Picking','InTransit','Delivered','DeliveryFailed','Returning','Returned','Cancelled','Exception')");
                    table.CheckConstraint("CK_Shipment_WeightGram", "[WeightGram] >= 0");
                    table.CheckConstraint("CK_Shipment_WidthCm", "[WidthCm] >= 0");
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Shipments_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Shipments_ParentShipmentId",
                        column: x => x.ParentShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderCancellationItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CancellationRequestId = table.Column<long>(type: "bigint", nullable: false),
                    OrderItemId = table.Column<int>(type: "int", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "int", nullable: false),
                    ApprovedQuantity = table.Column<int>(type: "int", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCancellationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCancellationItems_OrderCancellationRequests_CancellationRequestId",
                        column: x => x.CancellationRequestId,
                        principalTable: "OrderCancellationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderCancellationItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    OrderItemId = table.Column<int>(type: "int", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "int", nullable: false),
                    ApprovedQuantity = table.Column<int>(type: "int", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "int", nullable: false),
                    AcceptedQuantity = table.Column<int>(type: "int", nullable: false),
                    RejectedQuantity = table.Column<int>(type: "int", nullable: false),
                    RestockQuantity = table.Column<int>(type: "int", nullable: false),
                    WriteOffQuantity = table.Column<int>(type: "int", nullable: false),
                    ConditionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    InspectionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnItems", x => x.Id);
                    table.CheckConstraint("CK_ReturnItem_AcceptedRejected", "[AcceptedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [AcceptedQuantity] + [RejectedQuantity] <= [ReceivedQuantity]");
                    table.CheckConstraint("CK_ReturnItem_ApprovedQuantity", "[ApprovedQuantity] >= 0 AND [ApprovedQuantity] <= [RequestedQuantity]");
                    table.CheckConstraint("CK_ReturnItem_ConditionCode", "[ConditionCode] IS NULL OR [ConditionCode] IN ('Restockable','Damaged','MissingParts','WrongItem','Mixed','WriteOff')");
                    table.CheckConstraint("CK_ReturnItem_Disposition", "[RestockQuantity] >= 0 AND [WriteOffQuantity] >= 0 AND [RestockQuantity] + [WriteOffQuantity] = [AcceptedQuantity]");
                    table.CheckConstraint("CK_ReturnItem_ReceivedQuantity", "[ReceivedQuantity] >= 0 AND [ReceivedQuantity] <= [ApprovedQuantity]");
                    table.CheckConstraint("CK_ReturnItem_RefundAmount", "[RefundAmount] >= 0");
                    table.CheckConstraint("CK_ReturnItem_RequestedQuantity", "[RequestedQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReturnItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnItems_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderItemId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Reserved"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReservedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.CheckConstraint("CK_StockReservation_Expiry", "[ExpiresAt] > [ReservedAt]");
                    table.CheckConstraint("CK_StockReservation_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_StockReservation_Status", "[Status] IN ('Reserved','Committed','Released','Expired')");
                    table.ForeignKey(
                        name: "FK_StockReservations_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockReservations_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Email",
                table: "Accounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedEmail",
                table: "Accounts",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedUsername",
                table: "Accounts",
                column: "NormalizedUsername",
                unique: true,
                filter: "[NormalizedUsername] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Role_IsActive",
                table: "Accounts",
                columns: new[] { "Role", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_CustomerId",
                table: "Addresses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_CustomerId_IsDefault",
                table: "Addresses",
                columns: new[] { "CustomerId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Slug",
                table: "Brands",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId_IsVisible_DisplayOrder",
                table: "Categories",
                columns: new[] { "ParentId", "IsVisible", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AccountId",
                table: "Customers",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_Provider_DeduplicationKey",
                table: "IntegrationInboxEvents",
                columns: new[] { "Provider", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_Provider_ExternalEventId",
                table: "IntegrationInboxEvents",
                columns: new[] { "Provider", "ExternalEventId" },
                filter: "[ExternalEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_Status_ReceivedAt",
                table: "IntegrationInboxEvents",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_AggregateType_AggregateId",
                table: "IntegrationOutboxMessages",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_Provider_IdempotencyKey",
                table: "IntegrationOutboxMessages",
                columns: new[] { "Provider", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_Status_NextAttemptAt",
                table: "IntegrationOutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_IdempotencyKey",
                table: "InventoryMovements",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductVariantId_CreatedAt",
                table: "InventoryMovements",
                columns: new[] { "ProductVariantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ReferenceType_ReferenceId",
                table: "InventoryMovements",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderCancellationItems_CancellationRequestId_OrderItemId",
                table: "OrderCancellationItems",
                columns: new[] { "CancellationRequestId", "OrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCancellationItems_OrderItemId",
                table: "OrderCancellationItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCancellationRequests_IdempotencyKey",
                table: "OrderCancellationRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCancellationRequests_OrderId_Status_RequestedAt",
                table: "OrderCancellationRequests",
                columns: new[] { "OrderId", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_ProductVariantId",
                table: "OrderItems",
                columns: new[] { "OrderId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductVariantId",
                table: "OrderItems",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClientRequestId",
                table: "Orders",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Code",
                table: "Orders",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FulfillmentStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "FulfillmentStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "OrderStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "PaymentStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PublicToken",
                table: "Orders",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_CorrelationId",
                table: "OrderStatusHistories",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_OrderId_OccurredAt",
                table: "OrderStatusHistories",
                columns: new[] { "OrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_IdempotencyKey",
                table: "PaymentTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_OrderId",
                table: "PaymentTransactions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Provider_MerchantReference",
                table: "PaymentTransactions",
                columns: new[] { "Provider", "MerchantReference" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Provider_ProviderTransactionId",
                table: "PaymentTransactions",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true,
                filter: "[ProviderTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status_CreatedAt",
                table: "PaymentTransactions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceCampaignItems_VariantId",
                table: "PriceCampaignItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceCampaigns_ClientRequestId",
                table: "PriceCampaigns",
                column: "ClientRequestId",
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PriceCampaigns_Code",
                table: "PriceCampaigns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceCampaigns_Status_StartDate_EndDate",
                table: "PriceCampaigns",
                columns: new[] { "Status", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceCampaigns_SupersededByCampaignId",
                table: "PriceCampaigns",
                column: "SupersededByCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_CorrelationId",
                table: "PriceHistories",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_ProductVariantId_CreatedAt",
                table: "PriceHistories",
                columns: new[] { "ProductVariantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_ProductId_IsMain",
                table: "ProductImage",
                columns: new[] { "ProductId", "IsMain" },
                unique: true,
                filter: "[IsMain] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPromotions_PromotionId",
                table: "ProductPromotions",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId",
                table: "Products",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_BrandId_IsActive",
                table: "Products",
                columns: new[] { "CategoryId", "BrandId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Slug",
                table: "Products",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Color_Size",
                table: "ProductVariants",
                columns: new[] { "Color", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_CurrentPriceSourceType_CurrentPriceSourceId",
                table: "ProductVariants",
                columns: new[] { "CurrentPriceSourceType", "CurrentPriceSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_IsActive",
                table: "ProductVariants",
                columns: new[] { "ProductId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_SKU",
                table: "ProductVariants",
                column: "SKU",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionCustomers_CustomerId",
                table: "PromotionCustomers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_StartDate_EndDate_IsActive",
                table: "Promotions",
                columns: new[] { "StartDate", "EndDate", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnEvidence_ReturnRequestId_CreatedAt",
                table: "ReturnEvidence",
                columns: new[] { "ReturnRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_IdempotencyKey",
                table: "ReturnInspections",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_ReturnRequestId_CreatedAt",
                table: "ReturnInspections",
                columns: new[] { "ReturnRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_OrderItemId",
                table: "ReturnItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_ReturnRequestId_OrderItemId",
                table: "ReturnItems",
                columns: new[] { "ReturnRequestId", "OrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_Code",
                table: "ReturnRequests",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_IdempotencyKey",
                table: "ReturnRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderId_Status_RequestedAt",
                table: "ReturnRequests",
                columns: new[] { "OrderId", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Direction_Status_CreatedAt",
                table: "Shipments",
                columns: new[] { "Direction", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId_Direction",
                table: "Shipments",
                columns: new[] { "OrderId", "Direction" },
                unique: true,
                filter: "[Direction] = 'Outbound' AND [Status] <> 'Cancelled' AND [Status] <> 'Returned'");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ParentShipmentId",
                table: "Shipments",
                column: "ParentShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Provider_ExternalOrderCode",
                table: "Shipments",
                columns: new[] { "Provider", "ExternalOrderCode" },
                unique: true,
                filter: "[ExternalOrderCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Provider_TrackingCode",
                table: "Shipments",
                columns: new[] { "Provider", "TrackingCode" },
                unique: true,
                filter: "[TrackingCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ReturnRequestId",
                table: "Shipments",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ReturnRequestId_Direction",
                table: "Shipments",
                columns: new[] { "ReturnRequestId", "Direction" },
                unique: true,
                filter: "[Direction] = 'Return' AND [ReturnRequestId] IS NOT NULL AND [Status] <> 'Cancelled' AND [Status] <> 'Returned'");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_IdempotencyKey",
                table: "StockReservations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                table: "StockReservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderItemId",
                table: "StockReservations",
                column: "OrderItemId",
                unique: true,
                filter: "[Status] IN ('Reserved','Committed')");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ProductVariantId",
                table: "StockReservations",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_Status_ExpiresAt",
                table: "StockReservations",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Addresses");

            migrationBuilder.DropTable(
                name: "IntegrationInboxEvents");

            migrationBuilder.DropTable(
                name: "IntegrationOutboxMessages");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "OrderCancellationItems");

            migrationBuilder.DropTable(
                name: "OrderStatusHistories");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "PriceCampaignItems");

            migrationBuilder.DropTable(
                name: "PriceHistories");

            migrationBuilder.DropTable(
                name: "ProductImage");

            migrationBuilder.DropTable(
                name: "ProductPromotions");

            migrationBuilder.DropTable(
                name: "PromotionCustomers");

            migrationBuilder.DropTable(
                name: "ReturnEvidence");

            migrationBuilder.DropTable(
                name: "ReturnInspections");

            migrationBuilder.DropTable(
                name: "ReturnItems");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "StockReservations");

            migrationBuilder.DropTable(
                name: "OrderCancellationRequests");

            migrationBuilder.DropTable(
                name: "PriceCampaigns");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
