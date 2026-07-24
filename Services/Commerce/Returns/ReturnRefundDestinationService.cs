using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Commerce.Returns;

public sealed record VietQrBankOption(
    string Bin,
    string Code,
    string Name);

public sealed record ReturnRefundDestination(
    long ReturnRequestId,
    string BankBin,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName)
{
    public string MaskedAccountNumber => AccountNumber.Length <= 4
        ? AccountNumber
        : new string('•', Math.Max(4, AccountNumber.Length - 4))
            + AccountNumber[^4..];
}

public interface IReturnRefundDestinationService
{
    IReadOnlyList<VietQrBankOption> Banks { get; }

    ReturnRefundDestination Validate(
        string bankBin,
        string accountNumber,
        string accountName);

    Task SaveAsync(
        ReturnRequest request,
        ReturnRefundDestination destination,
        CancellationToken cancellationToken);

    Task<ReturnRefundDestination?> GetAsync(
        long returnRequestId,
        CancellationToken cancellationToken);

    string BuildQrImageUrl(
        ReturnRefundDestination destination,
        decimal amount,
        string returnCode);
}

public sealed class ReturnRefundDestinationService
    : IReturnRefundDestinationService
{
    private static readonly VietQrBankOption[] SupportedBanks =
    [
        new("970436", "VCB", "Vietcombank"),
        new("970418", "BIDV", "BIDV"),
        new("970415", "CTG", "VietinBank"),
        new("970405", "VBA", "Agribank"),
        new("970422", "MB", "MB Bank"),
        new("970407", "TCB", "Techcombank"),
        new("970416", "ACB", "ACB"),
        new("970432", "VPB", "VPBank"),
        new("970423", "TPB", "TPBank"),
        new("970403", "STB", "Sacombank")
    ];

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ReturnRefundDestinationService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<VietQrBankOption> Banks => SupportedBanks;

    public ReturnRefundDestination Validate(
        string bankBin,
        string accountNumber,
        string accountName)
    {
        var bank = SupportedBanks.SingleOrDefault(item =>
            string.Equals(
                item.Bin,
                bankBin?.Trim(),
                StringComparison.Ordinal));

        if (bank is null)
        {
            throw new InvalidOperationException(
                "Vui lòng chọn ngân hàng nhận tiền hoàn.");
        }

        return new ReturnRefundDestination(
            0,
            bank.Bin,
            bank.Code,
            bank.Name,
            NormalizeAccountNumber(accountNumber),
            NormalizeAccountName(accountName));
    }

    public async Task SaveAsync(
        ReturnRequest request,
        ReturnRefundDestination destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var account = await _context.Set<ReturnRefundAccount>()
            .SingleOrDefaultAsync(
                item => item.ReturnRequestId == request.Id,
                cancellationToken);

        if (account is null)
        {
            account = new ReturnRefundAccount
            {
                ReturnRequestId = request.Id,
                BankBin = destination.BankBin,
                BankCode = destination.BankCode,
                BankName = destination.BankName,
                AccountNumber = destination.AccountNumber,
                AccountName = destination.AccountName,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            _context.Set<ReturnRefundAccount>().Add(account);
        }
        else
        {
            account.BankBin = destination.BankBin;
            account.BankCode = destination.BankCode;
            account.BankName = destination.BankName;
            account.AccountNumber = destination.AccountNumber;
            account.AccountName = destination.AccountName;
            account.UpdatedAt = nowUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReturnRefundDestination?> GetAsync(
        long returnRequestId,
        CancellationToken cancellationToken)
    {
        return await _context.Set<ReturnRefundAccount>()
            .AsNoTracking()
            .Where(item => item.ReturnRequestId == returnRequestId)
            .Select(item => new ReturnRefundDestination(
                item.ReturnRequestId,
                item.BankBin,
                item.BankCode,
                item.BankName,
                item.AccountNumber,
                item.AccountName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public string BuildQrImageUrl(
        ReturnRefundDestination destination,
        decimal amount,
        string returnCode)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var roundedAmount = decimal.Round(
            Math.Max(0m, amount),
            0,
            MidpointRounding.AwayFromZero);
        var addInfo = Uri.EscapeDataString($"FASTBUY {returnCode}");
        var accountName = Uri.EscapeDataString(destination.AccountName);

        return "https://img.vietqr.io/image/"
            + $"{destination.BankBin}-{destination.AccountNumber}-compact2.png"
            + $"?amount={roundedAmount:0}"
            + $"&addInfo={addInfo}"
            + $"&accountName={accountName}";
    }

    private static string NormalizeAccountNumber(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 6 or > 24
            || normalized.Any(character => !char.IsDigit(character)))
        {
            throw new InvalidOperationException(
                "Số tài khoản phải gồm từ 6 đến 24 chữ số.");
        }

        return normalized;
    }

    private static string NormalizeAccountName(string? value)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

        if (normalized.Length is < 2 or > 100)
        {
            throw new InvalidOperationException(
                "Tên chủ tài khoản chưa hợp lệ.");
        }

        return normalized;
    }
}
