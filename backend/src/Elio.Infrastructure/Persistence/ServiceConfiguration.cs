using Elio.Domain.Services;
using Elio.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Elio.Infrastructure.Persistence;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> entity)
    {
        entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => new { x.OrganizationId, x.IsActive, x.Name });
        entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Services_Currency", "\"Currency\" IN ('PHP', 'USD')");
            t.HasCheckConstraint("CK_Services_Name", "length(btrim(\"Name\")) > 0");
        });
        entity.Property(x => x.Description).HasMaxLength(2000);
        entity.Property(x => x.DefaultUnitPrice).HasPrecision(14, 2);
        entity.ToTable(t => t.HasCheckConstraint("CK_Services_Price", "\"DefaultUnitPrice\" >= 0"));
    }
}
