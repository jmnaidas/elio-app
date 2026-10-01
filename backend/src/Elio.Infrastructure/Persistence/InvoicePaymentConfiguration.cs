using Elio.Domain.Invoices;
using Elio.Domain.Organizations;
using Elio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Elio.Infrastructure.Persistence;

public sealed class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> entity)
    {
        entity.Property(x => x.Amount).HasPrecision(14, 2);
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        entity.Property(x => x.Reference).HasMaxLength(160);
        entity.Property(x => x.Notes).HasMaxLength(2000);
        entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Invoice>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.OrganizationId, x.InvoiceId, x.ReceivedAtUtc });
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("CK_InvoicePayments_Amount", "\"Amount\" > 0 AND \"Amount\" <= 999999999999.99");
            t.HasCheckConstraint("CK_InvoicePayments_Currency", "\"Currency\" IN ('PHP', 'USD')");
            t.HasCheckConstraint("CK_InvoicePayments_Method", "\"Method\" IN ('BankTransfer', 'Cash', 'Check', 'Card', 'EWallet', 'Other')");
        });
    }
}
