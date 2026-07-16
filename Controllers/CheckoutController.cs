using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Cart;
using WebApplication2.Services.Commerce.Checkout;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Orders;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Payments.VnPay;
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
    private readonly ICheckoutShippingQuoteService _shippingQuoteService;
    private readonly IOrderApplicationService _orderApplicationService;
    private readonly IVnPayPaymentService _vnPayPaymentService;
    private readonly VnPayOptions _vnPayOptions;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        ApplicationDbContext context,
        ISessionCartService cartService,
        IGhnAddressClient addressClient,
        ICheckoutShippingQuoteService shippingQuoteService,
        IOrderApplicationService orderApplicationService,
        IVnPayPaymentService vnPayPaymentService,
        IOptions<VnPayOptions> vnPayOptions,
        ILogger<CheckoutController> logger)
    {
        _context = context;
        _cartService = cartService;
        _addressClient = addressClient;
        _shippingQuoteService = shippingQuoteService;
        _orderApplicationService = orderApplicationService;
        _vnPayPaymentService = vnPayPaymentService;
        _vnPayOptions = vnPayOptions.Value;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);

        if (!cart.HasSelectedItems)
        {
            return RedirectToAction("Index", "Cart");
        }

        var cartVersion = _cartService.GetCartVersion();
        var clientRequestId =
            _cartService.GetOrCreateCheckoutClientRequestId(cartVersion);
        var customerId = RequireCustomerId();

        var existing = await _orderApplicationService
            .FindByClientRequestIdAsync(
                clientRequestId,
                customerId,
                cancellationToken);

        if (existing is not null)
        {
            return await ContinueExistingOrderAsync(
                existing,
                customerId,
                clientRequestId,
                cancellationToken);
        }

        var model = await BuildPageModelAsync(
            cart,
            cartVersion,
            clientRequestId,
            form: null,
            shippingQuote: null,
            errorMessage: null,
            cancellationToken);

        return View(model);
    }

    [HttpPost("shipping-quote")]
    public async Task<IActionResult> ShippingQuote(
        [FromBody] CheckoutShippingQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "INVALID_QUOTE_INPUT",
                message = FirstModelError()
            });
        }

        if (!IsSupportedPaymentMethod(request.PaymentMethod))
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "PAYMENT_METHOD_UNAVAILABLE",
                message = "Phương thức thanh toán đã chọn chưa khả dụng."
            });
        }

        var cart = await _cartService.GetCartAsync(cancellationToken);
        var cartVersion = _cartService.GetCartVersion();

        if (request.CartVersion != cartVersion)
        {
            return Conflict(new
            {
                success = false,
                errorCode = "CART_VERSION_CHANGED",
                message = "Giỏ hàng vừa thay đổi. Vui lòng tải lại trang thanh toán."
            });
        }

        var selectedItems = SelectedCartItems(cart);

        if (selectedItems.Length == 0)
        {
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = "EMPTY_CHECKOUT",
                message = "Không còn sản phẩm hợp lệ để tính phí giao hàng."
            });
        }

        var result = await _shippingQuoteService.QuoteAsync(
            BuildShippingQuoteCommand(
                request.DistrictId,
                request.WardCode,
                request.PaymentMethod,
                selectedItems),
            cancellationToken);

        if (!result.Success)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    success = false,
                    errorCode = result.ErrorCode,
                    message = result.Message
                });
        }

        return Json(new
        {
            success = true,
            data = new CheckoutShippingQuoteResponseViewModel
            {
                Fee = result.Fee,
                GrandTotal = cart.SelectedSubtotal + result.Fee,
                ServiceId = result.ServiceId,
                ServiceTypeId = result.ServiceTypeId,
                ServiceName = result.ServiceName,
                IsFallback = result.IsFallback,
                Message = result.Message
            }
        });
    }

    [HttpPost("place-order")]
    public async Task<IActionResult> PlaceOrder(
        CheckoutPlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();

        var existing = await _orderApplicationService
            .FindByClientRequestIdAsync(
                request.ClientRequestId,
                customerId,
                cancellationToken);

        if (existing is not null)
        {
            return await ContinueExistingOrderAsync(
                existing,
                customerId,
                request.ClientRequestId,
                cancellationToken);
        }

        var cart = await _cartService.GetCartAsync(cancellationToken);
        var cartVersion = _cartService.GetCartVersion();
        var selectedItems = SelectedCartItems(cart);

        if (!cart.HasSelectedItems || selectedItems.Length == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Giỏ hàng không còn sản phẩm được chọn.");
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

        if (!IsSupportedPaymentMethod(request.PaymentMethod))
        {
            ModelState.AddModelError(
                nameof(request.PaymentMethod),
                request.PaymentMethod == OrderApplicationService.VnPayPaymentMethod
                    ? "VNPay chưa được cấu hình để nhận thanh toán."
                    : "Phương thức thanh toán không được hỗ trợ.");
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
                    addressValidation.ErrorMessage
                    ?? "Địa chỉ giao hàng không hợp lệ.");
            }
        }

        CheckoutShippingQuoteResult? shippingQuote = null;

        if (ModelState.IsValid
            && addressValidation is not null
            && addressValidation.IsValid)
        {
            shippingQuote = await _shippingQuoteService.QuoteAsync(
                BuildShippingQuoteCommand(
                    addressValidation.District!.DistrictId,
                    addressValidation.Ward!.WardCode,
                    request.PaymentMethod,
                    selectedItems),
                cancellationToken);

            if (!shippingQuote.Success)
            {
                ModelState.AddModelError(
                    string.Empty,
                    shippingQuote.Message);
            }
            else if (!MatchesDisplayedQuote(request, shippingQuote))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Phí hoặc gói giao hàng vừa được cập nhật. Vui lòng kiểm tra tổng thanh toán và xác nhận lại.");
            }
        }

        if (!ModelState.IsValid
            || addressValidation is null
            || !addressValidation.IsValid
            || shippingQuote is null
            || !shippingQuote.Success)
        {
            var invalidModel = await BuildPageModelAsync(
                cart,
                cartVersion,
                request.ClientRequestId,
                request,
                shippingQuote,
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
                    shippingQuote.Fee,
                    shippingQuote.ServiceId,
                    shippingQuote.ServiceTypeId,
                    shippingQuote.ServiceName,
                    shippingQuote.WeightGram,
                    shippingQuote.LengthCm,
                    shippingQuote.WidthCm,
                    shippingQuote.HeightCm,
                    selectedItems
                        .Select(item => new PlaceOrderLine(
                            item.VariantId,
                            item.Quantity,
                            item.EffectivePrice))
                        .ToArray()),
                cancellationToken);

            if (result.ShouldClearPurchasedItems)
            {
                CompleteCartSafely(
                    result.PurchasedVariantIds,
                    request.ClientRequestId,
                    result.OrderCode);
            }

            if (result.RequiresPaymentRedirect)
            {
                return await RedirectToVnPayAsync(
                    result.OrderId,
                    customerId,
                    result.PublicToken,
                    cancellationToken);
            }

            return RedirectToAction(
                nameof(Success),
                new
                {
                    publicToken = result.PublicToken
                });
        }
        catch (Exception exception) when (exception is
                   CheckoutValidationException
                   or CheckoutConflictException
                   or InventoryValidationException
                   or InventoryConflictException)
        {
            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            var failedModel = await BuildPageModelAsync(
                cart,
                cartVersion,
                request.ClientRequestId,
                request,
                shippingQuote,
                exception.Message,
                cancellationToken);

            return View("Index", failedModel);
        }
    }

    [HttpPost("pay/{publicToken:guid}")]
    public async Task<IActionResult> RetryPayment(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();

        var orderId = await _context.Orders
            .AsNoTracking()
            .Where(order =>
                order.PublicToken == publicToken
                && order.CustomerId == customerId
                && order.OrderStatus == OrderStatus.PendingPayment
                && order.PaymentStatus == PaymentStatus.Pending)
            .Select(order => (int?)order.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (!orderId.HasValue)
        {
            TempData["ErrorMessage"] =
                "Đơn hàng không còn ở trạng thái chờ thanh toán.";

            return RedirectToAction(
                nameof(Success),
                new
                {
                    publicToken
                });
        }

        return await RedirectToVnPayAsync(
            orderId.Value,
            customerId,
            publicToken,
            cancellationToken);
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

        return View(
            CheckoutSuccessViewModel.FromReceipt(receipt));
    }

    [HttpGet("provinces")]
    public async Task<IActionResult> Provinces(
        CancellationToken cancellationToken)
    {
        var result = await _addressClient.GetProvincesAsync(
            cancellationToken);

        return LookupResponse(
            result,
            items => items
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
        var result = await _addressClient.GetDistrictsAsync(
            provinceId,
            cancellationToken);

        return LookupResponse(
            result,
            items => items
                .Where(item =>
                    item.IsEnabled
                    && item.ProvinceId == provinceId)
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
        var result = await _addressClient.GetWardsAsync(
            districtId,
            cancellationToken);

        return LookupResponse(
            result,
            items => items
                .Where(item =>
                    item.IsEnabled
                    && item.DistrictId == districtId)
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
        CheckoutShippingQuoteResult? shippingQuote,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var provinces = await _addressClient.GetProvincesAsync(
            cancellationToken);

        var provinceOptions =
            provinces.Success && provinces.Data is not null
                ? provinces.Data
                    .Where(item => item.IsEnabled)
                    .OrderBy(item => item.ProvinceName)
                    .Select(item => new CheckoutProvinceOption(
                        item.ProvinceId,
                        item.ProvinceName,
                        item.Code))
                    .ToArray()
                : Array.Empty<CheckoutProvinceOption>();

        form ??= await BuildCustomerPrefillAsync(
            cart,
            cartVersion,
            clientRequestId,
            cancellationToken);

        if (!IsSupportedPaymentMethod(form.PaymentMethod))
        {
            form.PaymentMethod =
                OrderApplicationService.CodPaymentMethod;
        }

        if (shippingQuote is null
            && form.DistrictId > 0
            && !string.IsNullOrWhiteSpace(form.WardCode)
            && cart.HasSelectedItems)
        {
            shippingQuote = await _shippingQuoteService.QuoteAsync(
                BuildShippingQuoteCommand(
                    form.DistrictId,
                    form.WardCode,
                    form.PaymentMethod,
                    SelectedCartItems(cart)),
                cancellationToken);
        }

        return CheckoutPageViewModel.FromCart(
            cart,
            cartVersion,
            clientRequestId,
            shippingQuote,
            provinceOptions,
            _vnPayOptions.IsConfigured,
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
                item =>
                    item.Id == customerId
                    && item.Account.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy hồ sơ khách hàng hợp lệ.");

        var address = customer.Addresses
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(
                item => item.UpdatedAt ?? item.CreatedAt)
            .FirstOrDefault();

        return new CheckoutPlaceOrderRequest
        {
            ClientRequestId = clientRequestId,
            CartVersion = cartVersion,
            FullName =
                address?.RecipientName ?? customer.FullName,
            Email = customer.Account.Email,
            Phone =
                address?.PhoneNumber ?? customer.PhoneNumber,
            AddressLine = address?.Street ?? string.Empty,
            ProvinceId = address?.ProvinceId ?? 0,
            DistrictId = address?.DistrictId ?? 0,
            WardCode = address?.WardCode ?? string.Empty,
            PaymentMethod =
                OrderApplicationService.CodPaymentMethod,
            Items = cart.Items
                .Where(item =>
                    item.IsSelected
                    && item.CanSelect)
                .Select(item =>
                    new CheckoutItemConfirmationRequest
                    {
                        VariantId = item.VariantId,
                        Quantity = item.Quantity,
                        ExpectedUnitPrice =
                            item.EffectivePrice
                    })
                .ToList()
        };
    }

    private async Task<IActionResult> ContinueExistingOrderAsync(
        PlaceOrderResult existing,
        int customerId,
        string clientRequestId,
        CancellationToken cancellationToken)
    {
        if (existing.OrderStatus == OrderStatus.Cancelled
            || existing.PaymentStatus is (
                PaymentStatus.Failed
                or PaymentStatus.Cancelled))
        {
            _cartService.ResetCheckoutClientRequestId(
                clientRequestId);

            TempData["ErrorMessage"] =
                "Lần thanh toán trước chưa thành công. Bạn có thể kiểm tra và đặt lại đơn.";

            return RedirectToAction(nameof(Index));
        }

        if (existing.ShouldClearPurchasedItems)
        {
            CompleteCartSafely(
                existing.PurchasedVariantIds,
                clientRequestId,
                existing.OrderCode);
        }

        if (existing.RequiresPaymentRedirect)
        {
            return await RedirectToVnPayAsync(
                existing.OrderId,
                customerId,
                existing.PublicToken,
                cancellationToken);
        }

        return RedirectToAction(
            nameof(Success),
            new
            {
                publicToken = existing.PublicToken
            });
    }

    private async Task<IActionResult> RedirectToVnPayAsync(
        int orderId,
        int customerId,
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        var result = await _vnPayPaymentService
            .CreatePaymentUrlAsync(
                orderId,
                customerId,
                GetClientIpAddress(),
                cancellationToken);

        if (!result.Success
            || string.IsNullOrWhiteSpace(result.PaymentUrl))
        {
            TempData["ErrorMessage"] =
                result.Message
                + " Đơn hàng vẫn được giữ ở trạng thái chờ thanh toán.";

            return RedirectToAction(
                nameof(Success),
                new
                {
                    publicToken
                });
        }

        return Redirect(result.PaymentUrl);
    }

    private void CompleteCartSafely(
        IReadOnlyCollection<int> purchasedVariantIds,
        string clientRequestId,
        string orderCode)
    {
        try
        {
            _cartService.CompleteCheckout(
                purchasedVariantIds,
                clientRequestId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Order {OrderCode} was committed but purchased cart lines could not be removed.",
                orderCode);
        }
    }

    private bool IsSupportedPaymentMethod(string? paymentMethod)
    {
        return paymentMethod == OrderApplicationService.CodPaymentMethod
            || paymentMethod == OrderApplicationService.VnPayPaymentMethod
            && _vnPayOptions.IsConfigured;
    }

    private static CartLineViewModel[] SelectedCartItems(
        CartPageViewModel cart)
    {
        return cart.Items
            .Where(item =>
                item.IsSelected
                && item.CanSelect)
            .OrderBy(item => item.VariantId)
            .ToArray();
    }

    private static CheckoutShippingQuoteCommand
        BuildShippingQuoteCommand(
            int districtId,
            string wardCode,
            string paymentMethod,
            IReadOnlyCollection<CartLineViewModel> items)
    {
        return new CheckoutShippingQuoteCommand(
            districtId,
            wardCode,
            paymentMethod,
            items.Select(item => new CheckoutShippingLine(
                    item.VariantId,
                    item.ProductName,
                    item.Sku,
                    item.Quantity,
                    item.EffectivePrice))
                .ToArray());
    }

    private static bool MatchesDisplayedQuote(
        CheckoutPlaceOrderRequest request,
        CheckoutShippingQuoteResult quote)
    {
        return request.ExpectedShippingFee == quote.Fee
            && request.ShippingServiceId == quote.ServiceId
            && request.ShippingServiceTypeId
                == quote.ServiceTypeId;
    }

    private int RequireCustomerId()
    {
        return User.GetCustomerId()
            ?? throw new InvalidOperationException(
                "Authenticated account has no CustomerId claim.");
    }

    private string GetClientIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress
            ?.MapToIPv4()
            .ToString()
            ?? "127.0.0.1";
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
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        if (requestedByVariant.Values.Any(
                group => group.Length != 1))
        {
            return false;
        }

        foreach (var item in selected)
        {
            if (!requestedByVariant.TryGetValue(
                    item.VariantId,
                    out var matches))
            {
                return false;
            }

            var requestedItem = matches[0];

            if (requestedItem.Quantity != item.Quantity
                || requestedItem.ExpectedUnitPrice
                    != item.EffectivePrice)
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
            .FirstOrDefault(message =>
                !string.IsNullOrWhiteSpace(message))
            ?? "Thông tin thanh toán chưa hợp lệ.";
    }

    private IActionResult LookupResponse<T>(
        GhnLookupResult<IReadOnlyList<T>> result,
        Func<IReadOnlyList<T>, IEnumerable<object>> map)
    {
        if (!result.Success || result.Data is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
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
