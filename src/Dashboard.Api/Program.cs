using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Household-specific values (exact home coordinates, API keys) live in a git-ignored file. Environment
// variables and command-line args are re-added after it so they still win, as usual in ASP.NET Core
// (e.g. secrets from /etc/dashboard-api.env on the Pi).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

builder.Services.AddOpenApi();
builder.Services.AddHybridCache();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddDashboardProviders(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.MapHealthChecks("/health");
app.MapDashboardEndpoints();

app.Run();
