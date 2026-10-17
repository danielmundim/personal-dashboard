using System.Text.Json;
using Dashboard.Api.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace Dashboard.Api.Features.Noona;

public sealed record NoonaTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string CompanyId);

/// <summary>
/// Keeps the OAuth tokens in DataPath/noona-tokens.bin, encrypted with ASP.NET Data Protection
/// (keys live in the user profile, outside the repo), so a copied App_Data folder doesn't leak a
/// working refresh token.
/// </summary>
public sealed class NoonaTokenStore(DataDirectory dataDirectory, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Dashboard.Noona.Tokens.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NoonaTokens? _cached;
    private bool _loaded;

    private string FilePath => Path.Combine(dataDirectory.Root, "noona-tokens.bin");

    public async Task<NoonaTokens?> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_loaded) return _cached;
            _loaded = true;
            if (!File.Exists(FilePath)) return null;
            try
            {
                var json = _protector.Unprotect(await File.ReadAllBytesAsync(FilePath, ct));
                return _cached = JsonSerializer.Deserialize<NoonaTokens>(json);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Keys changed (new machine/user): the tokens are unreadable, so the user reconnects.
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(NoonaTokens tokens, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(tokens));
            var tmp = FilePath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, FilePath, overwrite: true);
            _cached = tokens;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            _cached = null;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
