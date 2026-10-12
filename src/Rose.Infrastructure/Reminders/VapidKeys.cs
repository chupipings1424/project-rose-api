using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Rose.Application.Abstractions;

namespace Rose.Infrastructure.Reminders;

/// <summary>
/// VAPID keys identify this server to Apple's / Google's push services. They are created once
/// and saved next to the data (git-ignored). Losing them only means every phone has to tap
/// "Enable notifications" again.
/// </summary>
public sealed record VapidKeys(string PublicKey, string PrivateKey)
{
    public static VapidKeys LoadOrCreate(IDataFile<VapidKeys?> file)
    {
        if (file.Read() is { PublicKey.Length: > 0, PrivateKey.Length: > 0 } existing) return existing;
        var keys = Generate();
        file.Write(keys);
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