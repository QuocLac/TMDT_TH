namespace WebApplication2.Services.Identity;

public sealed record RegisterAccountCommand(
    string Username,
    string Email,
    string FullName,
    string PhoneNumber,
    string Password);

public sealed record LoginAccountCommand(
    string Identity,
    string Password,
    bool RememberMe);

public sealed record AuthenticationResult(
    bool Success,
    string? ErrorCode,
    string Message,
    bool IsLockedOut = false);

public interface IAccountAuthenticationService
{
    Task<AuthenticationResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken);

    Task<AuthenticationResult> LoginAsync(
        LoginAccountCommand command,
        CancellationToken cancellationToken);

    Task SignOutAsync();

    Task<AuthenticationResult> ChangePasswordAsync(
        int accountId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}
