using System.ComponentModel.DataAnnotations;
namespace Elio.Application.Catalog;

public sealed class CatalogQuery
{
    [MaxLength(160)] public string? Search { get; init; }
    [RegularExpression("^(active|inactive|all)$")] public string Status { get; init; } = "active";
}
public sealed record StatusRequest([Required] Guid? Version);
