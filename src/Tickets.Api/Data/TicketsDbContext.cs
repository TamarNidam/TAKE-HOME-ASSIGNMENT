using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tickets.Api.Data;

public class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(e =>
        {
            e.HasKey(t => t.TicketId);
            e.Property(t => t.CustomerId).IsRequired();
            e.Property(t => t.Subject).IsRequired();
            e.Property(t => t.Status).IsRequired();
        });

        modelBuilder.Entity<ProcessedEvent>(e =>
        {
            // Primary key => UNIQUE constraint on EventId enforced by SQLite.
            e.HasKey(p => p.EventId);
            e.HasIndex(p => p.TicketId);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset type. Store it as text with all 7 fractional
        // digits and the offset, so values round-trip exactly (the binary converter
        // truncates to 0.1 ms). Timestamps are only compared in memory, never in SQL.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToStringConverter>();
    }
}
