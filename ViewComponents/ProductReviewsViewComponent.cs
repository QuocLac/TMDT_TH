using Microsoft.AspNetCore.Mvc;
using WebApplication2.Models;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Reviews;

namespace WebApplication2.ViewComponents;

public sealed class ProductReviewsViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public ProductReviewsViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync(int productId)
    {
        var feed = await ProductReviewExperienceQuery.GetPageAsync(
            _context,
            productId,
            page: 1,
            pageSize: ProductReviewTransparencyPolicy.InitialPageSize,
            rating: null,
            mediaOnly: false,
            User.GetCustomerId(),
            HttpContext.RequestAborted);

        return feed is null
            ? Content(string.Empty)
            : View(new ProductReviewExperienceComponentViewModel
            {
                Feed = feed
            });
    }
}
