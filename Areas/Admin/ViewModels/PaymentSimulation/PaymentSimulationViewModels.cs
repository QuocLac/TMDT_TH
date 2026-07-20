using WebApplication2.Models.Enums;
using WebApplication2.Services.Payments.Reconciliation;

namespace WebApplication2.Areas.Admin.ViewModels.PaymentSimulation;

public sealed class PaymentSimulationPageViewModel
{
    public IReadOnlyList<PendingVnPaySimulationItemViewModel> PendingPayments
    {
        get;
        init;
    } = [];

    public IReadOnlyList<PendingRefundSimulationItemViewModel> Refunds
    {
        get;
        init;
    } = [];

    public IReadOnlyList<DevelopmentPaymentIssueSnapshot> Issues
    {
        get;
        init;
    } = [];
}

public sealed class PendingVnPaySimulationItemViewModel
{
    public int OrderId { get; init; }

    public string OrderCode { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "VND";

    public long PaymentTransactionId { get; init; }

    public string MerchantReference { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
}

public sealed class PendingRefundSimulationItemViewModel
{
    public long ReturnRequestId { get; init; }

    public string ReturnCode { get; init; } = string.Empty;

    public int OrderId { get; init; }

    public string OrderCode { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public ReturnRequestStatus Status { get; init; }

    public decimal RefundAmount { get; init; }

    public DateTime RequestedAt { get; init; }
}
