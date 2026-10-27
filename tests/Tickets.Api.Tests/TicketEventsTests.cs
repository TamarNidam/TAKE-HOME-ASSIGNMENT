using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tickets.Api.Data;
using Tickets.Api.Models;

namespace Tickets.Api.Tests;

public class TicketEventsTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object CreatedEvent(string eventId, string ticketId, string occurredAt = "2026-10-27T10:00:00Z") => new
    {
        eventId,
        ticketId,
        type = "TicketCreated",
        status = "Open",
        occurredAt,
        customerId = "Acme Ltd",
        subject = "Cannot log in",
    };

    private static object StatusEvent(string eventId, string ticketId, string status, string occurredAt) => new
    {
        eventId,
        ticketId,
        type = "StatusChanged",
        status,
        occurredAt,
    };

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<string> CreateTicketAsync()
    {
        var ticketId = NewId("T");
        var res = await _client.PostAsJsonAsync("/api/ticket-events", CreatedEvent(NewId("evt"), ticketId));
        res.EnsureSuccessStatusCode();
        return ticketId;
    }

    [Fact]
    public async Task Same_event_id_sent_twice_is_applied_only_once()
    {
        var ticketId = await CreateTicketAsync();
        var eventId = NewId("evt");

        var first = await _client.PostAsJsonAsync("/api/ticket-events",
            StatusEvent(eventId, ticketId, "InProgress", "2026-10-27T11:00:00Z"));
        // Same eventId, but a different (later) payload: must still be ignored.
        var second = await _client.PostAsJsonAsync("/api/ticket-events",
            StatusEvent(eventId, ticketId, "Resolved", "2026-10-27T12:00:00Z"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await first.Content.ReadFromJsonAsync<EventResult>())!.Duplicate);
        Assert.True((await second.Content.ReadFromJsonAsync<EventResult>())!.Duplicate);

        var ticket = await _client.GetFromJsonAsync<TicketResponse>($"/api/tickets/{ticketId}");
        Assert.Equal("InProgress", ticket!.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T11:00:00Z"), ticket.LastUpdatedAt);

        Assert.Equal(1, factory.WithDb(db => db.ProcessedEvents.Count(p => p.EventId == eventId)));
    }

    [Fact]
    public async Task Duplicate_event_id_with_different_ticket_id_returns_original_ticket()
    {
        var ticketId = await CreateTicketAsync();
        var eventId = NewId("evt");

        await _client.PostAsJsonAsync("/api/ticket-events",
            StatusEvent(eventId, ticketId, "InProgress", "2026-10-27T11:00:00Z"));
        // Same eventId, pointing at a ticket that doesn't exist.
        var retry = await _client.PostAsJsonAsync("/api/ticket-events",
            StatusEvent(eventId, NewId("T"), "Resolved", "2026-10-27T12:00:00Z"));

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var result = await retry.Content.ReadFromJsonAsync<EventResult>();
        Assert.True(result!.Duplicate);
        Assert.Equal(ticketId, result.Ticket.TicketId);
        Assert.Equal("InProgress", result.Ticket.Status);
    }

    [Fact]
    public async Task Concurrent_duplicates_are_applied_exactly_once()
    {
        var ticketId = await CreateTicketAsync();
        var eventId = NewId("evt");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            _client.PostAsJsonAsync("/api/ticket-events",
                StatusEvent(eventId, ticketId, "Resolved", "2026-10-27T11:00:00Z"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<EventResult>()));
        Assert.Single(results, r => !r!.Duplicate);
        Assert.Equal(1, factory.WithDb(db => db.ProcessedEvents.Count(p => p.EventId == eventId)));
    }

    [Fact]
    public async Task Database_rejects_a_second_row_with_the_same_event_id()
    {
        var ticketId = await CreateTicketAsync();
        var eventId = NewId("evt");

        ProcessedEvent Row() => new()
        {
            EventId = eventId, TicketId = ticketId, Type = "StatusChanged", Status = "Open",
            OccurredAt = DateTimeOffset.UtcNow, ProcessedAt = DateTimeOffset.UtcNow,
        };

        factory.WithDb(db => { db.ProcessedEvents.Add(Row()); return db.SaveChanges(); });

        Assert.Throws<DbUpdateException>(() =>
            factory.WithDb(db => { db.ProcessedEvents.Add(Row()); return db.SaveChanges(); }));
    }

    [Fact]
    public async Task Older_event_does_not_roll_back_status()
    {
        var ticketId = await CreateTicketAsync();

        await _client.PostAsJsonAsync("/api/ticket-events", StatusEvent(NewId("evt"), ticketId, "Resolved", "2026-10-27T12:00:00Z"));
        var stale = await _client.PostAsJsonAsync("/api/ticket-events", StatusEvent(NewId("evt"), ticketId, "InProgress", "2026-10-27T11:00:00Z"));

        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        var ticket = await _client.GetFromJsonAsync<TicketResponse>($"/api/tickets/{ticketId}");
        Assert.Equal("Resolved", ticket!.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T12:00:00Z"), ticket.LastUpdatedAt);
    }

    [Fact]
    public async Task Created_twice_with_different_event_ids_returns_409_and_keeps_status()
    {
        var ticketId = await CreateTicketAsync();
        await _client.PostAsJsonAsync("/api/ticket-events",
            StatusEvent(NewId("evt"), ticketId, "InProgress", "2026-10-27T11:00:00Z"));
        var secondCreateId = NewId("evt");

        var res = await _client.PostAsJsonAsync("/api/ticket-events", CreatedEvent(secondCreateId, ticketId));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var ticket = await _client.GetFromJsonAsync<TicketResponse>($"/api/tickets/{ticketId}");
        Assert.Equal("InProgress", ticket!.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T11:00:00Z"), ticket.LastUpdatedAt);
        Assert.Equal(0, factory.WithDb(db => db.ProcessedEvents.Count(p => p.EventId == secondCreateId)));
    }

    [Fact]
    public async Task Post_and_get_return_identical_timestamps()
    {
        var ticketId = NewId("T");
        const string occurredAt = "2026-10-27T15:30:00.1234567+03:00";

        var post = await _client.PostAsJsonAsync("/api/ticket-events", CreatedEvent(NewId("evt"), ticketId, occurredAt));
        var fromPost = (await post.Content.ReadFromJsonAsync<EventResult>())!.Ticket.LastUpdatedAt;
        var fromGet = (await _client.GetFromJsonAsync<TicketResponse>($"/api/tickets/{ticketId}"))!.LastUpdatedAt;

        // Compare the "O" strings: DateTimeOffset equality ignores the offset.
        Assert.Equal(occurredAt, fromPost.ToString("O"));
        Assert.Equal(occurredAt, fromGet.ToString("O"));
    }

    public static TheoryData<string, object> InvalidEvents => new()
    {
        { "missing eventId", new { ticketId = "T-1", type = "StatusChanged", status = "InProgress", occurredAt = "2026-10-27T10:00:00Z" } },
        { "missing ticketId", new { eventId = "e-1", type = "StatusChanged", status = "InProgress", occurredAt = "2026-10-27T10:00:00Z" } },
        { "missing occurredAt", new { eventId = "e-1", ticketId = "T-1", type = "StatusChanged", status = "InProgress" } },
        { "empty status", new { eventId = "e-1", ticketId = "T-1", type = "StatusChanged", status = "", occurredAt = "2026-10-27T10:00:00Z" } },
        { "unknown status", new { eventId = "e-1", ticketId = "T-1", type = "StatusChanged", status = "Banana", occurredAt = "2026-10-27T10:00:00Z" } },
        { "unknown type", new { eventId = "e-1", ticketId = "T-1", type = "TicketDeleted", status = "Open", occurredAt = "2026-10-27T10:00:00Z" } },
        { "create without customerId", new { eventId = "e-1", ticketId = "T-1", type = "TicketCreated", status = "Open", occurredAt = "2026-10-27T10:00:00Z", subject = "x" } },
        { "create with status other than Open", new { eventId = "e-1", ticketId = "T-1", type = "TicketCreated", status = "InProgress", occurredAt = "2026-10-27T10:00:00Z", customerId = "c", subject = "x" } },
        { "status change to Open", new { eventId = "e-1", ticketId = "T-1", type = "StatusChanged", status = "Open", occurredAt = "2026-10-27T10:00:00Z" } },
        { "whitespace eventId", new { eventId = "   ", ticketId = "T-1", type = "StatusChanged", status = "InProgress", occurredAt = "2026-10-27T10:00:00Z" } },
        { "whitespace ticketId", new { eventId = "e-1", ticketId = "   ", type = "StatusChanged", status = "InProgress", occurredAt = "2026-10-27T10:00:00Z" } },
        { "whitespace customerId", new { eventId = "e-1", ticketId = "T-1", type = "TicketCreated", status = "Open", occurredAt = "2026-10-27T10:00:00Z", customerId = "   ", subject = "x" } },
        { "whitespace subject", new { eventId = "e-1", ticketId = "T-1", type = "TicketCreated", status = "Open", occurredAt = "2026-10-27T10:00:00Z", customerId = "c", subject = "   " } },
    };

    [Theory]
    [MemberData(nameof(InvalidEvents))]
    public async Task Invalid_event_is_rejected_and_not_stored(string _, object payload)
    {
        var res = await _client.PostAsJsonAsync("/api/ticket-events", payload);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, factory.WithDb(db => db.ProcessedEvents.Count(p => p.EventId == "e-1")));
    }

    [Fact]
    public async Task Status_change_for_unknown_ticket_returns_404()
    {
        var eventId = NewId("evt");
        var ticketId = NewId("T");

        var res = await _client.PostAsJsonAsync("/api/ticket-events",
            StatusEvent(eventId, ticketId, "InProgress", "2026-10-27T10:00:00Z"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(0, factory.WithDb(db => db.ProcessedEvents.Count(p => p.EventId == eventId)));
        Assert.Null(factory.WithDb(db => db.Tickets.Find(ticketId)));
    }

    [Fact]
    public async Task Get_returns_ticket_fields()
    {
        var ticketId = await CreateTicketAsync();

        var ticket = await _client.GetFromJsonAsync<TicketResponse>($"/api/tickets/{ticketId}");

        Assert.Equal(new TicketResponse(ticketId, "Acme Ltd", "Cannot log in", "Open",
            DateTimeOffset.Parse("2026-10-27T10:00:00Z")), ticket);
    }

    [Fact]
    public async Task Get_unknown_ticket_returns_404()
    {
        var res = await _client.GetAsync($"/api/tickets/{NewId("T")}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
