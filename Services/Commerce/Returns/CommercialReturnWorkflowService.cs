using WebApplication2.Models;

namespace WebApplication2.Services.Commerce.Returns;

public sealed class CommercialReturnWorkflowService : IReturnWorkflowService
{
    private readonly ReturnWorkflowService _inner;

    public CommercialReturnWorkflowService(ReturnWorkflowService inner)
    {
        _inner = inner;
    }

    public async Task<ReturnEligibilitySnapshot?> GetEligibilityAsync(
        Guid orderPublicToken,
        CancellationToken cancellationToken)
    {
        var result = await _inner.GetEligibilityAsync(
            orderPublicToken,
            cancellationToken);

        if (result is null)
        {
            return null;
        }

        var message = result.IsEligible
            ? $"Đơn đủ điều kiện gửi yêu cầu trong {ReturnPolicy.ReturnWindowDays} ngày kể từ khi giao thành công."
            : Commercialize(result.Message);

        return result with { Message = message };
    }

    public Task<ReturnRequest> CreateRequestAsync(
        CreateReturnRequestCommand command,
        CancellationToken cancellationToken) =>
        _inner.CreateRequestAsync(command, cancellationToken);

    public Task<ReturnRequest> StartReviewAsync(
        StartReturnReviewCommand command,
        CancellationToken cancellationToken) =>
        _inner.StartReviewAsync(command, cancellationToken);

    public Task<ReturnRequest> DecideAsync(
        DecideReturnRequestCommand command,
        CancellationToken cancellationToken) =>
        _inner.DecideAsync(command, cancellationToken);

    public Task<ReturnRequest> BeginInspectionAsync(
        BeginReturnInspectionCommand command,
        CancellationToken cancellationToken) =>
        _inner.BeginInspectionAsync(command, cancellationToken);

    public Task<ReturnRequest> CompleteInspectionAsync(
        CompleteReturnInspectionCommand command,
        CancellationToken cancellationToken) =>
        _inner.CompleteInspectionAsync(command, cancellationToken);

    private static string Commercialize(string value) =>
        value.Replace(
            "GHN giao thành công",
            "giao thành công",
            StringComparison.OrdinalIgnoreCase)
        .Replace(
            "GHN đã giao",
            "đã được giao",
            StringComparison.OrdinalIgnoreCase)
        .Replace(
            "provider",
            "đơn vị xử lý",
            StringComparison.OrdinalIgnoreCase);
}
