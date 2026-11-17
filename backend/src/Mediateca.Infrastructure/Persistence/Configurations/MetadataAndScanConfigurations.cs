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

        // Un solo escaneo activo por biblioteca, garantizado por la base de datos: si dos solicitudes
        // intentan crear uno a la vez, la segunda falla con una violación de índice único.
        builder.HasIndex(j => j.LibraryId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Queued', 'Running')")
            .HasDatabaseName("IX_ScanJobs_LibraryId_Active");
    }
}

internal sealed class ScanScheduleConfiguration : IEntityTypeConfiguration<ScanSchedule>
{
    public void Configure(EntityTypeBuilder<ScanSchedule> builder)
    {
        builder.ToTable("ScanSchedules");
        // Una sola fila con Id = 1: el id lo pone la app, no la base de datos.
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.CronExpression).HasMaxLength(120);
    }
}
