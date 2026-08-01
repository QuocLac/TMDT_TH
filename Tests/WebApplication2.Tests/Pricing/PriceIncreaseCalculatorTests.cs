using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Tests.Pricing;

public sealed class PriceIncreaseCalculatorTests
{
    [Fact]
    public void Calculate_PercentIncrease_UsesListPrice()
    {
        var result = PriceIncreaseCalculator.Calculate(
            200_000m,
            PriceAdjustmentType.PercentIncrease,
            10m,
            "SKU-01");

        Assert.Equal(220_000m, result);
    }

    [Fact]
    public void Calculate_AmountIncrease_AddsAmount()
    {
        var result = PriceIncreaseCalculator.Calculate(
            200_000m,
            PriceAdjustmentType.AmountIncrease,
            50_000m,
            "SKU-01");

        Assert.Equal(250_000m, result);
    }

    [Fact]
    public void Calculate_PercentIncrease_RejectsExcessiveValue()
    {
        var exception = Assert.Throws<
            InvalidPriceIncreaseException>(() =>
                PriceIncreaseCalculator.Calculate(
                    200_000m,
                    PriceAdjustmentType.PercentIncrease,
                    1000.01m,
                    "SKU-01"));

        Assert.Contains(
            "không được vượt quá",
            exception.Message);
    }

    [Fact]
    public void Calculate_RejectsNonPositiveAdjustment()
    {
        Assert.Throws<InvalidPriceIncreaseException>(() =>
            PriceIncreaseCalculator.Calculate(
                200_000m,
                PriceAdjustmentType.AmountIncrease,
                0m,
                "SKU-01"));
    }
}
