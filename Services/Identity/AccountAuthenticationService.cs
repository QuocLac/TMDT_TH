using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Identity;

public sealed class AccountAuthenticationService : IAccountAuthenticationService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountAuthenticationService> _logger;

    public AccountAuthenticationService(
        ApplicationDbContext context,
        IPasswordHasher<Account> passwordHasher,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<AccountAuthenticationService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthenticationResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var username = command.Username.Trim();
        var email = command.Email.Trim();
        var normalizedUsername = AccountSecurity.NormalizeIdentity(username);
        var normalizedEmail = AccountSecurity.NormalizeIdentity(email);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var duplicate = await _context.Accounts
            .AsNoTracking()
            .AnyAsync(account =>
                account.NormalizedUsername == normalizedUsername
                || account.NormalizedEmail == normalizedEmail
                || account.Username.ToUpper() == normalizedUsername
                || account.Email.ToUpper() == normalizedEmail,
                cancellationToken);

        if (duplicate)
        {
            return new AuthenticationResult(
                false,
                "ACCOUNT_IDENTITY_ALREADY_EXISTS",
                "Tên đăng nhập hoặc email đã được sử dụng.");
        }

        var account = new Account
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = AccountRole.Customer,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = nowUtc,
            PasswordChangedAt = nowUtc,
            Customer = new Customer
            {
                FullName = command.FullName.Trim(),
                PhoneNumber = command.PhoneNumber.Trim(),
                Tier = CustomerTier.Standard,
                CreatedAt = nowUtc
            }
        };

        account.PasswordHash = _passwordHasher.HashPassword(account, command.Password);

        try
        {
            _context.Accounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Registration conflicted for normalized username {NormalizedUsername}.",
                normalizedUsername);

            return new AuthenticationResult(
                false,
                "ACCOUNT_REGISTRATION_CONFLICT",
                "Thông tin đăng ký vừa được sử dụng bởi tài khoản khác.");
        }

        await SignInAsync(account, command.FullName, rememberMe: false);
        return new AuthenticationResult(true, null, "Đăng ký tài khoản thành công.");
    }

    public async Task<AuthenticationResult> LoginAsync(
        LoginAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedIdentity = AccountSecurity.NormalizeIdentity(command.Identity);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var account = await _context.Accounts
            .Include(item => item.Customer)
            .SingleOrDefaultAsync(item =>
                item.NormalizedUsername == normalizedIdentity
                || item.NormalizedEmail == normalizedIdentity
                || item.Username.ToUpper() == normalizedIdentity
                || item.Email.ToUpper() == normalizedIdentity,
                cancellationToken);

        if (account is null)
        {
            return InvalidCredentials();
        }

        if (!account.IsActive)
        {
            return new AuthenticationResult(
                false,
                "ACCOUNT_DISABLED",
                "Tài khoản đã bị tạm ngừng. Vui lòng liên hệ bộ phận hỗ trợ.");
        }

        if (account.Customer is null)
        {
            return new AuthenticationResult(
                false,
                "ACCOUNT_CUSTOMER_PROFILE_MISSING",
                "Tài khoản chưa có hồ sơ khách hàng hợp lệ.");
        }

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > nowUtc)
        {
            return new AuthenticationResult(
                false,
                "ACCOUNT_LOCKED",
                $"Tài khoản tạm khóa đến {account.LockoutEndAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}.",
                IsLockedOut: true);
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            account,
            account.PasswordHash,
            command.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            account.FailedAccessCount++;

            if (account.FailedAccessCount >= AccountSecurity.MaximumFailedAccessAttempts)
            {
                account.FailedAccessCount = 0;
                account.LockoutEndAt = nowUtc.Add(AccountSecurity.LockoutDuration);
            }

            account.UpdatedAt = nowUtc;
            await _context.SaveChangesAsync(cancellationToken);

            return account.LockoutEndAt.HasValue
                ? new AuthenticationResult(
                    false,
                    "ACCOUNT_LOCKED",
                    "Đăng nhập sai quá nhiều lần. Tài khoản đã được khóa tạm thời.",
                    IsLockedOut: true)
                : InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = _passwordHasher.HashPassword(account, command.Password);
            account.PasswordChangedAt = nowUtc;
            account.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        account.SecurityStamp ??= Guid.NewGuid().ToString("N");
        account.FailedAccessCount = 0;
        account.LockoutEndAt = null;
        account.LastLoginAt = nowUtc;
        account.UpdatedAt = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);
        await SignInAsync(account, account.Customer.FullName, command.RememberMe);

        return new AuthenticationResult(true, null, "Đăng nhập thành công.");
    }

    public Task SignOutAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Không tìm thấy HTTP context.");

        return httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<AuthenticationResult> ChangePasswordAsync(
        int accountId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var account = await _context.Accounts
            .Include(item => item.Customer)
            .SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken);

        if (account is null || account.Customer is null || !account.IsActive)
        {
            return new AuthenticationResult(
                false,
                "ACCOUNT_NOT_AVAILABLE",
                "Không thể cập nhật mật khẩu cho tài khoản hiện tại.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            account,
            account.PasswordHash,
            currentPassword);

        if (verification == PasswordVerificationResult.Failed)
        {
            return new AuthenticationResult(
                false,
                "CURRENT_PASSWORD_INVALID",
                "Mật khẩu hiện tại không chính xác.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        account.PasswordHash = _passwordHasher.HashPassword(account, newPassword);
        account.PasswordChangedAt = nowUtc;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.FailedAccessCount = 0;
        account.LockoutEndAt = null;
        account.UpdatedAt = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);
        await SignInAsync(account, account.Customer.FullName, rememberMe: false);

        return new AuthenticationResult(true, null, "Mật khẩu đã được cập nhật.");
    }

    private async Task SignInAsync(Account account, string fullName, bool rememberMe)
    {
        if (account.Customer is null)
        {
            throw new InvalidOperationException(
                "Account must have a customer profile before sign-in.");
        }

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                account.Id.ToString(CultureInfo.InvariantCulture)),
            new(
                AccountSecurity.CustomerIdClaim,
                account.Customer.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Role, AccountSecurity.RoleName(account.Role)),
            new(
                AccountSecurity.SecurityStampClaim,
                account.SecurityStamp ?? string.Empty)
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            AllowRefresh = true,
            ExpiresUtc = rememberMe
                ? _timeProvider.GetUtcNow().Add(AccountSecurity.AuthenticationLifetime)
                : null
        };

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Không tìm thấy HTTP context.");

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }

    private static AuthenticationResult InvalidCredentials() =>
        new(
            false,
            "INVALID_CREDENTIALS",
            "Tên đăng nhập, email hoặc mật khẩu không chính xác.");
}
