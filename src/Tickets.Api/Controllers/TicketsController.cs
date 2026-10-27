using Microsoft.AspNetCore.Mvc;
using Tickets.Api.Data;
using Tickets.Api.Models;
using Tickets.Api.Services;

namespace Tickets.Api.Controllers;

[ApiController]
[Route("api")]
public class TicketsController(TicketEventProcessor processor, TicketsDbContext db) : ControllerBase
{
    /// <summary>
    /// Receives a ticket event. Invalid payloads are rejected with 400 by
    /// [ApiController] model validation before this method runs.
    /// </summary>
    [HttpPost("ticket-events")]
    [ProducesResponseType<EventResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostEvent(TicketEventRequest request, CancellationToken ct)
    {
        var result = await processor.ProcessAsync(request, ct);

        return result.Outcome switch
        {
            ProcessOutcome.Applied or ProcessOutcome.Duplicate =>
                Ok(new EventResult(request.EventId!, result.Outcome == ProcessOutcome.Duplicate, TicketResponse.From(result.Ticket!))),
            ProcessOutcome.TicketNotFound =>
                Problem(statusCode: StatusCodes.Status404NotFound, title: $"Ticket '{request.TicketId}' does not exist."),
            ProcessOutcome.TicketAlreadyExists =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: $"Ticket '{request.TicketId}' already exists."),
        };
    }

    [HttpGet("tickets/{ticketId}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(string ticketId, CancellationToken ct)
    {
        var ticket = await db.Tickets.FindAsync([ticketId], ct);
        return ticket is null ? NotFound() : Ok(TicketResponse.From(ticket));
    }
}
