using System.Security.Cryptography;
using amFTPd.Logging;

namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Represents a Diffie-Hellman 1080 key exchange session with a specific target entity.
/// </summary>
/// <remarks>This class manages the state and cryptographic operations required to establish a shared secret using
/// the DH1080 protocol. It provides methods to initiate the key exchange, handle incoming messages, and derive a shared
/// key suitable for use with symmetric encryption algorithms. Instances of this class are not thread-safe.</remarks>
public sealed class Dh1080Session
{
    private IFtpLogger? _log;

    /// <summary>
    /// Gets the target identifier associated with this instance.
    /// </summary>
    public string Target { get; }
    /// <summary>
    /// Gets the underlying DH1080 cryptographic engine used for key exchange operations.
    /// </summary>
    /// <remarks>Use this property to access advanced cryptographic functionality or to perform custom
    /// Diffie-Hellman key exchanges. The returned engine is read-only and should not be replaced.</remarks>
    public Dh1080 Engine { get; }
    /// <summary>
    /// Gets the current state of the DH1080 key exchange process.
    /// </summary>
    public Dh1080State State { get; private set; }

    /// <summary>
    /// Initializes a new instance of the Dh1080Session class for secure key exchange with the specified target.
    /// </summary>
    /// <remarks>The session starts in the Idle state and uses the provided logger for diagnostic output if
    /// available.</remarks>
    /// <param name="target">The identifier of the remote party to establish the DH1080 session with. Cannot be null.</param>
    /// <param name="log">An optional logger used to record session events and debug information. If null, no logging will occur.</param>
    public Dh1080Session(string target, IFtpLogger? log)
    {
        _log = log;
        Target = target;
        Engine = new Dh1080(_log);
        State = Dh1080State.Idle;
        _log?.Log(FtpLogLevel.Debug, $"[Dh1080Session] Initialized...");
    }

    public string HandleInit(string base64)
    {
        Engine.Unpack(base64);
        State = Dh1080State.InitSent;
        return Engine.CreateFinish();
    }

    public void HandleFinish(string base64)
    {
        Engine.Unpack(base64);
        State = Dh1080State.Established;
    }

    public string DeriveFishKey()
    {
        // EXACT mIRC behavior
        var secretBytes = Engine.SecretBytes; // expose from DH1080
        var hash = SHA256.HashData(secretBytes);
        return Convert.ToBase64String(hash)[..43];
    }
}