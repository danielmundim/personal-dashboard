using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Noona;

/// <summary>
/// /api/noona/connect starts the one-time consent; Noona redirects back to /api/noona/callback,
/// which stores the tokens and returns the browser to the dashboard.
/// </summary>
public static class NoonaEndpoints
{
    public static void MapNoona(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/noona");

        g.MapGet("/status", async (IServiceProvider sp, IOptions<NoonaOptions> o, CancellationToken ct) =>
        {
            var store = sp.GetService<NoonaTokenStore>();
            var tokens = store is null ? null : await store.GetAsync(ct);
            return new { configured = o.Value.IsConfigured, connected = tokens is not null, companyId = tokens?.CompanyId };
        });

        g.MapGet("/connect", (IServiceProvider sp) =>
            sp.GetService<NoonaAuth>() is { } auth
                ? Results.Redirect(auth.BuildAuthorizeUrl())
                : Results.Problem("Set Dashboard:Noona:ClientId and ClientSecret in appsettings.Local.json first.", statusCode: 503));

        g.MapGet("/callback", async (string? code, string? state, string? error, IServiceProvider sp, IOptions<NoonaOptions> o, ILoggerFactory logs, CancellationToken ct) =>
        {
            var back = o.Value.ReturnUrl.TrimEnd('/') + "/?noona=";
            if (sp.GetService<NoonaAuth>() is not { } auth) return Results.Redirect(back + "not-configured");
            if (!string.IsNullOrEmpty(error)) return Results.Redirect(back + "denied");
            if (string.IsNullOrEmpty(code) || !auth.ConsumeState(state)) return Results.Redirect(back + "invalid");

            try
            {
                await auth.CompleteAsync(code, ct);
                return Results.Redirect(back + "connected");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                logs.CreateLogger("Noona").LogWarning(ex, "Noona connect failed");
                return Results.Redirect(back + "failed");
            }
        });

        g.MapPost("/disconnect", async (IServiceProvider sp, CancellationToken ct) =>
        {
            if (sp.GetService<NoonaAuth>() is { } auth) await auth.DisconnectAsync(ct);
            return Results.NoContent();
        });
    }
}
