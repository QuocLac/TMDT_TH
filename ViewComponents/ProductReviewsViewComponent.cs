using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Reviews;
using WebApplication2.ViewModels.Storefront.Reviews;

namespace WebApplication2.ViewComponents;

public sealed class ProductReviewsViewComponent : ViewComponent
{
    private readonly IProductReviewService _reviews;

    public ProductReviewsViewComponent(IProductReviewService reviews)
    {
        _reviews = reviews;
    }

    public async Task<IViewComponentResult> InvokeAsync(int productId)
    {
        return View(new ProductReviewComponentViewModel
        {
            Summary = await _reviews.GetProductSummaryAsync(
                productId,
                take: 8,
                HttpContext.RequestAborted)
        });
    }
}
