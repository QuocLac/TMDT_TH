namespace WebApplication2.Services.Catalog;

public interface IProductOptionReadService
{
    Task<IReadOnlyDictionary<int, string>> GetVariantLabelsAsync(
        IEnumerable<int> variantIds,
        CancellationToken cancellationToken);

    Task<ProductOptionPickerSnapshot?> GetProductPickerAsync(
        int productId,
        CancellationToken cancellationToken);
}
