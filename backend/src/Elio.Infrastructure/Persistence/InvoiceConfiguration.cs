using Elio.Domain.Invoices;
using Elio.Domain.Clients;
using Elio.Domain.Services;
using Elio.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Elio.Infrastructure.Persistence;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> entity)
    {
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(4000);
        entity.Property(x => x.PaymentInstructions).HasMaxLength(4000);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Ignore(x => x.Subtotal); entity.Ignore(x => x.Total);
        entity.Property(x => x.InvoiceNumber).HasMaxLength(32);
        entity.Property(x => x.SellerName).HasMaxLength(120);
        entity.Property(x => x.SellerTimeZone).HasMaxLength(100);
        entity.Property(x => x.IssuedClientName).HasMaxLength(160);
        entity.Property(x => x.IssuedClientEmail).HasMaxLength(254);
        entity.Property(x => x.IssuedClientPhone).HasMaxLength(50);
        entity.Property(x => x.IssuedBillingAddress).HasMaxLength(1000);
        entity.Property(x => x.FinalizedSubtotal).HasPrecision(14, 2);
        entity.Property(x => x.FinalizedTotal).HasPrecision(14, 2);
        entity.HasIndex(x => new { x.OrganizationId, x.SequenceValue }).IsUnique();
        entity.HasIndex(x => new { x.OrganizationId, x.InvoiceNumber }).IsUnique();
        entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.HasIndex(x => new { x.OrganizationId, x.Lifecycle, x.UpdatedAtUtc });
        entity.HasIndex(x => new { x.OrganizationId, x.ClientId, x.Currency });
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Invoices_Currency", "\"Currency\" IN ('PHP', 'USD')");
            t.HasCheckConstraint("CK_Invoices_Lifecycle", "\"Lifecycle\" IN ('Draft', 'Finalized', 'Void')");
            t.HasCheckConstraint("CK_Invoices_Dates", "\"DueDate\" >= \"IssueDate\"");
            t.HasCheckConstraint("CK_Invoices_Issuance", """
                ("Lifecycle" = 'Draft' AND "SequenceValue" IS NULL AND "InvoiceNumber" IS NULL AND "FinalizedAtUtc" IS NULL
                 AND "SellerName" IS NULL AND "SellerTimeZone" IS NULL AND "IssuedClientName" IS NULL AND "IssuedClientEmail" IS NULL
                 AND "IssuedClientPhone" IS NULL AND "IssuedBillingAddress" IS NULL AND "IssuedClientIsActive" IS NULL
                 AND "FinalizedSubtotal" IS NULL AND "FinalizedTotal" IS NULL)
                OR ("Lifecycle" IN ('Finalized', 'Void') AND "SequenceValue" IS NOT NULL AND "SequenceValue" > 0
                 AND "InvoiceNumber" IS NOT NULL AND "InvoiceNumber" = 'INV-' || lpad("SequenceValue"::text, greatest(6, length("SequenceValue"::text)), '0')
                 AND "FinalizedAtUtc" IS NOT NULL AND "SellerName" IS NOT NULL AND "SellerTimeZone" IS NOT NULL
                 AND "IssuedClientName" IS NOT NULL AND "IssuedClientEmail" IS NOT NULL AND "IssuedClientIsActive" IS NOT NULL
                 AND "FinalizedSubtotal" IS NOT NULL AND "FinalizedTotal" IS NOT NULL
                 AND "FinalizedSubtotal" >= 0 AND "FinalizedTotal" = "FinalizedSubtotal")
                """);
        });
    }
}
public sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> entity)
    {
        entity.HasKey(x => x.OrganizationId);
        entity.Property(x => x.OrganizationId).ValueGeneratedNever();
        entity.HasOne<Organization>().WithOne().HasForeignKey<InvoiceSequence>(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        entity.ToTable("InvoiceSequences", t => t.HasCheckConstraint("CK_InvoiceSequences_Value", "\"LastValue\" > 0"));
    }
}
public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> entity)
    {
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(10, 4);
        entity.Property(x => x.UnitPrice).HasPrecision(14, 2);
        entity.Property(x => x.LineTotal).HasPrecision(14, 2);
        entity.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.InvoiceId, x.SortOrder });
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("CK_InvoiceLines_Description", "length(btrim(\"Description\")) > 0");
            t.HasCheckConstraint("CK_InvoiceLines_Amounts", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"LineTotal\" = round(\"Quantity\" * \"UnitPrice\", 2)");
            t.HasCheckConstraint("CK_InvoiceLines_Order", "\"SortOrder\" >= 0");
        });
    }
}
