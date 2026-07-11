namespace WebApplication2.Services.Commerce.Flows;

public enum CommerceFlowStage
{
    Initialize,
    ValidateInput,
    LoadAggregate,
    ValidateConcurrency,
    ValidateBusinessRules,
    ValidateStateTransition,
    Deduplicate,
    BeginTransaction,
    ApplyStateTransition,
    UpdateRelatedAggregate,
    WriteInventoryLedger,
    WriteTimeline,
    WriteInbox,
    WriteOutbox,
    SaveChanges,
    Commit,
    Rollback,
    CallProvider,
    ParseProviderResponse,
    ApplyProviderResponse,
    Complete
}
