using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Hosting;
using WebApplication2.Models;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebApplication2.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductsController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // ==========================================
        // 1. INDEX - DANH SÁCH SẢN PHẨM
        // ==========================================
        public async Task<IActionResult> Index()
        {
            var products = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Images)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return View(products);
        }

        // ==========================================
        // 2. DETAILS - CHI TIẾT SẢN PHẨM
        // ==========================================
        public async Task<IActionResult> Details(int id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Include(p => p.ProductPromotions)
                    .ThenInclude(pp => pp.Promotion)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();

            return View(product);
        }

        // ==========================================
        // 3. CREATE - THÊM MỚI SẢN PHẨM
        // ==========================================
        public async Task<IActionResult> Create()
        {
            ViewData["CategoryId"] = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name");
            ViewBag.ActivePromotions = await _context.Promotions
                .Where(p => p.IsActive && p.EndDate >= DateTime.UtcNow).ToListAsync();

            return View(new ProductCreateViewModel());
        }

        private async Task<string> UploadImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0) return null;
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "products");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }
            return "/uploads/products/" + uniqueFileName;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCreateViewModel vm)
        {
            if (ModelState.IsValid)
            {
                if (await _context.Products.AnyAsync(p => p.Slug == vm.Slug))
                {
                    ModelState.AddModelError("Slug", "Đường dẫn SEO này đã tồn tại!");
                }
                else if (await _context.ProductVariants.AnyAsync(v => v.SKU == vm.SKU))
                {
                    ModelState.AddModelError("SKU", "Mã SKU này đã tồn tại trên hệ thống!");
                }
                else
                {
                    // BỌC LÓT DỮ LIỆU TÙY CHỌN TRÁNH LỖI NULL CỦA SQL SERVER
                    var product = new Product
                    {
                        Name = vm.Name,
                        Slug = vm.Slug,
                        Description = vm.Description ?? "",
                        CategoryId = vm.CategoryId,
                        IsActive = vm.IsActive,
                        MetaTitle = vm.MetaTitle ?? "",
                        MetaDescription = vm.MetaDescription ?? "",
                        MetaKeywords = "", // Gán rỗng mặc định vì trên UI chưa có ô nhập
                        Variants = new List<ProductVariant>(),
                        ProductPromotions = new List<ProductPromotion>(),
                        Images = new List<ProductImage>()
                    };

                    // Xử lý ảnh chính
                    if (vm.MainImage != null)
                    {
                        product.Images.Add(new ProductImage { ImageUrl = await UploadImageAsync(vm.MainImage), IsMain = true });
                    }

                    // Xử lý bộ sưu tập
                    if (vm.GalleryImages != null && vm.GalleryImages.Any())
                    {
                        foreach (var file in vm.GalleryImages)
                        {
                            product.Images.Add(new ProductImage { ImageUrl = await UploadImageAsync(file), IsMain = false });
                        }
                    }

                    // Xử lý biến thể
                    string variantImageUrl = vm.VariantImage != null ? await UploadImageAsync(vm.VariantImage) : null;
                    product.Variants.Add(new ProductVariant
                    {
                        SKU = vm.SKU,
                        Price = vm.Price,
                        StockQuantity = vm.StockQuantity,
                        Color = vm.Color ?? "", // Tránh lỗi null nếu không nhập
                        Size = vm.Size ?? "",   // Tránh lỗi null nếu không nhập
                        ImageUrl = variantImageUrl
                    });

                    // Xử lý Khuyến mãi
                    if (vm.SelectedPromotionIds != null && vm.SelectedPromotionIds.Any())
                    {
                        foreach (var promoId in vm.SelectedPromotionIds)
                            product.ProductPromotions.Add(new ProductPromotion { PromotionId = promoId });
                    }

                    _context.Add(product);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Thêm sản phẩm thành công!";
                    return RedirectToAction(nameof(Index));
                }
            }

            ViewData["CategoryId"] = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", vm.CategoryId);
            ViewBag.ActivePromotions = await _context.Promotions.Where(p => p.IsActive && p.EndDate >= DateTime.UtcNow).ToListAsync();
            return View(vm);
        }

        // ==========================================
        // 4. API: ẨN/HIỆN SẢN PHẨM (TOGGLE STATUS)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return Json(new { success = false, message = "Không tìm thấy sản phẩm!" });
            }

            product.IsActive = !product.IsActive;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            string statusText = product.IsActive ? "đã được HIỆN" : "đã bị ẨN";
            return Json(new
            {
                success = true,
                isActive = product.IsActive,
                message = $"Sản phẩm '{product.Name}' {statusText} thành công!"
            });
        }

        // DTO nhận dữ liệu từ Client
        public class UpdatePriceRequest
        {
            public int VariantId { get; set; }
            public decimal NewPrice { get; set; }
            public string Note { get; set; }
        }

        // API Xử lý thay đổi giá thị trường
        [HttpPost]
        public async Task<IActionResult> UpdateVariantPrice([FromBody] UpdatePriceRequest request)
        {
            if (request.NewPrice <= 0)
                return Json(new { success = false, message = "Giá mới phải lớn hơn 0!" });

            var variant = await _context.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == request.VariantId);

            if (variant == null)
                return Json(new { success = false, message = "Không tìm thấy phiên bản sản phẩm!" });

            if (variant.Price == request.NewPrice)
                return Json(new { success = false, message = "Giá mới không có sự thay đổi so với giá hiện tại!" });

            try
            {
                var history = new PriceHistory
                {
                    ProductVariantId = variant.Id,
                    OldPrice = variant.Price,
                    NewPrice = request.NewPrice,
                    ChangedBy = "Admin",
                    Note = string.IsNullOrWhiteSpace(request.Note) ? "Cập nhật giá thủ công" : request.Note,
                    CreatedAt = DateTime.UtcNow
                };

                variant.Price = request.NewPrice;

                _context.PriceHistories.Add(history);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Đã cập nhật giá mới cho SKU {variant.SKU} thành công!"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }
}