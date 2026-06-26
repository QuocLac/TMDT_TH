using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using WebApplication2.Models;
using Microsoft.EntityFrameworkCore;

namespace WebApplication2.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admin/Categories
        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories
                .Include(c => c.Parent)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // Truyền danh sách để làm options cho Dropdown "Danh mục cha" trong Modal
            ViewBag.ParentCategories = categories;
            return View(categories);
        }

        // GET API: Lấy thông tin 1 danh mục để đổ vào Modal (Dùng cho chức năng Sửa)
        [HttpGet]
        public async Task<IActionResult> GetCategory(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null) return NotFound(new { success = false, message = "Không tìm thấy danh mục" });

            return Json(new { success = true, data = category });
        }

        // POST API: Lưu dữ liệu (Dùng chung cho cả Thêm mới và Sửa)
        [HttpPost]
        public async Task<IActionResult> SaveCategory([FromBody] Category model)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Slug))
            {
                return Json(new { success = false, message = "Tên và Slug không được để trống!" });
            }

            // Kiểm tra trùng Slug (loại trừ chính nó nếu đang sửa)
            bool slugExists = await _context.Categories
                .AnyAsync(c => c.Slug == model.Slug && c.Id != model.Id);

            if (slugExists)
            {
                return Json(new { success = false, message = "Đường dẫn SEO (Slug) đã tồn tại!" });
            }

            try
            {
                if (model.Id == 0) // THÊM MỚI
                {
                    _context.Categories.Add(model);
                }
                else // CẬP NHẬT
                {
                    var existing = await _context.Categories.FindAsync(model.Id);
                    if (existing == null) return Json(new { success = false, message = "Dữ liệu không tồn tại!" });

                    existing.Name = model.Name;
                    existing.ParentId = model.ParentId;
                    existing.Slug = model.Slug;
                    existing.MetaTitle = model.MetaTitle;
                    existing.MetaDescription = model.MetaDescription;
                    // UpdatedAt sẽ tự động cập nhật nếu bạn ghi đè phương thức SaveChanges, hoặc gán tay:
                    existing.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Lưu danh mục thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }
}