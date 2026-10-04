using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Machines.Data;

internal sealed class MachinesDbContext(DbContextOptions<MachinesDbContext> options) : DbContext(options)
{
    public const string Schema = "machines";

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<Placement> Placements => Set<Placement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new MachineConfiguration());
        modelBuilder.ApplyConfiguration(new PlacementConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<MachineId>().HaveConversion<TypedIdConverter<MachineId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<MachineName>()
            .HaveConversion<MachineNameConverter>()
            .HaveMaxLength(MachineName.MaxLength);
    }
}
