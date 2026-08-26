using System.Text;
using System.Text.Json;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;

namespace YO4X.Api.Tests;

/// <summary>
/// The link dialog collects an MT5 password, so these fix where that plaintext
/// may go: into the on-device vault under a re-derived binding key, and nowhere
/// near PostgreSQL, a log line, or a rendered string. Every credential here is
/// synthetic.
/// </summary>
public sealed class BrokerAccountLinkCredentialTests
{
    private const string Secret = "synthetic-link-secret";
    private static readonly Guid BrokerProfileId = Guid.Parse("30000000-0000-4000-8000-000000000003");

    /// <summary>
    /// The same vector <c>YO4X.LocalSecrets.Windows.Tests</c> and the browser
    /// binding both pin. All three derivations must agree or a stored credential
    /// becomes unfindable.
    /// </summary>
    [Fact]
    public void CredentialKeyMatchesTheLocalSecretsBoundaryVector()
    {
        Assert.Equal(
            "ff86813c5e96c4bcdbb40541ce529d8f6d9c34b305f9da3188e157001876df75",
            BrokerAccountLinkValidation.DeriveCredentialKey(12345678UL, "Broker-Demo"));

        // Case-insensitive by construction: the vault upper-cases the server.
        Assert.Equal(
            BrokerAccountLinkValidation.DeriveCredentialKey(12345678UL, "Broker-Demo"),
            BrokerAccountLinkValidation.DeriveCredentialKey(12345678UL, "broker-demo"));
    }

    [Fact]
    public void ValidatedLinkKeepsTheSecretOutOfTheApplicationRequest()
    {
        using CreateBrokerAccountBody body = Body();
        BrokerAccountLinkRequest link = BrokerAccountLinkValidation.Validate(body);

        Assert.Equal(12345678UL, link.Login);
        Assert.Equal("******78", link.MaskedLogin);
        Assert.Equal(
            "ff86813c5e96c4bcdbb40541ce529d8f6d9c34b305f9da3188e157001876df75",
            link.CredentialKey);

        CreateBrokerAccount application = BrokerAccountLinkValidation.ToApplicationRequest(body, link);
        string serialized = JsonSerializer.Serialize(application);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        // The unmasked login is equally absent: only the mask is persisted.
        Assert.DoesNotContain("12345678", serialized, StringComparison.Ordinal);
        Assert.Equal("******78", application.MaskedLogin);
        Assert.Equal(link.CredentialKey, application.BindingFingerprint);
    }

    [Fact]
    public void FingerprintTheBrowserDidNotDeriveFromTheLoginAndServerIsRefused()
    {
        using CreateBrokerAccountBody body = Body(bindingFingerprint: new string('a', 64));

        DomainException failure = Assert.Throws<DomainException>(
            () => BrokerAccountLinkValidation.Validate(body));

        Assert.Equal("BROKER_ACCOUNT_REGISTRATION_INVALID", failure.Code);
    }

    [Fact]
    public void MaskedLoginThatDoesNotFollowFromTheLoginIsRefused()
    {
        using CreateBrokerAccountBody body = Body(maskedLogin: "****9999");

        Assert.Throws<DomainException>(() => BrokerAccountLinkValidation.Validate(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("\ttabbed")]
    public void PasswordTheCredentialFormatCannotRepresentIsRefusedBeforeAnyProcessStarts(string password)
    {
        using CreateBrokerAccountBody body = Body(password: password);

        DomainException failure = Assert.Throws<DomainException>(
            () => BrokerAccountLinkValidation.Validate(body));

        // The refusal names the rule, never the value.
        Assert.Equal("BROKER_CREDENTIAL_INVALID", failure.Code);
        Assert.Equal(
            "The password must not be empty, start or end with a space, or contain a line break.",
            failure.Message);
    }

    [Fact]
    public void OverlongPasswordIsRefused()
    {
        using CreateBrokerAccountBody body = Body(password: new string('x', Utf8Secret.MaximumBytes + 1));

        Assert.Throws<DomainException>(() => BrokerAccountLinkValidation.Validate(body));
    }

    [Fact]
    public void DisposedSecretIsZeroedAndCannotBeReadAgain()
    {
        byte[] material = Encoding.UTF8.GetBytes(Secret);
        Utf8Secret secret = Utf8Secret.TakeOwnership(material);

        Assert.Equal(Secret.Length, secret.Length);
        secret.Dispose();

        Assert.All(material, value => Assert.Equal(0, value));
        Assert.Equal(0, secret.Length);
        Assert.Throws<ObjectDisposedException>(() => { _ = secret.Use(static utf8 => utf8.Length); });
    }

    [Fact]
    public void SecretIsNeverRenderedOrSerialized()
    {
        using Utf8Secret secret = Utf8Secret.TakeOwnership(Encoding.UTF8.GetBytes(Secret));

        // A log line, an exception message, or a diagnostic dump that reaches
        // for a bound request must not be able to print the password.
        Assert.DoesNotContain(Secret, secret.ToString(), StringComparison.Ordinal);
        Assert.Contains("REDACTED", secret.ToString(), StringComparison.Ordinal);

        var options = new JsonSerializerOptions();
        options.Converters.Add(new Utf8SecretJsonConverter());
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(secret, options));
    }

    [Fact]
    public void SecretBindsFromJsonBytesAndRejectsAnOverlongValue()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new Utf8SecretJsonConverter());

        using Utf8Secret bound = JsonSerializer.Deserialize<Utf8Secret>($"\"{Secret}\"", options)!;
        Assert.Equal(
            Secret,
            bound.Use(static utf8 => Encoding.UTF8.GetString(utf8)));

        string overlong = new('x', Utf8Secret.MaximumBytes + 1);
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Utf8Secret>($"\"{overlong}\"", options));
    }

    [Fact]
    public async Task UnconfiguredVaultRefusesTheWriteInsteadOfStoringElsewhere()
    {
        var vault = new UnavailableLocalBrokerCredentialVault();
        using Utf8Secret secret = Utf8Secret.TakeOwnership(Encoding.UTF8.GetBytes(Secret));

        await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() => vault.StoreAsync(
            12345678UL,
            "Broker-Demo",
            BrokerAccountLinkValidation.DeriveCredentialKey(12345678UL, "Broker-Demo"),
            secret,
            TestContext.Current.CancellationToken));
    }

    private static CreateBrokerAccountBody Body(
        string maskedLogin = "******78",
        string? bindingFingerprint = null,
        string password = Secret) =>
        new(
            BrokerProfileId,
            "Broker-Demo",
            "12345678",
            maskedLogin,
            bindingFingerprint
                ?? BrokerAccountLinkValidation.DeriveCredentialKey(12345678UL, "Broker-Demo"),
            BrokerAccountEnvironment.Demo,
            Utf8Secret.TakeOwnership(Encoding.UTF8.GetBytes(password)));
}
