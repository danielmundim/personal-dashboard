using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Noona;

/// <summary>Thrown when Noona needs (re)connecting: no tokens yet, or the refresh token was rejected.</summary>
public sealed class NoonaNotConnectedException(string message) : Exception(message);

/// <summary>
/// OAuth 2.0 authorization-code flow against Noona HQ (https://api.noona.is/v1/hq/oauth/*), plus
/// token refresh. The browser only ever sees the consent screen; tokens stay on the server.
/// </summary>
public sealed class NoonaAuth(HttpClient http, NoonaTokenStore store, IOptions<NoonaOptions> options, TimeProvider time, ILogger<NoonaAuth> logger)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> PendingStates = new();
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    /// <summary>Consent URL with a fresh anti-CSRF state value.</summary>
    public string BuildAuthorizeUrl()
    {
        var o = options.Value;
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var now = time.GetUtcNow();
        foreach (var (key, created) in PendingStates)
            if (now - created > StateLifetime) PendingStates.TryRemove(key, out _);
        PendingStates[state] = now;

        return "https://api.noona.is/v1/hq/oauth/auth" +
               $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
               "&response_type=code" +
               $"&scope={Uri.EscapeDataString(o.Scopes)}" +
               $"&state={state}";
    }

    public bool ConsumeState(string? state) =>
        state is not null && PendingStates.TryRemove(state, out var created) && time.GetUtcNow() - created <= StateLifetime;

    /// <summary>Exchanges the one-time code for tokens, finds the company, and stores everything.</summary>
    public async Task<NoonaTokens> CompleteAsync(string code, CancellationToken ct)
    {
        var o = options.Value;
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["redirect_uri"] = o.RedirectUri,
        }, ct);

        var companyId = o.CompanyId;
        if (string.IsNullOrWhiteSpace(companyId))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "v1/hq/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await http.SendAsync(request, ct);
            await EnsureOkAsync(response, "reading the Noona user", ct);
            using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            companyId = user.RootElement.TryGetProperty("active_company", out var active) && active.ValueKind == JsonValueKind.String
                ? active.GetString()
                : user.RootElement.TryGetProperty("companies", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
                    ? list[0].GetString()
                    : null;
            if (string.IsNullOrWhiteSpace(companyId))
                throw new InvalidOperationException("This Noona user has no company. Set Dashboard:Noona:CompanyId.");
        }

        var tokens = new NoonaTokens(token.AccessToken, token.RefreshToken ?? "", token.ExpiresAt, companyId!);
        await store.SaveAsync(tokens, ct);
        logger.LogInformation("Noona connected for company {CompanyId}", companyId);
        return tokens;
    }

    /// <summary>A valid access token, refreshing it first when it's about to expire.</summary>
    public async Task<NoonaTokens> GetValidTokensAsync(CancellationToken ct, bool forceRefresh = false)
    {
        var tokens = await store.GetAsync(ct) ?? throw new NoonaNotConnectedException("Noona is not connected yet.");
        if (!forceRefresh && tokens.ExpiresAt > time.GetUtcNow().AddMinutes(2)) return tokens;

        await RefreshGate.WaitAsync(ct);
        try
        {
            // Another request may have refreshed while we waited.
            var latest = await store.GetAsync(ct) ?? throw new NoonaNotConnectedException("Noona is not connected yet.");
            if (!forceRefresh && latest.ExpiresAt > time.GetUtcNow().AddMinutes(2)) return latest;
            if (string.IsNullOrEmpty(latest.RefreshToken))
                throw new NoonaNotConnectedException("Noona session expired and no refresh token was issued.");

            var o = options.Value;
            TokenResponse token;
            try
            {
                token = await RequestTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = latest.RefreshToken,
                    ["client_id"] = o.ClientId,
                    ["client_secret"] = o.ClientSecret,
                }, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await store.ClearAsync(ct);
                throw new NoonaNotConnectedException("Noona refused the refresh token; reconnect needed.");
            }

            var refreshed = latest with
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken ?? latest.RefreshToken, // keep the old one if Noona doesn't rotate it
                ExpiresAt = token.ExpiresAt,
            };
            await store.SaveAsync(refreshed, ct);
            return refreshed;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public Task DisconnectAsync(CancellationToken ct) => store.ClearAsync(ct);

    private async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("v1/hq/oauth/token", body, ct);
        await EnsureOkAsync(response, $"the Noona token request ({body["grant_type"]})", ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var access = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Noona returned no access token.");
        var refresh = root.TryGetProperty("refresh_token", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        var expires = root.TryGetProperty("expires_at", out var e) && e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), out var at)
            ? at
            : root.TryGetProperty("expires_in", out var s) && s.TryGetInt32(out var seconds)
                ? time.GetUtcNow().AddSeconds(seconds)
                : time.GetUtcNow().AddMinutes(30);
        return new TokenResponse(access, refresh, expires);
    }

    /// <summary>Logs Noona's explanation (never the tokens) and throws with the status code attached.</summary>
    internal async Task EnsureOkAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Noona returned {Status} for {What}: {Body}", (int)response.StatusCode, what, body.Length > 500 ? body[..500] : body);
        throw new HttpRequestException($"Noona returned {(int)response.StatusCode} for {what}.", null, response.StatusCode);
    }

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
}
