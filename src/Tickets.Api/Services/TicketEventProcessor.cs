using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tickets.Api.Data;
using Tickets.Api.Models;

namespace Tickets.Api.Services;

public enum ProcessOutcome { Applied, Duplicate, TicketNotFound, TicketAlreadyExists }

public record ProcessResult(ProcessOutcome Outcome, Ticket? Ticket = null);

public class TicketEventProcessor(TicketsDbContext db, TimeProvider clock, ILogger<TicketEventProcessor> logger)
{
    private const int SqliteConstraintError = 19;

    /// <summary>
    /// Applies an already-validated event. The processed-event row and the ticket
    /// change are written in a single SaveChanges (one transaction), so either both
    /// land or neither does. The EventId primary key guarantees that a repeated
    /// event can never be applied twice, even under concurrent requests.
    /// </summary>
    public async Task<ProcessResult> ProcessAsync(TicketEventRequest e, CancellationToken ct = default)
    {
        var result = await ApplyAsync(e, ct);
        logger.LogInformation("Event {EventId} for ticket {TicketId}: {Outcome}", e.EventId, e.TicketId, result.Outcome);
        return result;
    }

    private async Task<ProcessResult> ApplyAsync(TicketEventRequest e, CancellationToken ct)
    {
        var eventId = e.EventId!;
        var ticketId = e.TicketId!;
        var occurredAt = e.OccurredAt!.Value;

        // Fast path for the common retry case. The DB constraint below is what
        // actually guarantees correctness if two copies race past this check.
        if (await FindDuplicateAsync(eventId, ct) is { } duplicate)
            return duplicate;

        var ticket = await db.Tickets.FindAsync([ticketId], ct);

        if (e.Type == EventTypes.TicketCreated)
        {
            if (ticket is not null)
                return new ProcessResult(ProcessOutcome.TicketAlreadyExists, ticket);

            ticket = new Ticket
            {
                TicketId = ticketId,
                CustomerId = e.CustomerId!,
                Subject = e.Subject!,
                Status = TicketStatuses.Open,
                LastUpdatedAt = occurredAt,
            };
            db.Tickets.Add(ticket);
        }
        else
        {
            if (ticket is null)
                return new ProcessResult(ProcessOutcome.TicketNotFound);

            // Ignore out-of-order events so an older event can't roll the status back.
            // The event is still recorded as processed.
            if (occurredAt >= ticket.LastUpdatedAt)
            {
                ticket.Status = e.Status!;
                ticket.LastUpdatedAt = occurredAt;
            }
        }

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            TicketId = ticketId,
            Type = e.Type!,
            Status = e.Status!,
            OccurredAt = occurredAt,
            ProcessedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return new ProcessResult(ProcessOutcome.Applied, ticket);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError })
        {
            // Another request won the race. Nothing from this request was written.
            db.ChangeTracker.Clear();

            if (await FindDuplicateAsync(eventId, ct) is { } raced)
                return raced;

            var current = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == ticketId, ct);
            return new ProcessResult(ProcessOutcome.TicketAlreadyExists, current);
        }
    }

    /// <summary>
    /// Returns a Duplicate result if the event was already processed. The ticket is
    /// looked up by the TicketId stored with the original event, not the one in the
    /// retried request, which may differ.
    /// </summary>
    private async Task<ProcessResult?> FindDuplicateAsync(string eventId, CancellationToken ct)
    {
        var originalTicketId = await db.ProcessedEvents
            .Where(p => p.EventId == eventId)
            .Select(p => p.TicketId)
            .FirstOrDefaultAsync(ct);

        if (originalTicketId is null)
            return null;

        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == originalTicketId, ct);
        return new ProcessResult(ProcessOutcome.Duplicate, ticket);
    }
}
