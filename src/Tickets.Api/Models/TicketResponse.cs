using Tickets.Api.Data;

namespace Tickets.Api.Models;

public record TicketResponse(string TicketId, string CustomerId, string Subject, string Status, DateTimeOffset LastUpdatedAt)
{
    public static TicketResponse From(Ticket t) => new(t.TicketId, t.CustomerId, t.Subject, t.Status, t.LastUpdatedAt);
}

public record EventResult(string EventId, bool Duplicate, TicketResponse Ticket);
