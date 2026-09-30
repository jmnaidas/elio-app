using Elio.Domain.Invoices;
using Elio.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Elio.Infrastructure.Persistence;

public sealed class InvoiceDeliveryConfiguration : IEntityTypeConfiguration<InvoiceDelivery>
{
    public void Configure(EntityTypeBuilder<InvoiceDelivery> entity)
    {
        entity.Property(x => x.RecipientEmail).HasMaxLength(254).IsRequired();
        entity.Property(x => x.Channel).HasMaxLength(40).IsRequired();
        entity.Property(x => x.FailureCode).HasMaxLength(40);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Invoice>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.OrganizationId, x.InvoiceId, x.AttemptedAtUtc });
        entity.HasIndex(x => new { x.OrganizationId, x.InvoiceId }).IsUnique().HasFilter("\"Status\" = 'Pending'");
        entity.ToTable(t => t.HasCheckConstraint("CK_InvoiceDeliveries_State", """
            ("Status" = 'Pending' AND "SentAtUtc" IS NULL AND "FailureCode" IS NULL)
            OR ("Status" = 'Sent' AND "SentAtUtc" IS NOT NULL AND "SentAtUtc" >= "AttemptedAtUtc" AND "FailureCode" IS NULL)
            OR ("Status" = 'Failed' AND "SentAtUtc" IS NULL AND "FailureCode" IN ('provider_unavailable', 'delivery_failed', 'pdf_failed') AND "FailureCode" IS NOT NULL)
            """));
    }
}
