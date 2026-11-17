using Mediateca.Core.Libraries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mediateca.Infrastructure.Persistence.Configurations;

internal sealed class LibraryConfiguration : IEntityTypeConfiguration<Library>
{
    public void Configure(EntityTypeBuilder<Library> builder)
    {
        builder.ToTable("Libraries");
        builder.Property(l => l.Name).HasMaxLength(100);
        builder.HasIndex(l => l.Name).IsUnique();
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
        // Paths no necesita configuración: Npgsql guarda List<string> como text[].
    }
}
