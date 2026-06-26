using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WebApplication2.Areas.Admin.ViewModels.Validation;

/// <summary>
/// Validates monetary values without relying on culture-sensitive string conversion.
/// </summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false)]
public sealed class MoneyRangeAttribute : ValidationAttribute, IClientModelValidator
{
    public const decimal Minimum = 0.01m;
    public const decimal Maximum = 9999999999999999m;

    public MoneyRangeAttribute()
    {
        ErrorMessage = "Giá phải nằm trong khoảng hợp lệ.";
    }

    protected override ValidationResult? IsValid(
        object? value,
        ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is decimal amount &&
            amount >= Minimum &&
            amount <= Maximum)
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            FormatErrorMessage(validationContext.DisplayName));
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MergeAttribute(context.Attributes, "data-val", "true");
        MergeAttribute(
            context.Attributes,
            "data-val-range",
            FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        MergeAttribute(
            context.Attributes,
            "data-val-range-min",
            Minimum.ToString(CultureInfo.InvariantCulture));
        MergeAttribute(
            context.Attributes,
            "data-val-range-max",
            Maximum.ToString(CultureInfo.InvariantCulture));
    }

    private static void MergeAttribute(
        IDictionary<string, string> attributes,
        string key,
        string value)
    {
        if (!attributes.ContainsKey(key))
        {
            attributes.Add(key, value);
        }
    }
}
