namespace WebApplication2.Services.Catalog;

public sealed record ProductOptionPickerSnapshot(
    int ProductId,
    string ProductName,
    string ProductSlug,
    string ImageUrl,
    decimal MinimumPrice,
    int AvailableItemCount,
    IReadOnlyList<ProductOptionGroupSnapshot> Groups,
    IReadOnlyList<ProductOptionItemSnapshot> Items);

public sealed record ProductOptionGroupSnapshot(
    string Code,
    string Name,
    IReadOnlyList<string> Values);

public sealed record ProductOptionItemSnapshot(
    int VariantId,
    string Sku,
    string ImageUrl,
    decimal OriginalPrice,
    decimal EffectivePrice,
    int StockQuantity,
    IReadOnlyDictionary<string, string> Selections);
