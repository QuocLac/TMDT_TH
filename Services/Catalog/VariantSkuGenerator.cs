using System.Globalization;

namespace WebApplication2.Services.Catalog;

public static class VariantSkuGenerator
{
    private const string Prefix = "SKU-P";
    private const string ProductIdFormat = "D6";
    private const string VariantIdFormat = "D8";

    public static string CreateTemporarySku()
    {
        return $"TMP-{Guid.NewGuid():N}";
    }

    public static string CreateSku(int productId, int variantId)
    {
        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(productId),
                productId,
                "Product ID must be greater than zero.");
        }

        if (variantId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(variantId),
                variantId,
                "Variant ID must be greater than zero.");
        }

        return Prefix
            + productId.ToString(ProductIdFormat, CultureInfo.InvariantCulture)
            + "-V"
            + variantId.ToString(VariantIdFormat, CultureInfo.InvariantCulture);
    }
}
