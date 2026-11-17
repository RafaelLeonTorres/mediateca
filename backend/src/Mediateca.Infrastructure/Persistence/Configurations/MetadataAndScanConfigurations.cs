using Mediateca.Core.Libraries;
using Mediateca.Core.Metadata;
using Mediateca.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mediateca.Infrastructure.Persistence.Configurations;

internal sealed class MetadataCacheEntryConfiguration : IEntityTypeConfiguration<MetadataCacheEntry>
{
    public void Configure(EntityTypeBuilder<MetadataCacheEntry> builder)
    {
        builder.ToTable("MetadataCache");
        builder.HasKey(c => new { c.Provider, c.ExternalId });
        builder.Property(c => c.Provider).HasMaxLength(20);
        builder.Property(c => c.ExternalId).HasMaxLength(50);
        builder.Property(c => c.Payload).HasColumnType("jsonb");
    }
}

internal sealed class ScanJobConfiguration : IEntityTypeConfiguration<ScanJob>
{
    public void Configure(EntityTypeBuilder<ScanJob> builder)
    {
        builder.ToTable("ScanJobs");
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Library>().WithMany().HasForeignKey(j => j.LibraryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(j => new { j.LibraryId, j.CreatedAt });
    }
}
