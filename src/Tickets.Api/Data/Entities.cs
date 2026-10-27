namespace Tickets.Api.Data;

public class Ticket
{
    public required string TicketId { get; set; }
    public required string CustomerId { get; set; }
    public required string Subject { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

/// <summary>
/// One row per event that has been applied. EventId is the primary key, so the
/// database itself rejects a second insert of the same event.
/// </summary>
public class ProcessedEvent
{
    public required string EventId { get; set; }
    public required string TicketId { get; set; }
    public required string Type { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
