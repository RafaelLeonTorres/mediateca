namespace Mediateca.Core.Media;

/// <summary>Estado de emisión de una serie o película según su proveedor de metadatos.</summary>
public enum ReleaseStatus
{
    /// <summary>En emisión ("2026 – Actualidad").</summary>
    Airing,

    Finished,

    /// <summary>Todavía no sale.</summary>
    Upcoming,

    Cancelled,

    /// <summary>En pausa.</summary>
    Hiatus,
}
