using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class ProcessConfiguration : IEntityTypeConfiguration<Process>
{
    public void Configure(EntityTypeBuilder<Process> builder)
    {
        builder.HasKey(process => process.Id);
        builder.Property(process => process.Command).HasMaxLength(Process.MaxPathLength);
        builder.Property(process => process.WorkingDirectory).HasMaxLength(Process.MaxPathLength);

        // Lists a nook's processes.
        builder.HasIndex(process => new { process.NookId, process.Id });
    }
}
