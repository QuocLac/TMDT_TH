using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Identity;

public sealed class AdminAccountBootstrapper : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AdminBootstrapOptions _options;
    private readonly ILogger<AdminAccountBootstrapper> _logger;

    public AdminAccountBootstrapper(
        IServiceProvider serviceProvider,
        IOptions<AdminBootstrapOptions> options,
        ILogger<AdminAccountBootstrapper> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider
            .GetRequiredService<IPasswordHasher<Account>>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        if (await context.Accounts.AnyAsync(
                item => item.Role == AccountRole.Admin,
                cancellationToken))
        {
            return;
        }

        var normalizedUsername = AccountSecurity.NormalizeIdentity(_options.Username);
        var normalizedEmail = AccountSecurity.NormalizeIdentity(_options.Email);

        var identityExists = await context.Accounts
            .AsNoTracking()
            .AnyAsync(item =>
                item.NormalizedUsername == normalizedUsername
                || item.NormalizedEmail == normalizedEmail
                || item.Username.ToUpper() == normalizedUsername
                || item.Email.ToUpper() == normalizedEmail,
                cancellationToken);

        if (identityExists)
        {
            _logger.LogError(
                "Bootstrap Admin could not be created because its username or email already exists.");
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var account = new Account
        {
            Username = _options.Username.Trim(),
            NormalizedUsername = normalizedUsername,
            Email = _options.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            Role = AccountRole.Admin,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = nowUtc,
            PasswordChangedAt = nowUtc,
            Customer = new Customer
            {
                FullName = _options.FullName.Trim(),
                PhoneNumber = _options.PhoneNumber.Trim(),
                Tier = CustomerTier.Standard,
                CreatedAt = nowUtc
            }
        };

        account.PasswordHash = passwordHasher.HashPassword(account, _options.Password);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrap Admin account {Username} was created. Disable the bootstrap setting now.",
            account.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
