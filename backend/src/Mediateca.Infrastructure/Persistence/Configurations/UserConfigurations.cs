using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mediateca.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder) =>
        builder.Property(u => u.DisplayName).HasMaxLength(100);
}

internal sealed class UserLibraryAccessConfiguration : IEntityTypeConfiguration<UserLibraryAccess>
{
    public void Configure(EntityTypeBuilder<UserLibraryAccess> builder)
    {
        builder.ToTable("UserLibraryAccess");
        builder.HasKey(a => new { a.UserId, a.LibraryId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Library>().WithMany().HasForeignKey(a => a.LibraryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserItemDataConfiguration : IEntityTypeConfiguration<UserItemData>
{
    public void Configure(EntityTypeBuilder<UserItemData> builder)
    {
        builder.ToTable("UserItemData");
        builder.HasKey(d => new { d.UserId, d.ItemId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Cascade);
        // "Continuar viendo": lo último que reprodujo cada usuario.
        builder.HasIndex(d => new { d.UserId, d.LastPlayedAt });
    }
}
