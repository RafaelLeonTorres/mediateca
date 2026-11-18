using Mediateca.Core.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mediateca.Infrastructure.Persistence.Configurations;

/// <summary>
/// Jerarquía de MediaItem con TPH: una sola tabla MediaItems y la columna Kind con el tipo.
/// Las columnas de los tipos derivados (FilePath, TrackNumber…) quedan como opcionales.
/// </summary>
internal sealed class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.ToTable("MediaItems");

        builder.HasDiscriminator<string>("Kind")
            .HasValue<Movie>("Movie")
            .HasValue<Series>("Series")
            .HasValue<Season>("Season")
            .HasValue<Episode>("Episode")
            .HasValue<MusicAlbum>("MusicAlbum")
            .HasValue<Track>("Track");
        builder.Property<string>("Kind").HasMaxLength(20);

        builder.Property(m => m.Title).HasMaxLength(500);
        builder.Property(m => m.SortTitle).HasMaxLength(500);
        builder.Property(m => m.ContentRating).HasMaxLength(20);
        builder.Property(m => m.ImdbId).HasMaxLength(20);
        builder.Property(m => m.MetadataStatus).HasConversion<string>().HasMaxLength(20);

        // Metadatos de un proveedor (fase 4). Genres, Studios, Tags, Directors y Actors son text[] por convención.
        builder.Property(m => m.OriginalTitle).HasMaxLength(500);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.MetadataMessage).HasMaxLength(500);
        builder.Property(m => m.ScanKey).HasMaxLength(600);
        builder.Property(m => m.ExternalLinksJson).HasColumnType("jsonb");

        builder.HasOne(m => m.Library)
            .WithMany()
            .HasForeignKey(m => m.LibraryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Parent)
            .WithMany(m => m.Children)
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Búsqueda por título: índice GIN con trigramas para ILIKE '%texto%'.
        builder.HasIndex(m => m.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
        // "Agregado recientemente" por biblioteca.
        builder.HasIndex(m => new { m.LibraryId, m.DateAdded });
        // La cola de metadatos y los contadores de la administración filtran por estado.
        builder.HasIndex(m => new { m.LibraryId, m.MetadataStatus });
    }
}

internal sealed class PlayableItemConfiguration : IEntityTypeConfiguration<PlayableItem>
{
    public void Configure(EntityTypeBuilder<PlayableItem> builder)
    {
        builder.HasBaseType<MediaItem>();
        // Un archivo se registra una sola vez por biblioteca.
        builder.HasIndex(p => new { p.LibraryId, p.FilePath }).IsUnique();
    }
}

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder) => builder.HasBaseType<PlayableItem>();
}

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder) => builder.HasBaseType<MediaItem>();
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.HasBaseType<MediaItem>();
        // Temporadas y episodios comparten la columna SeasonNumber.
        builder.Property(s => s.SeasonNumber).HasColumnName("SeasonNumber");
    }
}

internal sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.HasBaseType<PlayableItem>();
        builder.Property(e => e.SeasonNumber).HasColumnName("SeasonNumber");
    }
}

internal sealed class MusicAlbumConfiguration : IEntityTypeConfiguration<MusicAlbum>
{
    public void Configure(EntityTypeBuilder<MusicAlbum> builder)
    {
        builder.HasBaseType<MediaItem>();
        builder.Property(a => a.AlbumArtist).HasMaxLength(300);
    }
}

internal sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.HasBaseType<PlayableItem>();
        builder.Property(t => t.Artist).HasMaxLength(300);
    }
}
