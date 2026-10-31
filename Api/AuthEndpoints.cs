using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class AuthEndpoints
{
    public static void MapPeerCareAuth(this WebApplication app)
    {
        app.MapGet("/api/meta/consent", (IConfiguration config) => Results.Ok(new
        {
            version = config["Consent:Version"] ?? "2026-10-v1",
            text = config["Consent:Text"] ?? "PeerCare stores the health experiences you choose to share so they can help other families. You can post anonymously. Your health content is not sold, used for advertising, or shared to train outside models. These stories are personal experience, not medical advice. You can download or delete your data in settings.",
            disclaimer = "This is one family's experience, not medical advice. Talk with a qualified health professional about your care."
        }));

        app.MapPost("/api/auth/code/start", async (OtpStartRequest request, PeerCareDbContext db, ContactCrypto crypto,
            ContactDelivery delivery, IConfiguration config, HttpContext http, CancellationToken ct) =>
        {
            var parsed = ParseContact(request.Contact, request.ContactType);
            if (parsed is null) return Results.BadRequest(new { message = "Enter a valid email address or phone number." });
            var (type, contact) = parsed.Value;
            var index = crypto.BlindIndex(type + ":" + contact);
            var activeChallenges = await db.OtpChallenges.Where(x => x.ContactType == type && x.ContactBlindIndex == index).ToListAsync(ct);
            var active = activeChallenges.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            if (active?.LockedUntil is { } lockUntil && lockUntil > DateTimeOffset.UtcNow)
                return Results.Json(new { message = "Too many incorrect codes. Please wait 10 minutes before trying again." }, statusCode: 429);
            if (active is not null) db.OtpChallenges.RemoveRange(db.OtpChallenges.Where(x => x.ContactType == type && x.ContactBlindIndex == index));
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            try { await delivery.SendCodeAsync(type, contact, code, ct); }
            catch (Exception ex) when (ex is HttpRequestException or SmtpException or InvalidOperationException)
            {
                return Results.Problem("We could not send a verification code. Check the delivery settings and try again.", statusCode: 503);
            }
            db.OtpChallenges.Add(new OtpChallenge
            {
                ContactType = type,
                ContactBlindIndex = index,
                Contact = contact,
                CodeHash = crypto.HashOtp(code),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "If this address or number can receive a code, one is on its way.", expiresInSeconds = 600 });
        }).RequireRateLimiting("otp");

        app.MapPost("/api/auth/code/verify", async (OtpVerifyRequest request, PeerCareDbContext db, ContactCrypto crypto,
            UserManager<PeerUser> users, SignInManager<PeerUser> signIn, IConfiguration config, ILogger<PeerUser> logger, CancellationToken ct) =>
        {
            var parsed = ParseContact(request.Contact, request.ContactType);
            if (parsed is null || request.Code.Length != 6 || request.Code.Any(c => !char.IsAsciiDigit(c)))
                return Results.BadRequest(new { message = "Enter the six-digit code we sent you." });
            var (type, contact) = parsed.Value;
            var index = crypto.BlindIndex(type + ":" + contact);
            var challenges = await db.OtpChallenges.Where(x => x.ContactType == type && x.ContactBlindIndex == index).ToListAsync(ct);
            var challenge = challenges.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            if (challenge is null || challenge.ExpiresAt < DateTimeOffset.UtcNow)
                return Results.BadRequest(new { message = "That code has expired. Request a new one." });
            if (challenge.LockedUntil is { } locked && locked > DateTimeOffset.UtcNow)
                return Results.Json(new { message = "Too many incorrect codes. Please wait 10 minutes." }, statusCode: 429);
            if (!crypto.VerifyOtp(request.Code, challenge.CodeHash))
            {
                challenge.FailedAttempts++;
                if (challenge.FailedAttempts >= 3) challenge.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
                await db.SaveChangesAsync(ct);
                return challenge.FailedAttempts >= 3
                    ? Results.Json(new { message = "Too many incorrect codes. Please wait 10 minutes." }, statusCode: 429)
                    : Results.BadRequest(new { message = $"That code did not match. {3 - challenge.FailedAttempts} attempts left." });
            }

            db.OtpChallenges.Remove(challenge);
            PeerUser? user = type == "email"
                ? await users.FindByEmailAsync(contact)
                : await db.Users.SingleOrDefaultAsync(x => x.PhoneBlindIndex == index, ct);
            if (user is null)
            {
                user = new PeerUser
                {
                    Id = Guid.NewGuid(),
                    UserName = "pc-" + Guid.NewGuid().ToString("N"),
                    DisplayName = "",
                    Email = type == "email" ? contact : null,
                    EmailConfirmed = type == "email",
                    PhoneNumber = type == "phone" ? contact : null,
                    PhoneNumberConfirmed = type == "phone",
                    PhoneBlindIndex = type == "phone" ? index : null,
                    Status = "active",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                var create = await users.CreateAsync(user);
                if (!create.Succeeded)
                {
                    logger.LogError("PeerCare account creation failed Identity validation. Error codes: {ErrorCodes}",
                        string.Join(", ", create.Errors.Select(error => error.Code)));
                    return Results.Problem("We could not create the account. Please try again.", statusCode: 500);
                }
                await users.AddToRoleAsync(user, "Member");
            }
            else if (user.Status != "active" || user.DeleteRequestedAt is not null)
            {
                await db.SaveChangesAsync(ct);
                return Results.Forbid();
            }
            var bootstrap = config["Bootstrap:AdminEmail"];
            if (type == "email" && !string.IsNullOrWhiteSpace(bootstrap) && string.Equals(bootstrap.Trim(), contact, StringComparison.OrdinalIgnoreCase)
                && !await users.IsInRoleAsync(user, "Admin"))
                await users.AddToRoleAsync(user, "Admin");
            await db.SaveChangesAsync(ct);
            await signIn.SignInAsync(user, isPersistent: true);
            var roles = await users.GetRolesAsync(user);
            return Results.Ok(new { user = ApiSupport.UserCard(user, roles), consentRequired = !ApiSupport.HasConsent(user, config) });
        }).RequireRateLimiting("otp");

        app.MapGet("/api/auth/me", async (HttpContext http, PeerCareDbContext db, UserManager<PeerUser> users, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            var roles = await users.GetRolesAsync(user);
            return Results.Ok(new { user = ApiSupport.UserCard(user, roles), consentRequired = !ApiSupport.HasConsent(user, config) });
        });
        app.MapPost("/api/auth/logout", async (SignInManager<PeerUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok(new { signedOut = true });
        });
    }

    private static (string Type, string Contact)? ParseContact(string input, string? requestedType)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var contact = input.Trim();
        var isPhone = string.Equals(requestedType, "phone", StringComparison.OrdinalIgnoreCase) ||
                      (string.IsNullOrWhiteSpace(requestedType) && !contact.Contains('@'));
        if (!isPhone)
        {
            try
            {
                var address = new MailAddress(contact);
                if (!string.Equals(address.Address, contact, StringComparison.OrdinalIgnoreCase)) return null;
                return ("email", contact.ToLowerInvariant());
            }
            catch (FormatException) { return null; }
        }
        if (contact.Any(c => !char.IsDigit(c) && c is not '+' and not ' ' and not '-' and not '(' and not ')')) return null;
        var digits = new string(contact.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("0") && digits.Length == 11) digits = "234" + digits[1..];
        else if (digits.Length == 10) digits = "234" + digits;
        if (digits.Length is < 10 or > 15) return null;
        return ("phone", "+" + digits);
    }
}
