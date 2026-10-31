using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;

namespace PeerCare.Services;

public sealed class ContactDelivery(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ContactDelivery> logger)
{
    public async Task SendCodeAsync(string contactType, string contact, string code, CancellationToken cancellationToken)
    {
        var message = $"Your HealthStories verification code is {code}. It expires in 10 minutes. If you did not request this, ignore this message.";
        if (contactType == "phone")
        {
            var apiKey = configuration["Termii:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey)) { DevelopmentLog(contact, code); return; }
            var endpoint = configuration["Termii:Endpoint"] ?? "https://api.ng.termii.com/api/sms/send";
            var payload = new TermiiRequest(contact, configuration["Termii:SenderId"] ?? "PeerCare", message, "plain", "generic", apiKey);
            var response = await clients.CreateClient("termii").PostAsJsonAsync(endpoint, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }
        if (environment.IsDevelopment()
            && string.IsNullOrWhiteSpace(configuration["APP_API_KEY"])
            && string.IsNullOrWhiteSpace(configuration["APP_SECRET_KEY"])
            && (string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"]) || string.IsNullOrWhiteSpace(configuration["Email:From"])))
        {
            DevelopmentLog(contact, code);
            return;
        }
        await SendEmailAsync(contact, "Your HealthStories sign-in code", message, cancellationToken);
    }

    public async Task SendNotificationAsync(string email, string subject, string body, CancellationToken cancellationToken)
        => await SendEmailAsync(email, subject, body, cancellationToken);

    private async Task SendEmailAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var mailjetKey = configuration["APP_API_KEY"];
        var mailjetSecret = configuration["APP_SECRET_KEY"];
        if (!string.IsNullOrWhiteSpace(mailjetKey) || !string.IsNullOrWhiteSpace(mailjetSecret))
        {
            if (string.IsNullOrWhiteSpace(mailjetKey) || string.IsNullOrWhiteSpace(mailjetSecret))
                throw new InvalidOperationException("Both Mailjet API credentials must be configured.");
            await SendWithMailjetAsync(recipient, subject, body, mailjetKey, mailjetSecret, cancellationToken);
            return;
        }

        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) { DevelopmentLog(recipient, "Email delivery is not configured."); return; }
        using var mail = new MailMessage(from, recipient, subject, body);
        using var client = new SmtpClient(host, int.TryParse(configuration["Email:Port"], out var port) ? port : 587)
        {
            EnableSsl = !string.Equals(configuration["Email:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        await client.SendMailAsync(mail, cancellationToken);
    }

    private async Task SendWithMailjetAsync(string recipient, string subject, string body, string apiKey, string secretKey, CancellationToken cancellationToken)
    {
        var from = configuration["Email:From"];
        if (string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Email:From must be set to a sender address validated in Mailjet.");

        var payload = new
        {
            Messages = new[]
            {
                new
                {
                    From = new { Email = from, Name = configuration["Email:FromName"] ?? "HealthStories" },
                    To = new[] { new { Email = recipient } },
                    Subject = subject,
                    TextPart = body,
                    HTMLPart = $"<div style=\"font-family:Arial,sans-serif;line-height:1.6\"><p>{System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>")}</p></div>"
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, configuration["Email:MailjetEndpoint"] ?? "https://api.mailjet.com/v3.1/send");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = JsonContent.Create(payload);
        var response = await clients.CreateClient("mailjet").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Mailjet email delivery failed with status {StatusCode}.", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
    }

    private void DevelopmentLog(string contact, string detail)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("An OTP delivery provider is not configured.");
        var suffix = contact.Length > 2 ? contact[^2..] : "**";
        logger.LogWarning("Development-only PeerCare delivery to contact ending {Suffix}: {Detail}", suffix, detail);
    }

    private sealed record TermiiRequest(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("sms")] string Sms,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("api_key")] string ApiKey);
}
