using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Cart;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Payments.VnPay;

namespace WebApplication2.Controllers;

[Route("payments/vnpay")]
public sealed class VnPayController : Controller
{
    private readonly IVnPayPaymentService _paymentService;
    private readonly ISessionCartService _cartService;

    public VnPayController(
        IVnPayPaymentService paymentService,
        ISessionCartService cartService)
    {
        _paymentService = paymentService;
        _cartService = cartService;
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpGet("ipn")]
    public async Task<IActionResult> Ipn(
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ProcessCallbackAsync(
            ReadQuery(),
            "VNPay IPN",
            cancellationToken);

        var response = new VnPayIpnResponse(
            result.RspCode,
            result.Message);

        return Content(
            JsonSerializer.Serialize(
                response,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                }),
            "application/json");
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpGet("return")]
    public async Task<IActionResult> Return(
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ProcessCallbackAsync(
            ReadQuery(),
            "VNPay Return",
            cancellationToken);

        if (!result.SignatureValid)
        {
            TempData["ErrorMessage"] =
                "Kết quả thanh toán không hợp lệ hoặc chữ ký VNPay không khớp.";

            return RedirectToAction("Index", "Cart");
        }

        if (result.PaymentSucceeded)
        {
            var currentCustomerId = User.GetCustomerId();

            if (currentCustomerId.HasValue
                && currentCustomerId == result.CustomerId
                && !string.IsNullOrWhiteSpace(
                    result.CheckoutClientRequestId))
            {
                _cartService.CompleteCheckout(
                    result.PurchasedVariantIds,
                    result.CheckoutClientRequestId);
            }

            TempData["SuccessMessage"] =
                "Thanh toán VNPay thành công. Đơn hàng đang được tiếp nhận.";
        }
        else
        {
            var paymentWasFinalized =
                result.RspCode == "00"
                || result.AlreadyProcessed;

            if (paymentWasFinalized)
            {
                _cartService.ResetCheckoutClientRequestId(
                    result.CheckoutClientRequestId);
            }

            TempData["ErrorMessage"] = paymentWasFinalized
                ? "Giao dịch VNPay chưa thành công. Sản phẩm vẫn được giữ trong giỏ để bạn đặt lại."
                : result.Message;
        }

        if (!result.PublicToken.HasValue)
        {
            return RedirectToAction("Index", "Cart");
        }

        return RedirectToAction(
            "Success",
            "Checkout",
            new
            {
                publicToken = result.PublicToken.Value
            });
    }

    private Dictionary<string, string> ReadQuery()
    {
        return Request.Query.ToDictionary(
            item => item.Key,
            item => item.Value.ToString(),
            StringComparer.Ordinal);
    }

    private sealed record VnPayIpnResponse(
        [property: JsonPropertyName("RspCode")] string RspCode,
        [property: JsonPropertyName("Message")] string Message);
}
