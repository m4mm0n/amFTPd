/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpHandler.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-13 16:04:03
 *  Last Modified:  2025-12-13 22:44:51
 *  CRC32:          0x0756349E
 *  
 *  Description:
 *      Convenience wrapper around FxpPolicyEngine that also logs decisions.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Config.Fxp;
using amFTPd.Logging;
using amFTPd.Security;

namespace amFTPd.Core.Fxp
{
    /// <summary>
    /// Convenience wrapper around FxpPolicyEngine that also logs decisions.
    /// </summary>
    public sealed class FxpHandler
    {
        private readonly FxpPolicyEngine _engine;
        private readonly IFtpLogger _log;

        public FxpHandler(FxpConfig cfg, FxpPolicyConfig policy, TlsConfig tls, IFtpLogger log)
        {
            _engine = new FxpPolicyEngine(cfg, policy, tls);
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public FxpDecision Authorize(FxpRequest req, string phase)
        {
            var dec = _engine.Evaluate(req);
            LogDecision(req, dec, phase);
            return dec;
        }

        private void LogDecision(FxpRequest req, FxpDecision dec, string phase)
        {
            var verdict = dec.Allowed ? "ALLOW" : "DENY";
            var reason = dec.DenyReason ?? "OK";

            var ctlTls = req.ControlTlsActive
                ? (req.ControlProtocol?.ToString() ?? "tls")
                : "plain";

            string dataTls;
            if (req.DataTlsActive)
            {
                dataTls = req.DataProtocol?.ToString() ?? "tls";
            }
            else
            {
                dataTls = req.DataChannelProtected ? "prot-only" : "plain";
            }

            _log.Log(FtpLogLevel.Info,
                $"FXP {verdict} [{phase}] user={req.UserName} admin={req.IsAdmin} " +
                $"section={req.SectionName ?? "-"} vpath={req.VirtualPath} " +
                $"dir={req.Direction} remoteHost={req.RemoteHost} remoteIp={req.RemoteIp} " +
                $"ctlTls={ctlTls} dataTls={dataTls} reason={reason}");
        }
    }

}
