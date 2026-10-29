using Npgsql;

namespace UnidadPastoral.Api.Data;

public static class PostgresConnectionString
{
    public static string Normalize(string connectionString)
    {
        var value = connectionString.Trim();
        if (!value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The PostgreSQL connection URL is invalid.");
        }

        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2)
        {
            throw new InvalidOperationException("The PostgreSQL connection URL must include a username and password.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]).ToLowerInvariant() switch
            {
                "sslmode" => "SSL Mode",
                "channel_binding" => "Channel Binding",
                "application_name" => "Application Name",
                _ => throw new InvalidOperationException("The PostgreSQL connection URL contains an unsupported query parameter.")
            };
            var setting = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
            builder[key] = setting;
        }

        return builder.ConnectionString;
    }
}