using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public interface IPriceCampaignWorkflowService
{
    Task<PriceCampaignWorkflowResult> SaveDraftAsync(
        PriceCampaignDraftCommand command,
        CancellationToken cancellationToken);

    Task<PriceCampaignWorkflowResult> ConfirmDraftAsync(
        ConfirmPriceCampaignCommand command,
        CancellationToken cancellationToken);

    Task<PriceCampaignWorkflowResult> ActivateNowAsync(
        ActivatePriceCampaignCommand command,
        CancellationToken cancellationToken);

    Task<PriceCampaignWorkflowResult> CancelAsync(
        CancelPriceCampaignCommand command,
        CancellationToken cancellationToken);

    Task<PriceCampaignWorkflowResult> RecoverAsync(
        RecoverPriceCampaignCommand command,
        CancellationToken cancellationToken);
}

public sealed record PriceCampaignDraftCommand(
    int CampaignId,
    string Name,
    string? Description,
    PriceCampaignMode Mode,
    DateTime StartDateUtc,
    DateTime? EndDateUtc,
    string Reason,
    PriceChangeSourceType SourceType,
    PriceConflictPolicy ConflictPolicy,
    string ClientRequestId,
    byte[]? ExpectedCampaignRowVersion,
    IReadOnlyCollection<PricePlanPreviewInput> Items,
    string Actor,
    string CorrelationId);

public sealed record ConfirmPriceCampaignCommand(
    int CampaignId,
    byte[] ExpectedCampaignRowVersion,
    string Actor,
    string CorrelationId);

public sealed record ActivatePriceCampaignCommand(
    int CampaignId,
    byte[] ExpectedCampaignRowVersion,
    string Actor,
    string CorrelationId);

public sealed record CancelPriceCampaignCommand(
    int CampaignId,
    byte[] ExpectedCampaignRowVersion,
    string Reason,
    string Actor,
    string CorrelationId);

public sealed record RecoverPriceCampaignCommand(
    int SourceCampaignId,
    byte[] ExpectedSourceCampaignRowVersion,
    string Reason,
    string ClientRequestId,
    string Actor,
    string CorrelationId);

public sealed record PriceCampaignWorkflowResult(
    bool Success,
    string Message,
    string? ErrorCode,
    int CampaignId,
    string CampaignCode,
    PriceCampaignStatus Status,
    byte[] RowVersion,
    string ClientRequestId,
    PricePlanPreviewResult? Preview)
{
    public static PriceCampaignWorkflowResult Failure(
        string message,
        string errorCode,
        PricePlanPreviewResult? preview = null,
        int campaignId = 0)
    {
        return new(
            false,
            message,
            errorCode,
            campaignId,
            string.Empty,
            PriceCampaignStatus.Draft,
            [],
            string.Empty,
            preview);
    }
}
