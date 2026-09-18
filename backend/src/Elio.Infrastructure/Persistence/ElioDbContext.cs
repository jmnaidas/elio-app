using Microsoft.EntityFrameworkCore;

namespace Elio.Infrastructure.Persistence;

public sealed class ElioDbContext(DbContextOptions<ElioDbContext> options) : DbContext(options)
{
    // Entity mappings and migrations begin when Phase 1 introduces a real model.
}
