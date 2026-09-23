namespace Elio.Domain.Catalog;

internal static class CatalogRules
{
    public static string Required(string? value, int max, string label)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > max) throw new ArgumentException($"{label} is required and must be at most {max} characters.");
        return value;
    }
    public static string? Optional(string? value, int max, string label)
    {
        value = value?.Trim();
        if (value?.Length > max) throw new ArgumentException($"{label} must be at most {max} characters.");
        return string.IsNullOrEmpty(value) ? null : value;
    }
    public static string Currency(string value) => value is "PHP" or "USD" ? value : throw new ArgumentException("Choose PHP or USD.");
}
