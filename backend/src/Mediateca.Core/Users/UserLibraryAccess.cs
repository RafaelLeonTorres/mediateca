namespace Mediateca.Core.Users;

/// <summary>Permiso de un usuario para ver una biblioteca. El administrador ve todas sin necesitarlo.</summary>
public class UserLibraryAccess
{
    public Guid UserId { get; set; }

    public Guid LibraryId { get; set; }
}
