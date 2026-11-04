using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Rose.Infrastructure.Persistence;

namespace Rose.Infrastructure.Reminders;

/// <summary>
/// VAPID keys identify this server to Apple's / Google's push services. They come from
/// configuration (Push:VapidPublicKey / Push:VapidPrivateKey) or are created once and kept in
/// the database. Losing them only means every phone has to tap "Enable notifications" again.
/// </summary>
public sealed record VapidKeys(string PublicKey, string PrivateKey)
{
    private const string PublicKeyName = "vapid_public_key";
    private const string PrivateKeyName = "vapid_private_key";

    internal static VapidKeys Resolve(PushOptions options, IDbContextFactory<RoseDbContext> contexts)
    {
        if (options is { VapidPublicKey.Length: > 0, VapidPrivateKey.Length: > 0 })
            return new VapidKeys(options.VapidPublicKey, options.VapidPrivateKey);

        using var db = contexts.CreateDbContext();
        var saved = db.AppSecrets
            .Where(s => s.Key == PublicKeyName || s.Key == PrivateKeyName)
            .ToDictionary(s => s.Key, s => s.Value);
        if (saved.TryGetValue(PublicKeyName, out var publicKey) && saved.TryGetValue(PrivateKeyName, out var privateKey))
            return new VapidKeys(publicKey, privateKey);

        var keys = Generate();
        db.AppSecrets.RemoveRange(db.AppSecrets.Where(s => s.Key == PublicKeyName || s.Key == PrivateKeyName));
        db.AppSecrets.Add(new AppSecretRow { Key = PublicKeyName, Value = keys.PublicKey });
        db.AppSecrets.Add(new AppSecretRow { Key = PrivateKeyName, Value = keys.PrivateKey });
        db.SaveChanges();
        return keys;
    }

    /// <summary>A P-256 key pair, base64url-encoded like the web-push npm package does.</summary>
    public static VapidKeys Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        byte[] publicKey = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
        return new VapidKeys(WebEncoders.Base64UrlEncode(publicKey), WebEncoders.Base64UrlEncode(parameters.D!));
    }
}