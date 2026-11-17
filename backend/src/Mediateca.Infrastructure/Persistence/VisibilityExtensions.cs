using Mediateca.Core.Libraries;
using Mediateca.Core.Media;

namespace Mediateca.Infrastructure.Persistence;

/// <summary>
/// Regla de visibilidad (PLAN.md, sección 6), en un solo lugar: el administrador ve todas las
/// bibliotecas y los demás usuarios, solo las que aparecen en <c>UserLibraryAccess</c>. Todas las
/// consultas de los endpoints para usuarios parten de aquí; ninguna filtra por su cuenta.
/// </summary>
public static class VisibilityExtensions
{
    /// <summary>Bibliotecas que el usuario puede ver.</summary>
    public static IQueryable<Library> VisibleLibraries(this MediatecaDbContext db, Guid userId, bool isAdmin) =>
        isAdmin
            ? db.Libraries
            : db.Libraries.Where(library =>
                db.LibraryAccess.Any(access => access.UserId == userId && access.LibraryId == library.Id));

    /// <summary>Elementos (de cualquier tipo) de las bibliotecas que el usuario puede ver.</summary>
    public static IQueryable<MediaItem> VisibleItems(this MediatecaDbContext db, Guid userId, bool isAdmin) =>
        isAdmin
            ? db.MediaItems
            : db.MediaItems.Where(item =>
                db.LibraryAccess.Any(access => access.UserId == userId && access.LibraryId == item.LibraryId));
}
