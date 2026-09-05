using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Api;

/// <summary>
/// The wire body of <c>POST /v1/broker-accounts</c>. Metadata only: the
/// unmasked login is used to re-derive the binding fingerprint and to persist
/// <c>login_number</c>. The MT5 password is never accepted here; the desktop
/// writes it to the on-device DPAPI vault. <see cref="CreateBrokerAccount"/>
/// never carries a secret, so no persisted row, idempotency digest, or audit
/// record can either.
/// </summary>
public sealed record CreateBrokerAccountBody(
    Guid BrokerProfileId,
    string Server,
    string Login,
    string MaskedLogin,
    string BindingFingerprint,
    BrokerAccountEnvironment Environment);

/// <summary>
/// The validated form of a link request: everything the control plane may
/// persist, plus the login and binding key the vault write needs.
/// </summary>
public sealed record BrokerAccountLinkRequest(
    ulong Login,
    string Server,
    string MaskedLogin,
    string CredentialKey)
{
    public override string ToString() =>
        $"BrokerAccountLinkRequest {{ CredentialKey = {CredentialKey}, Login = {MaskedLogin} }}";
}

public static class BrokerAccountLinkValidation
{
    private const int MaximumServerCharacters = 255;

    /// <summary>Domain separator of <c>LocalCredentialKey</c>; changing it orphans every vault entry.</summary>
    private static readonly byte[] CredentialKeyDomain = "YO4X/local-mt5-credential/v1\0"u8.ToArray();

    /// <summary>
    /// Re-derives every value the browser claimed. The browser computed the
    /// binding fingerprint itself, and a fingerprint that does not follow from
    /// the login and server would store the credential under a key the
    /// connection probe never looks up, so a mismatch fails the link outright
    /// rather than producing an account nobody can connect.
    /// </summary>
    public static BrokerAccountLinkRequest Validate(CreateBrokerAccountBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.BrokerProfileId == Guid.Empty
            || !Enum.IsDefined(body.Environment)
            || body.Environment != BrokerAccountEnvironment.Demo)
        {
            throw Invalid();
        }

        string server = NormalizeServer(body.Server);
        ulong login = ParseLogin(body.Login);
        string maskedLogin = MaskLogin(login);
        string credentialKey = DeriveCredentialKey(login, server);
        if (!string.Equals(maskedLogin, body.MaskedLogin?.Trim(), StringComparison.Ordinal)
            || !FixedTimeHexEquals(credentialKey, body.BindingFingerprint))
        {
            throw Invalid();
        }

        return new BrokerAccountLinkRequest(login, server, maskedLogin, credentialKey);
    }

    public static CreateBrokerAccount ToApplicationRequest(
        CreateBrokerAccountBody body,
        BrokerAccountLinkRequest link)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(link);
        return new CreateBrokerAccount(
            body.BrokerProfileId,
            link.Server,
            link.MaskedLogin,
            link.CredentialKey,
            BrokerAccountEnvironment.Demo,
            link.Login);
    }

    /// <summary>
    /// Reproduces <c>LocalCredentialKey.Create</c>. That type lives in the
    /// Windows-only DPAPI boundary this API cannot reference, so the derivation
    /// is pinned here by a known-answer test against the boundary's own vector,
    /// and the writer process re-derives it from the credential it is about to
    /// store and refuses a mismatch.
    /// </summary>
    public static string DeriveCredentialKey(ulong login, string server)
    {
        ArgumentOutOfRangeException.ThrowIfZero(login);
        byte[] serverBytes = Encoding.UTF8.GetBytes(NormalizeServer(server).ToUpperInvariant());
        Span<byte> loginBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(loginBytes, login);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(CredentialKeyDomain);
        hash.AppendData(serverBytes);
        hash.AppendData(loginBytes);
        string key = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(serverBytes);
        return key;
    }

    private static string NormalizeServer(string? value)
    {
        string normalized = value?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
        if (normalized.Length is < 1 or > MaximumServerCharacters
            || normalized.Any(char.IsControl))
        {
            throw Invalid();
        }

        return normalized;
    }

    private static ulong ParseLogin(string? value)
    {
        string login = value?.Trim() ?? string.Empty;
        if (login.Length is < 1 or > 20
            || !login.All(character => character is >= '0' and <= '9')
            || !ulong.TryParse(login, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
            || parsed == 0)
        {
            throw Invalid();
        }

        return parsed;
    }

    private static string MaskLogin(ulong login)
    {
        string value = login.ToString(CultureInfo.InvariantCulture);
        int visible = value.Length <= 2 ? 0 : 2;
        return string.Concat(new string('*', value.Length - visible), value.AsSpan(value.Length - visible));
    }

    private static bool FixedTimeHexEquals(string expected, string? candidate)
    {
        string trimmed = candidate?.Trim().ToLowerInvariant() ?? string.Empty;
        if (trimmed.Length != expected.Length
            || !trimmed.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            return false;
        }

        byte[] expectedBytes = Convert.FromHexString(expected);
        byte[] candidateBytes = Convert.FromHexString(trimmed);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(candidateBytes);
        }
    }

    private static DomainException Invalid() => new(
        "BROKER_ACCOUNT_REGISTRATION_INVALID",
        "The broker-account registration is invalid.");
}
