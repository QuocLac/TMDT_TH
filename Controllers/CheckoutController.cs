using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Cart;
using WebApplication2.Services.Shipping.Ghn;
using WebApplication2.ViewModels.Storefront.Checkout;

namespace WebApplication2.Controllers;

[Route("checkout")]
public sealed class CheckoutController : Controller
{
    private readonly ISessionCartService _cartService;
    private readonly IGhnAddressClient _addressClient;

    public CheckoutController(
        ISessionCartService cartService,
        IGhnAddressClient addressClient)
    {
        _cartService = cartService;
        _addressClient = addressClient;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);
        if (!cart.HasSelectedItems)
        {
            return RedirectToAction("Index", "Cart");
        }

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

        var model = CheckoutPageViewModel.FromCart(
            cart,
            provinceOptions,
            provinces.Success ? null : provinces.Message);

        return View(model);
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
                Message = "Địa chỉ đã được đối chiếu với dữ liệu GHN.",
                ProvinceId = validation.Province!.ProvinceId,
                ProvinceName = validation.Province.ProvinceName,
                DistrictId = validation.District!.DistrictId,
                DistrictName = validation.District.DistrictName,
                WardCode = validation.Ward!.WardCode,
                WardName = validation.Ward.WardName
            }
        });
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
