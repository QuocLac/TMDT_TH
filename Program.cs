using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services;
using WebApplication2.Services.Media;
using WebApplication2.Services.Pricing;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddAntiforgery(options =>
    options.HeaderName = "RequestVerificationToken");

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddRazorPages();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ProductImageStorageOptions>(
    builder.Configuration.GetSection(ProductImageStorageOptions.SectionName));
builder.Services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();

// EffectivePriceService gốc giữ trách nhiệm preview/validation.
// ReliableEffectivePriceService thay riêng phần projection CurrentPrice/PriceHistory.
builder.Services.AddScoped<EffectivePriceService>();
builder.Services.AddScoped<
    IEffectivePriceService,
    ReliableEffectivePriceService>();

// Giữ implementation gốc cho SaveDraft/Activate/Cancel/Recover.
// ReliablePriceCampaignWorkflowService điều phối pipeline ConfirmDraft.
builder.Services.AddScoped<PriceCampaignWorkflowService>();
builder.Services.AddScoped<
    IPriceCampaignWorkflowService,
    ReliablePriceCampaignWorkflowService>();

builder.Services.AddHostedService<PriceCampaignWorker>();

var app = builder.Build();

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
