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

        public async Task<IActionResult> Index()
        {
            var products = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand) // Bổ sung Load Brand
                .Include(p => p.Images)
                .Include(p => p.Variants) // Bổ sung Load Biến thể
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return View(products);
        }

        public async Task<IActionResult> Details(int id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Include(p => p.ProductPromotions)
                    .ThenInclude(pp => pp.Promotion)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();

            return View(product);
        }

        public async Task<IActionResult> Create()
        {
            ViewData["CategoryId"] = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name");
            // Truyền dữ liệu Nhãn hàng ra form
            ViewData["BrandId"] = new SelectList(await _context.Brands.Where(b => b.IsActive).ToListAsync(), "Id", "Name");

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
                    var product = new Product
                    {
                        Name = vm.Name,
                        Slug = vm.Slug,
                        Description = vm.Description ?? "",
                        CategoryId = vm.CategoryId,
                        BrandId = vm.BrandId, // BỔ SUNG THUỘC TÍNH NHÃN HÀNG
                        IsActive = vm.IsActive,
                        MetaTitle = vm.MetaTitle ?? "",
                        MetaDescription = vm.MetaDescription ?? "",
                        MetaKeywords = "",
                        Variants = new List<ProductVariant>(),
                        ProductPromotions = new List<ProductPromotion>(),
                        Images = new List<ProductImage>()
                    };

                    if (vm.MainImage != null)
                        product.Images.Add(new ProductImage { ImageUrl = await UploadImageAsync(vm.MainImage), IsMain = true });

                    if (vm.GalleryImages != null && vm.GalleryImages.Any())
                    {
                        foreach (var file in vm.GalleryImages)
                            product.Images.Add(new ProductImage { ImageUrl = await UploadImageAsync(file), IsMain = false });
                    }

                    string variantImageUrl = vm.VariantImage != null ? await UploadImageAsync(vm.VariantImage) : "";

                    product.Variants.Add(new ProductVariant
                    {
                        SKU = vm.SKU,
                        Price = vm.Price,
                        CurrentPrice = vm.Price, // BỔ SUNG THUỘC TÍNH: Giá hiện tại bằng giá niêm yết
                        StockQuantity = vm.StockQuantity,
                        Color = vm.Color ?? "",
                        Size = vm.Size ?? "",
                        ImageUrl = variantImageUrl
                    });

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
            ViewData["BrandId"] = new SelectList(await _context.Brands.Where(b => b.IsActive).ToListAsync(), "Id", "Name", vm.BrandId);
            ViewBag.ActivePromotions = await _context.Promotions.Where(p => p.IsActive && p.EndDate >= DateTime.UtcNow).ToListAsync();
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm!" });

            product.IsActive = !product.IsActive;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            string statusText = product.IsActive ? "đã được HIỆN" : "đã bị ẨN";
            return Json(new { success = true, isActive = product.IsActive, message = $"Sản phẩm '{product.Name}' {statusText} thành công!" });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleVariantStatus(int id)
        {
            var variant = await _context.ProductVariants.FindAsync(id);
            if (variant == null) return Json(new { success = false, message = "Không tìm thấy biến thể!" });

            variant.IsActive = !variant.IsActive;
            variant.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Json(new { success = true, isActive = variant.IsActive });
        }

        public class UpdatePriceRequest
        {
            public int VariantId { get; set; }
            public decimal NewPrice { get; set; }
            public string Note { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateVariantPrice([FromBody] UpdatePriceRequest request)
        {
            if (request.NewPrice <= 0) return Json(new { success = false, message = "Giá mới phải lớn hơn 0!" });

            var variant = await _context.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == request.VariantId);
            if (variant == null) return Json(new { success = false, message = "Không tìm thấy phiên bản sản phẩm!" });

            if (variant.Price == request.NewPrice) return Json(new { success = false, message = "Giá mới không có sự thay đổi so với giá hiện tại!" });

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
                variant.CurrentPrice = request.NewPrice; // ĐỒNG BỘ: Cập nhật luôn giá hiện hành

                _context.PriceHistories.Add(history);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Đã cập nhật giá mới cho SKU {variant.SKU} thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        public class QuickVariantDto
        {
            public int VariantId { get; set; }
            public int ProductId { get; set; }
            public string SKU { get; set; }
            public string? Color { get; set; }
            public string? Size { get; set; }
            public decimal Price { get; set; }
            public int StockQuantity { get; set; }
            public IFormFile? VariantImage { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveQuickVariant([FromForm] QuickVariantDto request)
        {
            if (request.Price <= 0 || string.IsNullOrWhiteSpace(request.SKU))
                return Json(new { success = false, message = "SKU và Giá bán không hợp lệ!" });

            try
            {
                string newImageUrl = null;
                if (request.VariantImage != null) newImageUrl = await UploadImageAsync(request.VariantImage);

                if (request.VariantId == 0)
                {
                    if (await _context.ProductVariants.AnyAsync(v => v.SKU == request.SKU))
                        return Json(new { success = false, message = "Mã SKU này đã tồn tại!" });

                    var variant = new ProductVariant
                    {
                        ProductId = request.ProductId,
                        SKU = request.SKU,
                        Color = request.Color ?? "",
                        Size = request.Size ?? "",
                        Price = request.Price,
                        CurrentPrice = request.Price, // BỔ SUNG THUỘC TÍNH: Giá hiện tại
                        StockQuantity = request.StockQuantity,
                        ImageUrl = newImageUrl ?? ""
                    };
                    _context.ProductVariants.Add(variant);
                }
                else
                {
                    var variant = await _context.ProductVariants.FindAsync(request.VariantId);
                    if (variant == null) return Json(new { success = false, message = "Không tìm thấy biến thể!" });

                    variant.Color = request.Color ?? "";
                    variant.Size = request.Size ?? "";
                    variant.Price = request.Price;
                    variant.CurrentPrice = request.Price; // ĐỒNG BỘ GIÁ
                    variant.StockQuantity = request.StockQuantity;

                    if (newImageUrl != null) variant.ImageUrl = newImageUrl;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Lưu dữ liệu thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        // ==========================================
        // 6. EDIT - CẬP NHẬT THÔNG TIN SẢN PHẨM CHÍNH
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var product = await _context.Products
                .Include(p => p.Images)
                .Include(p => p.ProductPromotions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();

            var vm = new ProductEditViewModel
            {
                Id = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                Description = product.Description,
                CategoryId = product.CategoryId,
                BrandId = product.BrandId,
                IsActive = product.IsActive,
                MetaTitle = product.MetaTitle,
                MetaDescription = product.MetaDescription,
                CurrentMainImage = product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl,
                CurrentGalleryImages = product.Images.Where(i => !i.IsMain).ToList(),
                SelectedPromotionIds = product.ProductPromotions.Select(pp => pp.PromotionId).ToList()
            };

            ViewData["CategoryId"] = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", product.CategoryId);
            ViewData["BrandId"] = new SelectList(await _context.Brands.Where(b => b.IsActive).ToListAsync(), "Id", "Name", product.BrandId);
            ViewBag.ActivePromotions = await _context.Promotions.Where(p => p.IsActive && p.EndDate >= DateTime.UtcNow).ToListAsync();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductEditViewModel vm)
        {
            if (id != vm.Id) return NotFound();

            if (ModelState.IsValid)
            {
                // Kiểm tra trùng Slug với sản phẩm KHÁC
                if (await _context.Products.AnyAsync(p => p.Slug == vm.Slug && p.Id != id))
                {
                    ModelState.AddModelError("Slug", "Đường dẫn SEO này đã tồn tại trên sản phẩm khác!");
                }
                else
                {
                    var product = await _context.Products
                        .Include(p => p.Images)
                        .Include(p => p.ProductPromotions)
                        .FirstOrDefaultAsync(p => p.Id == id);

                    if (product == null) return NotFound();

                    // 1. Cập nhật thông tin cơ bản
                    product.Name = vm.Name;
                    product.Slug = vm.Slug;
                    product.Description = vm.Description ?? "";
                    product.CategoryId = vm.CategoryId;
                    product.BrandId = vm.BrandId;
                    product.IsActive = vm.IsActive;
                    product.MetaTitle = vm.MetaTitle ?? "";
                    product.MetaDescription = vm.MetaDescription ?? "";
                    product.UpdatedAt = DateTime.UtcNow;

                    // 2. Xử lý Ảnh đại diện (Ghi đè nếu có ảnh mới)
                    if (vm.MainImage != null)
                    {
                        var mainImg = product.Images.FirstOrDefault(i => i.IsMain);
                        if (mainImg != null)
                        {
                            mainImg.ImageUrl = await UploadImageAsync(vm.MainImage);
                        }
                        else
                        {
                            product.Images.Add(new ProductImage { ImageUrl = await UploadImageAsync(vm.MainImage), IsMain = true });
                        }
                    }

                    // 3. Xử lý Ảnh phụ (Thêm mới vào bộ sưu tập hiện có)
                    if (vm.GalleryImages != null && vm.GalleryImages.Any())
                    {
                        foreach (var file in vm.GalleryImages)
                        {
                            product.Images.Add(new ProductImage { ImageUrl = await UploadImageAsync(file), IsMain = false });
                        }
                    }

                    // 4. Cập nhật Khuyến mãi
                    _context.ProductPromotions.RemoveRange(product.ProductPromotions);
                    if (vm.SelectedPromotionIds != null && vm.SelectedPromotionIds.Any())
                    {
                        foreach (var promoId in vm.SelectedPromotionIds)
                        {
                            product.ProductPromotions.Add(new ProductPromotion { PromotionId = promoId, ProductId = product.Id });
                        }
                    }

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Cập nhật sản phẩm thành công!";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Nếu lỗi form, load lại Data
            ViewData["CategoryId"] = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", vm.CategoryId);
            ViewData["BrandId"] = new SelectList(await _context.Brands.Where(b => b.IsActive).ToListAsync(), "Id", "Name", vm.BrandId);
            ViewBag.ActivePromotions = await _context.Promotions.Where(p => p.IsActive && p.EndDate >= DateTime.UtcNow).ToListAsync();
            vm.CurrentGalleryImages = await _context.Set<ProductImage>().Where(i => i.ProductId == id && !i.IsMain).ToListAsync();
            return View(vm);
        }

        // ==========================================
        // 7. API: XÓA ẢNH PHỤ (AJAX)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> DeleteProductImage(int imageId)
        {
            var image = await _context.Set<ProductImage>().FindAsync(imageId);
            if (image == null || image.IsMain)
            {
                return Json(new { success = false, message = "Không tìm thấy ảnh hoặc đây là ảnh đại diện!" });
            }

            _context.Set<ProductImage>().Remove(image);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Xóa ảnh thành công!" });
        }
    }
}