using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using amFTPd.Config.Fxp;
using amFTPd.Core.Fxp;
using amFTPd.Security;

namespace amFTPd.Tests;

public sealed class FxpPolicyEngineTests
{
    [Fact]
    public void DenySections_OverridesAllowSections()
    {
        var policy = new FxpPolicyConfig
        {
            AllowSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MP3" },
            DenySections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SAFE" },
        };

        var engine = CreateEngine(policy);
        var req = BuildRequest(sectionName: "SAFE");

        var decision = engine.Evaluate(req);

        Assert.False(decision.Allowed);
        Assert.Contains("denied in section", decision.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserRequireFlag_BlocksUsersWithoutFlag_WhenConfigured()
    {
        var policy = new FxpPolicyConfig { RequireUserAllowFlag = true };
        var engine = CreateEngine(policy);
        var req = BuildRequest(isAdmin: false, userAllowFxp: false);

        var decision = engine.Evaluate(req);

        Assert.False(decision.Allowed);
        Assert.Contains("not permitted to use FXP", decision.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminPolicyCanDenyAdmins()
    {
        var policy = new FxpPolicyConfig { AllowAdmins = false };
        var engine = CreateEngine(policy);
        var req = BuildRequest(isAdmin: true);

        var decision = engine.Evaluate(req);

        Assert.False(decision.Allowed);
        Assert.Contains("admins by policy", decision.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameHostIsDenied_WhenPolicyIsEnabled()
    {
        var policy = new FxpPolicyConfig { DenySameHost = true };
        var engine = CreateEngine(policy);
        var sameHost = IPAddress.Loopback;
        var req = BuildRequest(remoteIp: sameHost, controlPeerIp: sameHost);

        var decision = engine.Evaluate(req);

        Assert.False(decision.Allowed);
        Assert.Contains("same host", decision.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrustedHostsCanOverrideEmptyAllowedPeerList()
    {
        var trustedIp = IPAddress.Parse("198.51.100.9");
        var policy = new FxpPolicyConfig
        {
            TrustedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "198.51.100.9"
            }
        };
        var denied = new FxpPolicyConfig
        {
            TrustedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            DenyHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "198.51.100.9"
            }
        };

        var engine = CreateEngine(policy);
        var deniedEngine = CreateEngine(denied);
        var allowed = engine.Evaluate(BuildRequest(remoteIp: trustedIp));
        var deniedReq = deniedEngine.Evaluate(BuildRequest(remoteIp: trustedIp));

        Assert.True(allowed.Allowed);
        Assert.False(deniedReq.Allowed);
        Assert.Contains("not allowed by policy", deniedReq.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlainFxpIsDenied_WhenPlainTransfersAreDisabled()
    {
        var policy = new FxpPolicyConfig
        {
            AllowPlainFxp = false,
            RequireUserAllowFlag = false
        };
        var engine = CreateEngine(policy, allowPlainFxp: false);
        var req = BuildRequest(
            controlTlsActive: false,
            dataChannelProtected: false,
            dataTlsActive: false);

        var decision = engine.Evaluate(req);

        Assert.False(decision.Allowed);
        Assert.Contains("Plain FXP is disabled", decision.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtectedDataFxpIsAllowed_WhenSecurePolicyAllowsIt()
    {
        var policy = new FxpPolicyConfig
        {
            AllowSecureFxp = true,
            RequireUserAllowFlag = false
        };
        var engine = CreateEngine(policy, allowPlainFxp: false);
        var req = BuildRequest(
            controlTlsActive: true,
            dataChannelProtected: true,
            dataTlsActive: true);

        var decision = engine.Evaluate(req);

        Assert.True(decision.Allowed, decision.DenyReason);
    }

    private static FxpPolicyEngine CreateEngine(FxpPolicyConfig policy, bool allowPlainFxp = true)
    {
        var cfg = new FxpConfig
        {
            AllowPlainFxp = allowPlainFxp
        };

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=amFTPd-fxp-policy-test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return new FxpPolicyEngine(cfg, policy, new TlsConfig(cert));
    }

    private static FxpRequest BuildRequest(
        string sectionName = "0DAY",
        bool isAdmin = false,
        bool userAllowFxp = true,
        IPAddress? remoteIp = null,
        IPAddress? controlPeerIp = null,
        string remoteHost = "peer.example",
        bool controlTlsActive = true,
        bool dataChannelProtected = true,
        bool dataTlsActive = true)
    {
        return new FxpRequest
        {
            UserName = isAdmin ? "admin" : "normaluser",
            GroupName = isAdmin ? "admins" : "users",
            IsAdmin = isAdmin,
            UserAllowFxp = userAllowFxp,
            SectionName = sectionName,
            VirtualPath = "/0DAY/TEST.ZLS",
            Direction = FxpDirection.Incoming,
            RemoteHost = remoteHost,
            RemoteIp = remoteIp ?? IPAddress.Parse("198.51.100.9"),
            ControlPeerIp = controlPeerIp ?? IPAddress.Loopback,
            ControlTlsActive = controlTlsActive,
            ControlProtocol = SslProtocols.Tls12,
            DataChannelProtected = dataChannelProtected,
            DataTlsActive = dataTlsActive,
            DataProtocol = SslProtocols.Tls12
        };
    }
}
