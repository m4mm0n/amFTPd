namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Specifies the encryption mode used by the FiSH protocol for secure message transmission.
/// </summary>
/// <remarks>Use this enumeration to indicate which key exchange or encryption method is active in a FiSH session.
/// The mode determines how session keys are established and how messages are encrypted. 'Ecb' represents the legacy
/// static key mode, while 'Cbc' and 'Pending' relate to Diffie-Hellman (DH1080) negotiated sessions.</remarks>
public enum FishKeyMode
{
    Ecb,        // legacy static FiSH
    Cbc,        // DH1080 negotiated
    Pending     // DH1080 handshake started
}