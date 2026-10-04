using Bagatka.AiSloth.Machines.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Machines.Data;

internal sealed class PlacementConfiguration : IEntityTypeConfiguration<Placement>
{
    public void Configure(EntityTypeBuilder<Placement> builder)
    {
        builder.HasKey(placement => placement.Key);
        builder.HasIndex(placement => placement.MachineId);
    }
}
