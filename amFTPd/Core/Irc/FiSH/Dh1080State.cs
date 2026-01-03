namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Represents the current state of a DH1080 key exchange session.
/// </summary>
/// <remarks>Use this enumeration to track the progress of a DH1080 key exchange, such as when initiating,
/// receiving, or completing the handshake. The values indicate whether the session is idle, has sent an initiation
/// message, has received a finish message, or has established a shared secret.</remarks>
public enum Dh1080State
{
    Idle,
    InitSent,
    FinishReceived,
    Established
}