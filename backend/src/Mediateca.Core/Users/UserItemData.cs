namespace Mediateca.Core.Users;

/// <summary>Progreso de un usuario en un elemento: dónde se quedó, si ya lo vio y cuándo lo reprodujo.</summary>
public class UserItemData
{
    public Guid UserId { get; set; }

    public Guid ItemId { get; set; }

    public double PositionSeconds { get; set; }

    public bool Played { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }
}
