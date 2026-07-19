namespace WebApplication2.Services.Catalog;

public interface IProductOptionIntegrityService
{
    Task<ProductOptionIntegrityResult> RebuildProductKeysAsync(
        int productId,
        CancellationToken cancellationToken);
}

public sealed record ProductOptionIntegrityResult(
    int TotalItemCount,
    int CompleteItemCount,
    int IncompleteItemCount);

public sealed class ProductOptionCombinationConflictException : Exception
{
    public ProductOptionCombinationConflictException(
        string firstSku,
        string secondSku)
        : base(
            $"Mã hàng {firstSku} và {secondSku} đang dùng cùng một tổ hợp lựa chọn mua.")
    {
        FirstSku = firstSku;
        SecondSku = secondSku;
    }

    public string FirstSku { get; }

    public string SecondSku { get; }
}
