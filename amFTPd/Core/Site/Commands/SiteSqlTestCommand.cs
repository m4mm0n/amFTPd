using amFTPd.Db.Abstractions;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteSqlTestCommand : SiteCommandBase
{
    public override string Name => "SQLTEST";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var parts = argument.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE SQLTEST <provider> <connection-string>\r\n", ct);
            return;
        }

        var providerName = parts[0];
        var connString = parts[1];

        if (!SqlProviderRegistry.TryGet(providerName, out var provider))
        {
            var avail = string.Join(", ", SqlProviderRegistry.Providers);
            await context.Session.WriteAsync(
                $"550 SQL provider '{providerName}' not available. Available: {avail}\r\n",
                ct);
            return;
        }

        try
        {
            await using var conn = provider.Create(connString);
            await conn.OpenAsync(ct);

            await context.Session.WriteAsync(
                $"200 SQL provider '{providerName}' connection OK.\r\n", ct);
        }
        catch (Exception ex)
        {
            await context.Session.WriteAsync(
                $"550 SQL connection failed: {ex.Message}\r\n", ct);
        }
    }
}