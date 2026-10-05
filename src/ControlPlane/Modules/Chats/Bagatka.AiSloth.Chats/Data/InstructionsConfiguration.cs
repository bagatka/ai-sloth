using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class InstructionsConfiguration : IEntityTypeConfiguration<WorkspaceInstructions>, IEntityTypeConfiguration<PersonalInstructions>
{
    public void Configure(EntityTypeBuilder<WorkspaceInstructions> builder)
    {
        builder.ToTable("workspace_instructions");
        builder.HasKey(instructions => instructions.WorkspaceId);
        builder.Property(instructions => instructions.Text).HasMaxLength(AgentInstructions.MaxLength);
    }

    public void Configure(EntityTypeBuilder<PersonalInstructions> builder)
    {
        builder.ToTable("personal_instructions");
        builder.HasKey(instructions => instructions.PersonId);
        builder.Property(instructions => instructions.Text).HasMaxLength(AgentInstructions.MaxLength);
    }
}
