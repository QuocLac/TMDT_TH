using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using System.Linq;

namespace WebApplication2.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PriceHistoriesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PriceHistoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admin/PriceHistories
        // Thêm tham số productId (có thể null) để lọc dữ liệu
        public async Task<IActionResult> Index(int? productId)
        {
            var query = _context.PriceHistories
                .Include(h => h.ProductVariant)
                    .ThenInclude(v => v.Product)
                .AsQueryable();

            // Nếu người dùng click từ trang Sản phẩm truyền ID sang
            if (productId.HasValue)
            {
                query = query.Where(h => h.ProductVariant.ProductId == productId.Value);

                // Truyền tên sản phẩm ra View để hiển thị tiêu đề cho đẹp
                ViewBag.FilterProductName = await _context.Products
                    .Where(p => p.Id == productId.Value)
                    .Select(p => p.Name)
                    .FirstOrDefaultAsync();
            }

            var histories = await query.OrderByDescending(h => h.CreatedAt).ToListAsync();
            return View(histories);
        }
    }
}