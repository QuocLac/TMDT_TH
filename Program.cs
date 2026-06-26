using Microsoft.EntityFrameworkCore;
using WebApplication2.Models; // Đảm bảo gọi namespace chứa ApplicationDbContext

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình DbContext kết nối SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Đăng ký dịch vụ cho mô hình MVC (BẮT BUỘC CHO CONTROLLER & VIEW)
builder.Services.AddControllersWithViews();

// (Tùy chọn) Đăng ký thêm Razor Pages nếu bạn có dùng kết hợp
builder.Services.AddRazorPages();

var app = builder.Build();

// 3. Cấu hình HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // Đổi từ MapStaticAssets sang UseStaticFiles (chuẩn của MVC)

app.UseRouting();
app.UseAuthorization();

// 4. Cấu hình Đường dẫn (Routing)
// Route cho các Area (như Admin)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// Route mặc định (Trang chủ)
app.MapControllerRoute(
    name: "default",
    pattern: "{area=Admin}/{controller=Products}/{action=Index}/{id?}");
// Tôi đã chỉnh lại route mặc định về chuẩn. 
// Nếu bạn muốn mở web lên là tự động nhảy vào Admin/Products luôn thì đổi lại thành: pattern: "{area=Admin}/{controller=Products}/{action=Index}/{id?}"

app.MapRazorPages();

app.Run();