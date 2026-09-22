using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Elio.Domain.Organizations;
using Elio.Infrastructure.Identity;

namespace Elio.Infrastructure.Persistence;

public sealed class ElioDbContext(DbContextOptions<ElioDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<Organization>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DefaultCurrency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.ToTable(t => t.HasCheckConstraint("CK_Organization_Currency", "\"DefaultCurrency\" IN ('PHP', 'USD')"));
        });
        builder.Entity<Membership>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.OrganizationId }).IsUnique();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
