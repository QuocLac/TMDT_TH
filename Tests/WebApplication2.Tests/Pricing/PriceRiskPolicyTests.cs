using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Tests.Pricing;

public sealed class PriceRiskPolicyTests
{
    [Fact]
    public void Assess_blocks_discount_at_critical_threshold()
    {
        var assessment = PriceRiskPolicy.Assess(
            CreateInput(
                listPrice: 1_000_000m,
                newPrice: 100_000m,
                actor: "checker"));

        Assert.Equal(
            PriceRiskLevel.Critical,
            assessment.Level);
        Assert.True(assessment.HasBlockingIssues);
        Assert.Contains(
            assessment.Issues,
            issue => issue.Code == "CRITICAL_DISCOUNT");
    }

    [Fact]
    public void Assess_requires_second_approver_for_large_discount()
    {
        var assessment = PriceRiskPolicy.Assess(
            CreateInput(
                listPrice: 1_000_000m,
                newPrice: 450_000m,
                actor: "checker"));

        Assert.Equal(
            PriceRiskLevel.High,
            assessment.Level);
        Assert.True(assessment.RequiresSecondApprover);
        Assert.True(assessment.CanCurrentActorConfirm);
    }

    [Fact]
    public void Assess_prevents_creator_from_confirming_high_risk_draft()
    {
        var assessment = PriceRiskPolicy.Assess(
            CreateInput(
                listPrice: 1_000_000m,
                newPrice: 450_000m,
                actor: "maker"));

        Assert.False(assessment.CanCurrentActorConfirm);
        Assert.Contains(
            assessment.Issues,
            issue =>
                issue.Code == "SECOND_APPROVER_REQUIRED");
    }

    [Fact]
    public void Assess_flags_promotion_that_does_not_reduce_list_price()
    {
        var input = CreateInput(
            listPrice: 1_000_000m,
            newPrice: 1_050_000m,
            actor: "checker") with
        {
            SourceType =
                PriceChangeSourceType.Promotion
        };

        var assessment =
            PriceRiskPolicy.Assess(input);

        Assert.True(assessment.HasBlockingIssues);
        Assert.Contains(
            assessment.Issues,
            issue =>
                issue.Code
                    == "PROMOTION_NOT_LOWER_THAN_LIST");
    }

    private static PriceRiskCampaignInput CreateInput(
        decimal listPrice,
        decimal newPrice,
        string actor)
    {
        return new PriceRiskCampaignInput(
            1,
            PriceCampaignStatus.Draft,
            PriceCampaignMode.FixedWindow,
            PriceChangeSourceType.Manual,
            PriceConflictPolicy.Reject,
            "maker",
            null,
            actor,
            0,
            [
                new PriceRiskItemInput(
                    10,
                    "SKU-10",
                    listPrice,
                    listPrice,
                    newPrice,
                    listPrice,
                    0,
                    false)
            ]);
    }
}
