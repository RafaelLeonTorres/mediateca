using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;
using Mediateca.Core.Scanning;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Infrastructure.Persistence;

/// <summary>
/// Base de datos de Mediateca (Code First). Incluye las tablas de Identity (usuarios y roles con
/// llaves Guid) y las llaves de Data Protection que cifran la cookie de sesión.
/// </summary>
public sealed class MediatecaDbContext(DbContextOptions<MediatecaDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Library> Libraries => Set<Library>();

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    public DbSet<UserLibraryAccess> LibraryAccess => Set<UserLibraryAccess>();

    public DbSet<UserItemData> ItemProgress => Set<UserItemData>();

    public DbSet<MetadataCacheEntry> MetadataCache => Set<MetadataCacheEntry>();

    public DbSet<ScanJob> ScanJobs => Set<ScanJob>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Extensión de PostgreSQL para que ILIKE '%texto%' use un índice (búsqueda por título).
        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(MediatecaDbContext).Assembly);
    }
}
