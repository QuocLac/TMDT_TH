using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services;
using WebApplication2.Services.Cart;
using WebApplication2.Services.Catalog;
using WebApplication2.Services.Commerce.Cancellations;
using WebApplication2.Services.Commerce.Checkout;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Orders;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Media;
using WebApplication2.Services.Payments.Refunds;
using WebApplication2.Services.Payments.VnPay;
using WebApplication2.Services.Pricing;
using WebApplication2.Services.Reviews;
using WebApplication2.Services.Shipping;
using WebApplication2.Services.Shipping.Ghn;
using WebApplication2.Services.Shipping.Internal;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddSingleton<InternalShipmentProviderInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    options
        .UseSqlServer(connectionString)
        .AddInterceptors(
            serviceProvider.GetRequiredService<InternalShipmentProviderInterceptor>()));

builder.Services.AddAntiforgery(options =>
    options.HeaderName = "RequestVerificationToken");

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<AdminAreaAuthorizationFilter>();
});
builder.Services.AddRazorPages();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".FastBuy.Authentication";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/access-denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = AccountSecurity.AuthenticationLifetime;
        options.EventsType = typeof(AccountCookieEvents);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "AdminOnly",
        policy => policy.RequireRole(nameof(WebApplication2.Models.Enums.AccountRole.Admin)));
});

builder.Services.AddScoped<AdminAreaAuthorizationFilter>();
builder.Services.AddScoped<AccountCookieEvents>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddScoped<IAccountAuthenticationService, AccountAuthenticationService>();
builder.Services.AddScoped<ICustomerAccountService, CustomerAccountService>();

var adminBootstrapSection =
    builder.Configuration.GetSection(AdminBootstrapOptions.SectionName);
var adminBootstrapOptions =
    adminBootstrapSection.Get<AdminBootstrapOptions>() ?? new AdminBootstrapOptions();
var hasInvalidAdminBootstrapConfiguration =
    adminBootstrapOptions.Enabled && !adminBootstrapOptions.IsConfigured;

builder.Services
    .AddOptions<AdminBootstrapOptions>()
    .Bind(adminBootstrapSection);

if (adminBootstrapOptions.Enabled && adminBootstrapOptions.IsConfigured)
{
    builder.Services.AddHostedService<AdminAccountBootstrapper>();
}

builder.Services.Configure<ProductImageStorageOptions>(
    builder.Configuration.GetSection(ProductImageStorageOptions.SectionName));
builder.Services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".FastBuy.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromDays(7);
});

builder.Services.AddScoped<IProductOptionReadService, ProductOptionReadService>();
builder.Services.AddScoped<IProductReviewService, ProductReviewService>();
builder.Services.AddScoped<IProductPublicationService, ProductPublicationService>();
builder.Services.AddScoped<IProductOptionIntegrityService, ProductOptionIntegrityService>();
builder.Services.AddScoped<IProductOptionCombinationGenerator, ProductOptionCombinationGenerator>();
builder.Services.AddScoped<SessionCartService>();
builder.Services.AddScoped<ISessionCartService, DynamicProductOptionCartService>();

builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<CommerceInventoryService>();
builder.Services.AddScoped<IInventoryService>(serviceProvider =>
    serviceProvider.GetRequiredService<CommerceInventoryService>());
builder.Services.AddScoped<ICancellationInventoryService>(serviceProvider =>
    serviceProvider.GetRequiredService<CommerceInventoryService>());
builder.Services.AddScoped<IReturnInventoryService>(serviceProvider =>
    serviceProvider.GetRequiredService<CommerceInventoryService>());
builder.Services.AddScoped<IOrderCancellationService, OrderCancellationService>();
builder.Services.AddScoped<IReturnCodeGenerator, ReturnCodeGenerator>();
builder.Services.AddScoped<ReturnWorkflowService>();
builder.Services.AddScoped<IReturnWorkflowService, CommercialReturnWorkflowService>();
builder.Services.AddScoped<IReturnRefundDestinationService, ReturnRefundDestinationService>();

builder.Services.AddScoped<IShippingFeeCalculator, StandardShippingFeeCalculator>();

