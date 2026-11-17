namespace Mediateca.Core.Libraries;

/// <summary>Colección de medios de un mismo tipo que apunta a una o varias carpetas.</summary>
public class Library
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public LibraryType Type { get; set; }

    /// <summary>Carpetas que se escanean. En Docker están dentro de /media.</summary>
    public List<string> Paths { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastScannedAt { get; set; }
}
