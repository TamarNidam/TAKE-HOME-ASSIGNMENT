# Tickets Service

ASP.NET Core (.NET 8, Controllers) + SQLite (EF Core). Requires the .NET 8 SDK or later.

## Run

```
dotnet run --project src/Tickets.Api      # http://localhost:5033, creates tickets.db
dotnet test TicketsService.sln
```

`src/Tickets.Api/Tickets.Api.http` has sample requests.

Swagger UI: /swagger (Development only)

## Endpoints

### `POST /api/ticket-events`

```json
{
  "eventId": "evt-1",
  "ticketId": "T-100",
  "type": "TicketCreated",          // TicketCreated | StatusChanged
  "status": "Open",                 // Open for TicketCreated; InProgress | Resolved for StatusChanged
  "occurredAt": "2026-10-27T10:00:00Z",
  "customerId": "Acme Ltd",           // required for TicketCreated
  "subject": "Cannot log in"        // required for TicketCreated
}
```

| Result | Response |
|---|---|
| Applied | `200` `{ eventId, duplicate: false, ticket }` |
| Same `eventId` already processed | `200` `{ eventId, duplicate: true, ticket }` with no changes |
| Missing or invalid field / `type` / `status` | `400` validation problem details |
| `StatusChanged` for an unknown ticket | `404` |
| `TicketCreated` for an existing ticket | `409` |

### `GET /api/tickets/{ticketId}`

Returns `{ ticketId, customerId, subject, status, lastUpdatedAt }` or `404`.

## Idempotency

- `ProcessedEvents.EventId` is the table's **primary key**, so SQLite itself rejects a second row with the same id.
- The `ProcessedEvents` insert and the ticket change go through one `SaveChanges`, which is one transaction. Either both are written or neither is.
- A pre-check returns early for ordinary retries. If two copies of an event race past the check, the losing insert fails on the PK constraint, its transaction rolls back, and the request returns `duplicate: true`.
- Out-of-order events (`occurredAt` older than the ticket's `lastUpdatedAt`) are recorded as processed but do not change the status.

## Assumptions

- A duplicate `eventId` returns `200` with `duplicate: true` and the ticket's **current** state, which may differ from the first response if later events were applied.
- The same `eventId` with a different payload is treated as a duplicate: the new payload is ignored, not compared or rejected.
- Transitions: a `StatusChanged` event may set `InProgress` or `Resolved` from any status; when events disagree, the one with the latest `occurredAt` wins.
- A `StatusChanged` event to the ticket's current status returns `200`; if its `occurredAt` is newer, `lastUpdatedAt` moves forward to it.
- `type` and `status` are case-sensitive: `"open"` or `"ticketcreated"` returns `400`.
- A request with `Content-Type: text/plain` returns `415 Unsupported Media Type`, which is the correct HTTP status for a body the API can't read.
- The check-then-insert in `TicketEventProcessor` is a deliberate fast path for ordinary retries. Correctness comes from the `EventId` primary key plus the catch for SQLite error 19 (constraint violation), not from the pre-check.

## Known limitations

- The schema is created with `EnsureCreated`, not migrations, so a schema change requires deleting `tickets.db`.
- A duplicate returns the current ticket state, not a stored copy of the original response.
- A reused `eventId` with a different payload is not detected.
- SQLite with a single database file supports one API instance; it is not suitable for several instances writing at once.
- SQLite allows one writer at a time. The database runs in WAL mode, and the connection string sets `Default Timeout=10`, so a locked database is retried for up to 10 seconds. If the lock is still held after that, `SQLITE_BUSY` is not caught and the request returns `500`.

## What I would improve with more time

- Use EF Core migrations instead of `EnsureCreated`.
- Store a hash of each event's payload and return `409` when a reused `eventId` arrives with a different payload.
- Store the original response per `eventId` so duplicates return exactly what the first call returned.
- Add explicit transition rules once the business defines them.
- Move to PostgreSQL or SQL Server for multi-instance deployments; the idempotency design (unique key + one transaction) carries over unchanged.

## Tests (`tests/Tickets.Api.Tests`)

Integration tests use `WebApplicationFactory` against a temporary SQLite file:
- the same `eventId` sent twice (with a different payload the second time) is applied once
- a repeated `eventId` with a different `ticketId` returns the original ticket
- 10 concurrent copies of one event are applied exactly once
- the DB rejects a duplicate `EventId` row directly
- validation cases (missing fields, invalid `type`/`status`, create without customerId) return `400` and store nothing
- out-of-order events, 404s, and the GET response shape
