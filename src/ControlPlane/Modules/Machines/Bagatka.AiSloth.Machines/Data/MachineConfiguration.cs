using Bagatka.AiSloth.Machines.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Machines.Data;

internal sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.HasKey(machine => machine.Id);
        builder.Property(machine => machine.Version).IsRowVersion();
        builder.Ignore(machine => machine.IsRegistered);

        // Lists a workspace's machines, and finds the machine a registration code belongs to.
        builder.HasIndex(machine => new { machine.WorkspaceId, machine.Id });
        builder.HasIndex(machine => machine.CodeHash).IsUnique();
    }
}