builder.Services
    .AddOptions<GhnAddressOptions>()
    .Bind(builder.Configuration.GetSection(GhnAddressOptions.SectionName))
    .Validate(
        options => !options.Enabled || options.IsConfigured,
        $"Configuration section '{GhnAddressOptions.SectionName}' is invalid. "
        + "Configure the HTTPS base URL, token, shop, origin district and origin ward "
        + "before enabling automatic address lookup.")
    .ValidateOnStart();

builder.Services
    .AddOptions<GhnShippingOptions>()
    .Bind(builder.Configuration.GetSection(GhnShippingOptions.SectionName))
    .Validate(
        options => !options.Enabled || options.IsQuoteConfigured,
        $"Configuration section '{GhnShippingOptions.SectionName}' is invalid for shipping quotes.")
    .ValidateOnStart();

static void ConfigureGhnQuoteHttpClient(
    IServiceProvider serviceProvider,
    HttpClient client)
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<GhnAddressOptions>>()
        .Value;

    var baseUrl = Uri.TryCreate(
        options.BaseUrl,
        UriKind.Absolute,
        out var configuredBaseUrl)
        && configuredBaseUrl.Scheme == Uri.UriSchemeHttps
            ? configuredBaseUrl
            : new Uri("https://dev-online-gateway.ghn.vn/");

    client.BaseAddress = new Uri(
        baseUrl.ToString().TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(
        Math.Clamp(options.TimeoutSeconds, 5, 60));
}

builder.Services.AddHttpClient<GhnAddressClient>(
    ConfigureGhnQuoteHttpClient);
builder.Services.AddScoped<
    IGhnAddressClient,
    CommercialShippingAddressClient>();
builder.Services.AddHttpClient<IGhnShippingClient, GhnShippingClient>(
    ConfigureGhnQuoteHttpClient);
builder.Services.AddScoped<
    ICheckoutShippingQuoteService,
    CheckoutShippingQuoteService>();

builder.Services.AddScoped<
    IInternalShippingLifecycleService,
    InternalShippingLifecycleService>();
builder.Services.AddScoped<
    IInternalReturnShippingService,
    InternalReturnShippingService>();
builder.Services.AddScoped<IInternalRefundService, InternalRefundService>();
builder.Services.AddHostedService<InternalShipmentBackfillWorker>();

builder.Services.AddScoped<OrderApplicationService>();
builder.Services.AddScoped<IOrderApplicationService, DynamicProductOptionOrderApplicationService>();
builder.Services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
builder.Services.AddScoped<IOrderWorkflowService, OrderWorkflowService>();

builder.Services
    .AddOptions<VnPayOptions>()
    .Bind(builder.Configuration.GetSection(VnPayOptions.SectionName))
    .Validate(
        options => !options.Enabled || options.IsConfigured,
        $"Configuration section '{VnPayOptions.SectionName}' is invalid. "
        + "When VNPay is enabled, configure HTTPS BaseUrl, PaymentPath, TmnCode, "
        + "HashSecret, ReturnUrl, IpnUrl, payment timeout and expiration worker limits.")
    .ValidateOnStart();

builder.Services.AddSingleton<IVnPayGateway, VnPayGateway>();
builder.Services.AddScoped<IVnPayPaymentService, VnPayPaymentService>();
builder.Services.AddScoped<IRefundSettlementService, RefundSettlementService>();
builder.Services.AddHostedService<VnPayPaymentExpirationWorker>();

builder.Services.AddScoped<EffectivePriceService>();
builder.Services.AddScoped<IEffectivePriceService, ReliableEffectivePriceService>();
builder.Services.AddScoped<PriceCampaignWorkflowService>();
builder.Services.AddScoped<IPriceCampaignWorkflowService, ReliablePriceCampaignWorkflowService>();
builder.Services.AddHostedService<PriceCampaignWorker>();

var app = builder.Build();

if (hasInvalidAdminBootstrapConfiguration)
{
    app.Logger.LogError(
        "Bootstrap Admin is enabled but its configuration is incomplete. "
        + "The bootstrapper was skipped so the application can start. "
        + "Configure Username, Email, FullName, PhoneNumber and a Password of at least 12 characters, "
        + "or set Authentication:BootstrapAdmin:Enabled to false.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.Run();

public partial class Program;
