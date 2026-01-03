using amFTPd.Logging;

namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Manages Diffie-Hellman 1080 (DH1080) key exchange sessions for secure communication with multiple targets.
/// </summary>
/// <remarks>This class provides methods to start, retrieve, and remove DH1080 key exchange sessions associated
/// with specific targets. It is designed for scenarios where multiple concurrent key exchanges may be managed, such as
/// in secure messaging applications. Instances of this class are thread-unsafe; external synchronization is required if
/// accessed from multiple threads concurrently.</remarks>
public sealed class Dh1080Manager
{
    private readonly Dictionary<string, Dh1080Session> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private IFtpLogger? _log;
    
    /// <summary>
    /// Initializes a new instance of the Dh1080Manager class with the specified logger.
    /// </summary>
    /// <param name="logger">An optional logger used to record FTP-related events. May be null if logging is not required.</param>
    public Dh1080Manager(IFtpLogger? logger) => _log = logger;
    /// <summary>
    /// Starts a new Diffie-Hellman 1080 key exchange session for the specified target.
    /// </summary>
    /// <param name="target">The identifier of the remote party with whom to initiate the session. Cannot be null or empty.</param>
    /// <returns>A <see cref="Dh1080Session"/> representing the newly started session for the specified target.</returns>
    public Dh1080Session Start(string target)
    {
        _log?.Log(FtpLogLevel.Debug, $"[Dh1080Manager] Starting session for {target}...");
        var s = new Dh1080Session(target, _log);
        _sessions[target] = s;
        return s;
    }
    /// <summary>
    /// Attempts to retrieve the DH1080 session associated with the specified target.
    /// </summary>
    /// <param name="target">The identifier of the target for which to retrieve the session. Cannot be null.</param>
    /// <param name="session">When this method returns, contains the DH1080 session associated with the specified target, if found; otherwise,
    /// null. This parameter is passed uninitialized.</param>
    /// <returns>true if a session associated with the specified target is found; otherwise, false.</returns>
    public bool TryGet(string target, out Dh1080Session session)
        => _sessions.TryGetValue(target, out session);
    /// <summary>
    /// Removes the session associated with the specified target identifier.
    /// </summary>
    /// <param name="target">The identifier of the session to remove. Cannot be null.</param>
    public void Remove(string target)
        => _sessions.Remove(target);
    /// <summary>
    /// Rebinds an existing session from the specified old nickname to a new nickname.
    /// </summary>
    /// <remarks>If there is no session associated with the specified old nickname, this method performs no
    /// action. If a session already exists for the new nickname, it will be overwritten.</remarks>
    /// <param name="oldNick">The current nickname associated with the session to be rebound. Cannot be null or empty.</param>
    /// <param name="newNick">The new nickname to associate with the session. Cannot be null or empty.</param>
    public void Rebind(string oldNick, string newNick)
    {
        if (_sessions.Remove(oldNick, out var session)) _sessions[newNick] = session;
    }
}