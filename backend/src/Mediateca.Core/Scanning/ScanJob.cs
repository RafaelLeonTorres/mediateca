namespace Mediateca.Core.Scanning;

/// <summary>Un escaneo de biblioteca y su avance, que Angular consulta mientras corre.</summary>
public class ScanJob
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public ScanStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    public int Removed { get; set; }

    public int Errors { get; set; }

    public string? ErrorMessage { get; set; }
}

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}
