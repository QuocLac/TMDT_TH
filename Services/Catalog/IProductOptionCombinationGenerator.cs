namespace WebApplication2.Services.Catalog;

public interface IProductOptionCombinationGenerator
{
    Task<ProductOptionCombinationPreview> PreviewAsync(
        int productId,
        CancellationToken cancellationToken);

    Task<ProductOptionCombinationGenerationResult> GenerateMissingAsync(
        ProductOptionCombinationGenerationCommand command,
        CancellationToken cancellationToken);
}

public sealed record ProductOptionCombinationGenerationCommand(
    int ProductId,
    decimal ListPrice,
    int StockQuantity,
    bool ActivateNewItems);

public sealed record ProductOptionCombinationPreview(
    int ActiveGroupCount,
    long TotalCombinationCount,
    int ExistingCombinationCount,
    long MissingCombinationCount,
    decimal SuggestedListPrice,
    IReadOnlyList<string> RequiredGroupsWithoutValues,
    bool HasExistingConflict,
    bool IsOverLimit,
    int MaximumCombinationCount);

public sealed record ProductOptionCombinationGenerationResult(
    long TotalCombinationCount,
    int ExistingCombinationCount,
    int CreatedItemCount,
    int CompleteItemCount,
    int IncompleteItemCount);

public sealed class ProductOptionGenerationException : Exception
{
    public ProductOptionGenerationException(string message)
        : base(message)
    {
    }
}
