/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace amFTPd.Core.Ident;

/// <summary>
/// Sends an Ident protocol query to a remote endpoint and retrieves the user information associated with a
/// connection.
/// </summary>
/// <remarks>This method establishes a TCP connection to the Ident service on the remote endpoint, sends a
/// query for the user information associated with the specified remote and local ports, and parses the response.
/// The Ident protocol is typically used to identify the user of a specific TCP connection. <para> The method
/// supports cancellation via the provided <see cref="CancellationToken"/> and enforces a timeout for the operation.
/// If the query fails (e.g., due to a network error, timeout, or invalid response), the method returns <see
/// cref="IdentResult.Failed"/>. </para></remarks>
internal sealed class IdentClient
{
    private const int IdentPort = 113;

    public async Task<IdentResult> QueryAsync(
        IPEndPoint remote,
        IPEndPoint local,
        int timeoutMs,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(remote.Address, IdentPort, cts.Token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            var query = $"{remote.Port} , {local.Port}\r\n";
            var buf = Encoding.ASCII.GetBytes(query);
            await stream.WriteAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            var respBuf = new byte[512];
            var len = await stream.ReadAsync(respBuf, 0, respBuf.Length, cts.Token).ConfigureAwait(false);
            if (len <= 0)
                return IdentResult.Failed;

            var raw = Encoding.ASCII.GetString(respBuf, 0, len).Trim();

            // Minimal parse: "port , port : USERID : OS : username"
            // We'll try to extract username.
            var parts = raw.Split(':');
            if (parts.Length < 4)
                return new IdentResult(false, null, null, raw, DateTimeOffset.UtcNow);

            var os = parts[2].Trim();
            var user = parts[3].Trim();

            return new IdentResult(true, user, os, raw, DateTimeOffset.UtcNow);
        }
        catch
        {
            return IdentResult.Failed;
        }
    }
}