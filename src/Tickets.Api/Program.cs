using Microsoft.EntityFrameworkCore;
using Tickets.Api.Data;
using Tickets.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<TicketsDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Tickets")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TicketEventProcessor>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Schema is small and fixed for this exercise, so EnsureCreated is used instead of migrations.
// WAL lets readers run alongside a writer; the setting is stored in the database file.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>().Database;
    db.EnsureCreated();
    db.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
