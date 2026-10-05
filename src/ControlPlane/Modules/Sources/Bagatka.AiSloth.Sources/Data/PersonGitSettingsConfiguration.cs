using Bagatka.AiSloth.Sources.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Sources.Data;

internal sealed class PersonGitSettingsConfiguration : IEntityTypeConfiguration<PersonGitSettings>
{
    public void Configure(EntityTypeBuilder<PersonGitSettings> builder)
    {
        builder.ToTable("git_settings");
        builder.HasKey(settings => settings.UserId);
        builder.Property(settings => settings.AuthorName).HasMaxLength(GitNames.MaxNameLength);
        builder.Property(settings => settings.AuthorEmail).HasMaxLength(GitNames.MaxEmailLength);
        builder.Property(settings => settings.CommitterName).HasMaxLength(GitNames.MaxNameLength);
        builder.Property(settings => settings.CommitterEmail).HasMaxLength(GitNames.MaxEmailLength);
        builder.Property(settings => settings.BranchPrefix).HasMaxLength(GitNames.MaxPrefixLength);
        builder.Ignore(settings => settings.Author);
        builder.Ignore(settings => settings.Committer);
    }
}
