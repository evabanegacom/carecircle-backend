using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace PeerCare.Services;

public sealed class ContactCrypto(IDataProtectionProvider protectionProvider, IConfiguration configuration, IHostEnvironment environment)
{
    private readonly IDataProtector _email = protectionProvider.CreateProtector("PeerCare.Contact.Email.v1");
    private readonly IDataProtector _phone = protectionProvider.CreateProtector("PeerCare.Contact.Phone.v1");
    private readonly IDataProtector _licence = protectionProvider.CreateProtector("PeerCare.Professional.Licence.v1");
    private readonly byte[] _indexKey = ResolveKey(configuration, environment);

    public string ProtectEmail(string value) => _email.Protect(value);
    public string UnprotectEmail(string value) => _email.Unprotect(value);
    public string ProtectPhone(string value) => _phone.Protect(value);
    public string UnprotectPhone(string value) => _phone.Unprotect(value);
    public string ProtectLicence(string value) => _licence.Protect(value);
    public string UnprotectLicence(string value) => _licence.Unprotect(value);
    public string BlindIndex(string value) => Convert.ToHexString(HMACSHA256.HashData(_indexKey, Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())));
    public string HashOtp(string code) => Convert.ToHexString(HMACSHA256.HashData(_indexKey, Encoding.UTF8.GetBytes("otp:" + code)));
    public bool VerifyOtp(string code, string expected) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(HashOtp(code)), Encoding.UTF8.GetBytes(expected));

    private static byte[] ResolveKey(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Security:BlindIndexKey"];
        if (string.IsNullOrWhiteSpace(configured) && !env.IsDevelopment())
            throw new InvalidOperationException("Security:BlindIndexKey must be configured outside development.");
        configured ??= "PEERCARE-LOCAL-ONLY-CHANGE-BEFORE-DEPLOYMENT-2026";
        return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }
}

public sealed class HmacLookupNormalizer(ContactCrypto crypto) : ILookupNormalizer
{
    public string? NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? null : crypto.BlindIndex(name);
    public string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : crypto.BlindIndex(email);
}
