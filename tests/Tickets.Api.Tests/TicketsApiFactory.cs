using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tickets.Api.Data;

namespace Tickets.Api.Tests;

/// <summary>Runs the real app against a throwaway SQLite file per test class.</summary>
public class TicketsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tickets-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Pooling=False so the file is released and can be deleted on dispose.
        builder.UseSetting("ConnectionStrings:Tickets", $"Data Source={_dbPath};Pooling=False;Default Timeout=10");
    }

    public T WithDb<T>(Func<TicketsDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<TicketsDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}
