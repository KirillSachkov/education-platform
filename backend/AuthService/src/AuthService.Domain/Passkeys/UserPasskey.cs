namespace AuthService.Domain.Passkeys;

/// <summary>
/// WebAuthn credential bound to a platform user. One row per registered passkey
/// (a single user can have many — phone Touch ID, security key, etc.).
///
/// Schema: <c>auth.user_passkeys</c>. Aggregate root — saved via
/// <c>_db.UserPasskeys.AddAsync</c>, so PK is set via <c>Guid.CreateVersion7()</c>
/// in the factory (see backend-transactions rule).
///
/// Issue #305 (WebAuthn / Passkeys). Field shape mirrors what Fido2.AspNetCore
/// expects in attestation/assertion handlers; the host-side WebAuthn protocol is
/// implemented in <c>Features/Passkeys</c> use cases.
/// </summary>
public sealed class UserPasskey
{
    private UserPasskey() { }

    private UserPasskey(
        Guid id,
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        uint signCount,
        Guid aaGuid,
        string transports,
        string deviceName,
        DateTime createdAt)
    {
        Id = id;
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignCount = signCount;
        AaGuid = aaGuid;
        Transports = transports;
        DeviceName = deviceName;
        CreatedAt = createdAt;
        LastUsedAt = null;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

#pragma warning disable CA1819 // WebAuthn credential + COSE public key are raw byte[] by spec (Fido2 wire format)
    /// <summary>WebAuthn credential id — opaque bytes, unique per passkey across all users.</summary>
    public byte[] CredentialId { get; private set; } = [];

    /// <summary>Public key in COSE format (CBOR-encoded). Used to verify assertion signatures.</summary>
    public byte[] PublicKey { get; private set; } = [];
#pragma warning restore CA1819

    /// <summary>Authenticator counter — bumped on every successful sign-in for clone detection.</summary>
    public uint SignCount { get; private set; }

    /// <summary>Authenticator AAGUID — identifies the authenticator model (Touch ID, YubiKey, etc.).</summary>
    public Guid AaGuid { get; private set; }

    /// <summary>Comma-separated transport list: usb, ble, nfc, internal, hybrid.</summary>
    public string Transports { get; private set; } = "";

    /// <summary>User-friendly label, e.g. "iPhone 15", "YubiKey 5C" — shown in settings UI.</summary>
    public string DeviceName { get; private set; } = "";

    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    public static Result<UserPasskey, Error> Create(
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        uint signCount,
        Guid aaGuid,
        IReadOnlyCollection<string> transports,
        string deviceName)
    {
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        if (credentialId.Length == 0)
            return GeneralErrors.ValueIsInvalid(nameof(credentialId));
        if (publicKey.Length == 0)
            return GeneralErrors.ValueIsInvalid(nameof(publicKey));
        if (string.IsNullOrWhiteSpace(deviceName))
            return GeneralErrors.ValueIsInvalid(nameof(deviceName));

        UserPasskey passkey = new(
            Guid.CreateVersion7(),
            userId,
            credentialId,
            publicKey,
            signCount,
            aaGuid,
            string.Join(",", transports),
            deviceName.Trim(),
            DateTime.UtcNow);

        return passkey;
    }

    public void RecordUsage(uint newSignCount)
    {
        SignCount = newSignCount;
        LastUsedAt = DateTime.UtcNow;
    }

    public void Rename(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
            DeviceName = newName.Trim();
    }
}
