using Elio.Domain.Clients;
using Elio.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Elio.Infrastructure.Persistence;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> entity)
    {
        entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => new { x.OrganizationId, x.IsActive, x.Name });
        entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Clients_Currency", "\"Currency\" IN ('PHP', 'USD')");
            t.HasCheckConstraint("CK_Clients_Name", "length(btrim(\"Name\")) > 0");
        });
        entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
        entity.Property(x => x.Phone).HasMaxLength(50);
        entity.Property(x => x.BillingAddress).HasMaxLength(1000);
        entity.Property(x => x.Notes).HasMaxLength(2000);
        entity.ToTable(t => t.HasCheckConstraint("CK_Clients_Email", "length(btrim(\"Email\")) > 0"));
    }
}
