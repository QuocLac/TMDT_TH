using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Shipping.Ghn;
using WebApplication2.ViewModels.Storefront.Account;

namespace WebApplication2.Controllers;

[Route("account")]
public sealed class AccountController : Controller
{
    private readonly IAccountAuthenticationService _authenticationService;
    private readonly ICustomerAccountService _customerAccountService;
    private readonly IGhnAddressClient _addressClient;

    public AccountController(
        IAccountAuthenticationService authenticationService,
        ICustomerAccountService customerAccountService,
        IGhnAddressClient addressClient)
    {
        _authenticationService = authenticationService;
        _customerAccountService = customerAccountService;
        _addressClient = addressClient;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterAuthentication(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = NormalizeReturnUrl(returnUrl) });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authenticationService.LoginAsync(
            new LoginAccountCommand(model.Identity, model.Password, model.RememberMe),
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectAfterAuthentication(model.ReturnUrl);
    }

    [AllowAnonymous]
    [HttpGet("register")]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterAuthentication(returnUrl);
        }

        return View(new RegisterViewModel { ReturnUrl = NormalizeReturnUrl(returnUrl) });
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authenticationService.RegisterAsync(
            new RegisterAccountCommand(
                model.Username,
                model.Email,
                model.FullName,
                model.PhoneNumber,
                model.Password),
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectAfterAuthentication(model.ReturnUrl);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _authenticationService.SignOutAsync();
        TempData["SuccessMessage"] = "Bạn đã đăng xuất.";
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var profile = await LoadCurrentProfileAsync(cancellationToken);
        return View(ToProfilePage(profile));
    }

    [Authorize]
    [HttpPost("profile")]
    public async Task<IActionResult> Profile(
        [Bind(Prefix = "Form")] ProfileInputModel model,
        CancellationToken cancellationToken)
    {
        var profile = await LoadCurrentProfileAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return View(ToProfilePage(profile, model));
        }

        var updated = await _customerAccountService.UpdateProfileAsync(
            profile.CustomerId,
            model.FullName,
            model.PhoneNumber,
            cancellationToken);

        if (!updated)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Thông tin cá nhân đã được cập nhật.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpGet("change-password")]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var accountId = User.GetAccountId();
        if (!accountId.HasValue)
        {
            return Challenge();
        }

        var result = await _authenticationService.ChangePasswordAsync(
            accountId.Value,
            model.CurrentPassword,
            model.NewPassword,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpGet("addresses")]
    public async Task<IActionResult> Addresses(CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        return View(new AddressBookPageViewModel
        {
            Addresses = await _customerAccountService.GetAddressesAsync(
                customerId,
                cancellationToken)
        });
    }

    [Authorize]
    [HttpPost("addresses/save")]
    public async Task<IActionResult> SaveAddress(
        [Bind(Prefix = "Form")] AddressInputModel model,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        if (!ModelState.IsValid)
        {
            return View("Addresses", new AddressBookPageViewModel
            {
                Addresses = await _customerAccountService.GetAddressesAsync(
                    customerId,
                    cancellationToken),
                Form = model
            });
        }

        var validation = await _addressClient.ValidateAddressAsync(
            model.ProvinceId,
            model.DistrictId,
            model.WardCode,
            cancellationToken);

        if (!validation.IsValid)
        {
            ModelState.AddModelError(
                string.Empty,
                validation.ErrorMessage ?? "Địa chỉ chưa được GHN xác nhận.");

            return View("Addresses", new AddressBookPageViewModel
            {
                Addresses = await _customerAccountService.GetAddressesAsync(
                    customerId,
                    cancellationToken),
                Form = model
            });
        }

        await _customerAccountService.SaveAddressAsync(
            new SaveCustomerAddressCommand(
                model.AddressId,
                customerId,
                model.RecipientName,
                model.PhoneNumber,
                model.Street,
                validation.Province!.ProvinceId,
                validation.Province.ProvinceName,
                validation.District!.DistrictId,
                validation.District.DistrictName,
                validation.Ward!.WardCode,
                validation.Ward.WardName,
                model.IsDefault),
            cancellationToken);

        TempData["SuccessMessage"] = "Địa chỉ giao hàng đã được lưu.";
        return RedirectToAction(nameof(Addresses));
    }

    [Authorize]
    [HttpPost("addresses/{id:int}/default")]
    public async Task<IActionResult> SetDefaultAddress(
        int id,
        CancellationToken cancellationToken)
    {
        var updated = await _customerAccountService.SetDefaultAddressAsync(
            RequireCustomerId(),
            id,
            cancellationToken);

        if (!updated)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Đã cập nhật địa chỉ mặc định.";
        return RedirectToAction(nameof(Addresses));
    }

    [Authorize]
    [HttpPost("addresses/{id:int}/delete")]
    public async Task<IActionResult> DeleteAddress(
        int id,
        CancellationToken cancellationToken)
    {
        var deleted = await _customerAccountService.DeleteAddressAsync(
            RequireCustomerId(),
            id,
            cancellationToken);

        if (!deleted)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Địa chỉ đã được xóa.";
        return RedirectToAction(nameof(Addresses));
    }

    [AllowAnonymous]
    [HttpGet("access-denied")]
    public IActionResult AccessDenied() => View();

    private int RequireCustomerId()
    {
        return User.GetCustomerId()
            ?? throw new InvalidOperationException(
                "Authenticated account has no CustomerId claim.");
    }

    private async Task<CustomerProfileSnapshot> LoadCurrentProfileAsync(
        CancellationToken cancellationToken)
    {
        return await _customerAccountService.GetProfileAsync(
                RequireCustomerId(),
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy hồ sơ khách hàng hiện tại.");
    }

    private IActionResult RedirectAfterAuthentication(string? returnUrl)
    {
        var normalized = NormalizeReturnUrl(returnUrl);
        return normalized is null
            ? RedirectToAction("Index", "Home")!
            : LocalRedirect(normalized);
    }

    private string? NormalizeReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;
    }

    private static ProfilePageViewModel ToProfilePage(
        CustomerProfileSnapshot profile,
        ProfileInputModel? form = null)
    {
        return new ProfilePageViewModel
        {
            Username = profile.Username,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl,
            Form = form ?? new ProfileInputModel
            {
                FullName = profile.FullName,
                PhoneNumber = profile.PhoneNumber
            }
        };
    }
}
