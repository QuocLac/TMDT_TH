using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Cart;
using WebApplication2.ViewModels.Storefront.Cart;

namespace WebApplication2.Controllers;

[Route("cart")]
public sealed class CartController : Controller
{
    private readonly ISessionCartService _cartService;

    public CartController(ISessionCartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var model = await _cartService.GetCartAsync(
            cancellationToken);

        return View(model);
    }

    [HttpGet("product-options/{productId:int}")]
    public async Task<IActionResult> ProductOptions(
        int productId,
        CancellationToken cancellationToken)
    {
        if (productId <= 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Mã sản phẩm không hợp lệ.",
                errorCode = "INVALID_PRODUCT_ID"
            });
        }

        var model = await _cartService.GetProductOptionsAsync(
            productId,
            cancellationToken);

        if (model is null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy sản phẩm đang hoạt động.",
                errorCode = "PRODUCT_NOT_FOUND"
            });
        }

        return Json(new
        {
            success = true,
            data = model
        });
    }

    [HttpPost("add")]
    public async Task<IActionResult> Add(
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        return Json(
            await _cartService.AddAsync(
                request,
                cancellationToken));
    }

    [HttpPost("quantity")]
    public async Task<IActionResult> UpdateQuantity(
        [FromBody] UpdateCartQuantityRequest request,
        CancellationToken cancellationToken)
    {
        return Json(
            await _cartService.UpdateQuantityAsync(
                request,
                cancellationToken));
    }

    [HttpPost("selection")]
    public async Task<IActionResult> SetSelection(
        [FromBody] SetCartSelectionRequest request,
        CancellationToken cancellationToken)
    {
        return Json(
            await _cartService.SetSelectionAsync(
                request,
                cancellationToken));
    }

    [HttpPost("selection/all")]
    public async Task<IActionResult> SetAllSelection(
        [FromBody] SetAllCartSelectionRequest request,
        CancellationToken cancellationToken)
    {
        return Json(
            await _cartService.SetAllSelectionAsync(
                request,
                cancellationToken));
    }

    [HttpPost("remove")]
    public async Task<IActionResult> Remove(
        [FromBody] RemoveCartItemRequest request,
        CancellationToken cancellationToken)
    {
        return Json(
            await _cartService.RemoveAsync(
                request,
                cancellationToken));
    }

    [HttpPost("prepare-checkout")]
    public async Task<IActionResult> PrepareCheckout(
        CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(
            cancellationToken);

        if (!cart.HasSelectedItems)
        {
            return Json(
                CartOperationResult.Failure(
                    "Hãy chọn ít nhất một sản phẩm còn hàng trước khi thanh toán.",
                    "NO_SELECTED_ITEMS",
                    cart));
        }

        if (cart.Items.Any(item =>
                item.IsSelected
                && !item.CanSelect))
        {
            return Json(
                CartOperationResult.Failure(
                    "Có sản phẩm không còn đủ điều kiện mua. Hãy kiểm tra lại giỏ hàng.",
                    "SELECTED_ITEM_INVALID",
                    cart));
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return Json(
                CartOperationResult.Ok(
                    "Đăng nhập để tiếp tục thanh toán và theo dõi đơn hàng.",
                    cart,
                    "/account/login?returnUrl=%2Fcheckout"));
        }

        return Json(
            CartOperationResult.Ok(
                "Giỏ hàng đã sẵn sàng để thanh toán.",
                cart,
                "/checkout"));
    }
}
