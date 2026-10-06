using System;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.Foundation.Modules.Data;

internal sealed class InstanceLeaseDbContext(DbContextOptions<InstanceLeaseDbContext> options) : DbContext(options)
{
    public const string Schema = "hosting";

    public DbSet<InstanceLease> Leases => Set<InstanceLease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<InstanceLease>(lease =>
        {
            lease.ToTable("instance_lease");
            lease.HasKey(row => row.Id);
            lease.Property(row => row.Id).ValueGeneratedNever();

            // Free from the start, so the first instance takes it at once.
            lease.HasData(new InstanceLease { Id = InstanceLease.Only, Holder = null, ExpiresAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        });
    }
}
