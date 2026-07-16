using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services.Cart;
using WebApplication2.Services.Commerce.Checkout;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Orders;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Shipping.Ghn;
using WebApplication2.ViewModels.Storefront.Cart;
using WebApplication2.ViewModels.Storefront.Checkout;

namespace WebApplication2.Controllers;

[Authorize]
[Route("checkout")]
public sealed class CheckoutController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ISessionCartService _cartService;
    private readonly IGhnAddressClient _addressClient;
    private readonly IShippingFeeCalculator _shippingFeeCalculator;
    private readonly IOrderApplicationService _orderApplicationService;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        ApplicationDbContext context,
        ISessionCartService cartService,
        IGhnAddressClient addressClient,
        IShippingFeeCalculator shippingFeeCalculator,
        IOrderApplicationService orderApplicationService,
        ILogger<CheckoutController> logger)
    {
        _context = context;
        _cartService = cartService;
        _addressClient = addressClient;
        _shippingFeeCalculator = shippingFeeCalculator;
        _orderApplicationService = orderApplicationService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);
        if (!cart.HasSelectedItems)
        {
            return RedirectToAction("Index", "Cart");
        }

        var cartVersion = _cartService.GetCartVersion();
        var clientRequestId = _cartService.GetOrCreateCheckoutClientRequestId(cartVersion);
        var model = await BuildPageModelAsync(
            cart,
            cartVersion,
            clientRequestId,
            form: null,
            errorMessage: null,
            cancellationToken);

        return View(model);
    }

    [HttpPost("place-order")]
    public async Task<IActionResult> PlaceOrder(
        CheckoutPlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        var existing = await _orderApplicationService.FindByClientRequestIdAsync(
            request.ClientRequestId,
            customerId,
            cancellationToken);

        if (existing is not null)
        {
            return RedirectToAction(nameof(Success), new { publicToken = existing.PublicToken });
        }

        var cart = await _cartService.GetCartAsync(cancellationToken);
        var cartVersion = _cartService.GetCartVersion();
        var selectedItems = cart.Items
            .Where(item => item.IsSelected && item.CanSelect)
            .OrderBy(item => item.VariantId)
            .ToArray();

        if (!cart.HasSelectedItems)
        {
            ModelState.AddModelError(string.Empty, "Giỏ hàng không còn sản phẩm được chọn.");
        }

        if (request.CartVersion != cartVersion)
        {
            ModelState.AddModelError(
                string.Empty,
                "Giỏ hàng đã thay đổi sau khi mở trang thanh toán. Vui lòng kiểm tra lại.");
        }

        if (!MatchesCart(request.Items, selectedItems))
        {
            ModelState.AddModelError(
                string.Empty,
                "Danh sách sản phẩm xác nhận không khớp giỏ hàng hiện tại.");
        }

        GhnAddressValidationResult? addressValidation = null;
        if (ModelState.IsValid)
        {
            addressValidation = await _addressClient.ValidateAddressAsync(
                request.ProvinceId,
                request.DistrictId,
                request.WardCode,
                cancellationToken);

            if (!addressValidation.IsValid)
            {
                ModelState.AddModelError(
                    string.Empty,
                    addressValidation.ErrorMessage ?? "Địa chỉ giao hàng không hợp lệ.");
            }
        }

        if (!ModelState.IsValid || addressValidation is null || !addressValidation.IsValid)
        {
            var invalidModel = await BuildPageModelAsync(
                cart,
                cartVersion,
                request.ClientRequestId,
                request,
                FirstModelError(),
                cancellationToken);
            return View("Index", invalidModel);
        }

        try
        {
            var result = await _orderApplicationService.PlaceOrderAsync(
                new PlaceOrderCommand(
                    customerId,
                    request.ClientRequestId,
                    request.FullName,
                    request.Email,
                    request.Phone,
                    request.AddressLine,
                    addressValidation.Province!.ProvinceId,
                    addressValidation.Province.ProvinceName,
                    addressValidation.District!.DistrictId,
                    addressValidation.District.DistrictName,
                    addressValidation.Ward!.WardCode,
                    addressValidation.Ward.WardName,
                    request.PaymentMethod,
                    request.MockPaymentOutcome,
                    selectedItems.Select(item => new PlaceOrderLine(
                        item.VariantId,
                        item.Quantity,
                        item.EffectivePrice)).ToArray()),
                cancellationToken);

            if (result.ShouldClearPurchasedItems)
            {
                try
                {
                    _cartService.CompleteCheckout(
                        result.PurchasedVariantIds,
                        request.ClientRequestId);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Order {OrderCode} was committed but purchased cart lines could not be removed.",
                        result.OrderCode);
                }
            }

            return RedirectToAction(nameof(Success), new { publicToken = result.PublicToken });
        }
        catch (Exception exception) when (exception is
                   CheckoutValidationException
                   or CheckoutConflictException
                   or InventoryValidationException
                   or InventoryConflictException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var failedModel = await BuildPageModelAsync(
                cart,
                cartVersion,
                request.ClientRequestId,
                request,
                exception.Message,
                cancellationToken);
            return View("Index", failedModel);
        }
    }

    [HttpGet("success/{publicToken:guid}")]
    public async Task<IActionResult> Success(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        var receipt = await _orderApplicationService.GetReceiptAsync(
            publicToken,
            RequireCustomerId(),
            cancellationToken);

        if (receipt is null)
        {
            return NotFound();
        }

        return View(CheckoutSuccessViewModel.FromReceipt(receipt));
    }

    [HttpGet("provinces")]
    public async Task<IActionResult> Provinces(CancellationToken cancellationToken)
    {
        var result = await _addressClient.GetProvincesAsync(cancellationToken);
        return LookupResponse(result, items => items
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.ProvinceName)
            .Select(item => new
            {
                id = item.ProvinceId,
                name = item.ProvinceName,
                code = item.Code
            }));
    }

    [HttpGet("districts/{provinceId:int}")]
    public async Task<IActionResult> Districts(
        int provinceId,
        CancellationToken cancellationToken)
    {
        var result = await _addressClient.GetDistrictsAsync(provinceId, cancellationToken);
        return LookupResponse(result, items => items
            .Where(item => item.IsEnabled && item.ProvinceId == provinceId)
            .OrderBy(item => item.DistrictName)
            .Select(item => new
            {
                id = item.DistrictId,
                provinceId = item.ProvinceId,
                name = item.DistrictName,
                supportType = item.SupportType
            }));
    }

    [HttpGet("wards/{districtId:int}")]
    public async Task<IActionResult> Wards(
        int districtId,
        CancellationToken cancellationToken)
    {
        var result = await _addressClient.GetWardsAsync(districtId, cancellationToken);
        return LookupResponse(result, items => items
            .Where(item => item.IsEnabled && item.DistrictId == districtId)
            .OrderBy(item => item.WardName)
            .Select(item => new
            {
                code = item.WardCode,
                districtId = item.DistrictId,
                name = item.WardName
            }));
    }

    [HttpPost("validate-address")]
    public async Task<IActionResult> ValidateAddress(
        [FromBody] CheckoutAddressValidationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "INVALID_ADDRESS_INPUT",
                message = "Vui lòng chọn đầy đủ tỉnh/thành phố, quận/huyện và phường/xã."
            });
        }

        var validation = await _addressClient.ValidateAddressAsync(
            request.ProvinceId,
            request.DistrictId,
            request.WardCode,
            cancellationToken);

        if (!validation.IsValid)
        {
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = validation.ErrorCode,
                message = validation.ErrorMessage
            });
        }

        return Json(new
        {
            success = true,
            data = new CheckoutAddressValidationViewModel
            {
                IsValid = true,
                Message = "Địa chỉ giao hàng hợp lệ.",
                ProvinceId = validation.Province!.ProvinceId,
                ProvinceName = validation.Province.ProvinceName,
                DistrictId = validation.District!.DistrictId,
                DistrictName = validation.District.DistrictName,
                WardCode = validation.Ward!.WardCode,
                WardName = validation.Ward.WardName
            }
        });
    }

    private async Task<CheckoutPageViewModel> BuildPageModelAsync(
        CartPageViewModel cart,
        long cartVersion,
        string clientRequestId,
        CheckoutPlaceOrderRequest? form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var provinces = await _addressClient.GetProvincesAsync(cancellationToken);
        var provinceOptions = provinces.Success && provinces.Data is not null
            ? provinces.Data
                .Where(item => item.IsEnabled)
                .OrderBy(item => item.ProvinceName)
                .Select(item => new CheckoutProvinceOption(
                    item.ProvinceId,
                    item.ProvinceName,
                    item.Code))
                .ToArray()
            : Array.Empty<CheckoutProvinceOption>();

        var selectedQuantity = cart.Items
            .Where(item => item.IsSelected && item.CanSelect)
            .Sum(item => item.Quantity);

        var shippingFee = selectedQuantity > 0
            ? _shippingFeeCalculator.Calculate(cart.SelectedSubtotal, selectedQuantity)
            : 0m;

        form ??= await BuildCustomerPrefillAsync(
            cart,
            cartVersion,
            clientRequestId,
            cancellationToken);

        return CheckoutPageViewModel.FromCart(
            cart,
            cartVersion,
            clientRequestId,
            shippingFee,
            provinceOptions,
            form,
            provinces.Success ? null : provinces.Message,
            errorMessage);
    }

    private async Task<CheckoutPlaceOrderRequest> BuildCustomerPrefillAsync(
        CartPageViewModel cart,
        long cartVersion,
        string clientRequestId,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        var customer = await _context.Customers
            .AsNoTracking()
            .Include(item => item.Account)
            .Include(item => item.Addresses)
            .SingleOrDefaultAsync(
                item => item.Id == customerId && item.Account.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy hồ sơ khách hàng hợp lệ.");

        var address = customer.Addresses
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .FirstOrDefault();

        return new CheckoutPlaceOrderRequest
        {
            ClientRequestId = clientRequestId,
            CartVersion = cartVersion,
            FullName = address?.RecipientName ?? customer.FullName,
            Email = customer.Account.Email,
            Phone = address?.PhoneNumber ?? customer.PhoneNumber,
            AddressLine = address?.Street ?? string.Empty,
            ProvinceId = address?.ProvinceId ?? 0,
            DistrictId = address?.DistrictId ?? 0,
            WardCode = address?.WardCode ?? string.Empty,
            PaymentMethod = OrderApplicationService.CodPaymentMethod,
            MockPaymentOutcome = OrderApplicationService.MockSuccessOutcome,
            Items = cart.Items
                .Where(item => item.IsSelected && item.CanSelect)
                .Select(item => new CheckoutItemConfirmationRequest
                {
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    ExpectedUnitPrice = item.EffectivePrice
                })
                .ToList()
        };
    }

    private int RequireCustomerId()
    {
        return User.GetCustomerId()
            ?? throw new InvalidOperationException(
                "Authenticated account has no CustomerId claim.");
    }

    private static bool MatchesCart(
        IReadOnlyCollection<CheckoutItemConfirmationRequest> requested,
        IReadOnlyCollection<CartLineViewModel> selected)
    {
        if (requested.Count != selected.Count)
        {
            return false;
        }

        var requestedByVariant = requested
            .GroupBy(item => item.VariantId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        if (requestedByVariant.Values.Any(group => group.Length != 1))
        {
            return false;
        }

        foreach (var item in selected)
        {
            if (!requestedByVariant.TryGetValue(item.VariantId, out var matches))
            {
                return false;
            }

            var requestedItem = matches[0];
            if (requestedItem.Quantity != item.Quantity
                || requestedItem.ExpectedUnitPrice != item.EffectivePrice)
            {
                return false;
            }
        }

        return true;
    }

    private string FirstModelError()
    {
        return ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? "Thông tin thanh toán chưa hợp lệ.";
    }

    private IActionResult LookupResponse<T>(
        GhnLookupResult<IReadOnlyList<T>> result,
        Func<IReadOnlyList<T>, IEnumerable<object>> map)
    {
        if (!result.Success || result.Data is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                errorCode = result.ErrorCode,
                message = result.Message
            });
        }

        return Json(new
        {
            success = true,
            data = map(result.Data).ToArray()
        });
    }
}
