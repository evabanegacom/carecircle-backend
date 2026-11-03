using System.Net;
using System.Text.RegularExpressions;
using Npgsql;

namespace PeerCare.Data;

/// <summary>Normalizes PostgreSQL URIs and standard Npgsql connection strings.</summary>
public static class PeerCareConnectionString
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("ConnectionStrings:PeerCare must be configured.");

        var connectionString = value.Trim();
        if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            return ParsePostgresUri(connectionString);

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("ConnectionStrings:PeerCare must be a valid PostgreSQL URI or Npgsql connection string.");
        }

        if (IsNeonHost(builder.Host) && !ContainsSslMode(connectionString))
            builder.SslMode = SslMode.Require;

        return builder.ConnectionString;
    }

    private static string ParsePostgresUri(string connectionString)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgresql" && uri.Scheme != "postgres")
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException("ConnectionStrings:PeerCare contains an invalid PostgreSQL URI.");

        try
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
            };

            var userInfo = uri.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped);
            if (userInfo.Length > 0)
            {
                var separator = userInfo.IndexOf(':');
                builder.Username = Uri.UnescapeDataString(separator < 0 ? userInfo : userInfo[..separator]);
                if (separator >= 0)
                    builder.Password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
            }

            var sslModeSpecified = false;
            foreach (var component in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = component.Split('=', 2);
                var key = WebUtility.UrlDecode(pair[0]);
                var parameterValue = WebUtility.UrlDecode(pair.Length == 2 ? pair[1] : string.Empty);
                var npgsqlKey = NormalizeUriParameterName(key);
                builder[npgsqlKey] = parameterValue;
                if (npgsqlKey == "SSL Mode")
                    sslModeSpecified = true;
            }

            if (IsNeonHost(builder.Host) && !sslModeSpecified)
                builder.SslMode = SslMode.Require;

            return builder.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Do not include the input URI in errors; it contains credentials.
            throw new InvalidOperationException("ConnectionStrings:PeerCare contains an invalid PostgreSQL URI or option.");
        }
    }

    private static string NormalizeUriParameterName(string name) => name.ToLowerInvariant() switch
    {
        "host" => "Host",
        "port" => "Port",
        "database" or "dbname" => "Database",
        "user" or "username" => "Username",
        "password" => "Password",
        "sslmode" or "ssl_mode" => "SSL Mode",
        "channel_binding" or "channelbinding" => "Channel Binding",
        "application_name" or "applicationname" => "Application Name",
        "connect_timeout" or "timeout" => "Timeout",
        "command_timeout" => "Command Timeout",
        "keepalives_idle" or "keepalive" => "Keepalive",
        "target_session_attrs" => "Target Session Attributes",
        "trust_server_certificate" => "Trust Server Certificate",
        "sslrootcert" => "Root Certificate",
        "sslcert" => "SSL Certificate",
        "sslkey" => "SSL Key",
        "sslpassword" => "SSL Password",
        "pooling" => "Pooling",
        "minpoolsize" or "min_pool_size" => "Minimum Pool Size",
        "maxpoolsize" or "max_pool_size" => "Maximum Pool Size",
        "load_balance_hosts" => "Load Balance Hosts",
        "options" => "Options",
        "search_path" => "Search Path",
        "enlist" => "Enlist",
        "include_error_detail" => "Include Error Detail",
        _ => throw new ArgumentException($"Unsupported PostgreSQL URI parameter '{name}'.")
    };

    private static bool IsNeonHost(string? host) =>
        host?.EndsWith(".neon.tech", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsSslMode(string connectionString) =>
        Regex.IsMatch(connectionString, @"(?:^|;)\s*ssl\s*mode\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
