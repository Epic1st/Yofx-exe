using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Api;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.SecretCoordination;
using YO4X.SecretIngestion.Api;

namespace YO4X.Api.Tests;

public sealed class SecretIngestionBoundaryTests
{
    [Fact]
    public async Task RawBodyReaderAcceptsBoundedOctetStreamAndMaterialClearsItself()
    {
        byte[] payload = Encoding.UTF8.GetBytes("test-only-credential");
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/octet-stream";
        context.Request.ContentLength = payload.Length;
        context.Request.Body = new MemoryStream(payload, writable: false);

        SecretMaterial material = await SecretBodyReader.ReadAsync(context.Request, CancellationToken.None);
        byte[] owned = material.Bytes.ToArray();
        material.Dispose();

        Assert.Equal(payload, owned);
        Assert.Throws<ObjectDisposedException>(() => material.Bytes.ToArray());
    }

    [Fact]
    public async Task RawBodyReaderRejectsOversizedContentBeforeReadingIt()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/octet-stream";
        context.Request.ContentLength = SecretBodyReader.MaximumBytes + 1;
        context.Request.Body = new ThrowingStream();

        BadHttpRequestException exception = await Assert.ThrowsAsync<BadHttpRequestException>(async () =>
            await SecretBodyReader.ReadAsync(context.Request, CancellationToken.None));

        Assert.Equal((int)HttpStatusCode.RequestEntityTooLarge, exception.StatusCode);
    }

    [Fact]
    public async Task RawBodyReaderBoundsChunkedBodiesWithoutContentLength()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/octet-stream";
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream(
            new byte[SecretBodyReader.MaximumBytes + 1],
            writable: false);

        BadHttpRequestException exception = await Assert.ThrowsAsync<BadHttpRequestException>(async () =>
            await SecretBodyReader.ReadAsync(context.Request, CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
    }

    [Fact]
    public async Task RawBodyReaderRejectsJsonMediaType()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 2;
        context.Request.Body = new MemoryStream("{}"u8.ToArray(), writable: false);

        BadHttpRequestException exception = await Assert.ThrowsAsync<BadHttpRequestException>(async () =>
            await SecretBodyReader.ReadAsync(context.Request, CancellationToken.None));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, exception.StatusCode);
    }

    [Fact]
    public void ProofReaderHashesBearerAndNonceWithoutExposingThem()
    {
        const string bearer = "0123456789abcdef0123456789abcdef";
        const string nonce = "abcdef0123456789abcdef0123456789";
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {bearer}";
        context.Request.Headers.Origin = "https://desktop.example";
        context.Request.Headers[ApiHeaders.IngestionNonce] = nonce;

        Guid tenantId = Guid.NewGuid();
        bool valid = IngestionProofReader.TryRead(
            context.Request,
            tenantId,
            Guid.NewGuid(),
            out CredentialIngestionProof? proof);

        Assert.True(valid);
        Assert.NotNull(proof);
        Assert.Equal(tenantId, proof.TenantId);
        Assert.Equal(Hash(bearer), proof.BearerHash);
        Assert.Equal(Hash(nonce), proof.NonceHash);
        Assert.DoesNotContain(bearer, proof.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nonce, proof.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://desktop.example")]
    [InlineData("https://desktop.example/path")]
    [InlineData("not-an-origin")]
    public void ProofReaderRejectsNonExactHttpsOrigin(string origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {new string('a', 32)}";
        context.Request.Headers.Origin = origin;
        context.Request.Headers[ApiHeaders.IngestionNonce] = new string('b', 32);

        Assert.False(IngestionProofReader.TryRead(context.Request, Guid.NewGuid(), Guid.NewGuid(), out _));
    }

    [Fact]
    public async Task CompleteTlsConfigurationAndExternalProviderWireThePostgresProcessor()
    {
        IConfiguration configuration = Configuration(
            "Host=db.example;Database=yo4x;Username=yo4x_secret_ingestion;Password=test-only;SSL Mode=VerifyFull",
            "https://portal.example");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddExternalWriteOnlySecretBroker<TestSecretBroker>();
        services.TryAddSecretIngestionPostgres(configuration);
        services.TryAddScoped<ICredentialIngestionProcessor, UnavailableCredentialIngestionProcessor>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Assert.IsType<CredentialIngestionProcessor>(
            scope.ServiceProvider.GetRequiredService<ICredentialIngestionProcessor>());
        Assert.NotNull(provider.GetService<PostgresDatabase>());
    }

    [Fact]
    public void MissingExternalProviderKeepsIngestionUnavailable()
    {
        IConfiguration configuration = Configuration(
            "Host=db.example;Database=yo4x;Username=yo4x_secret_ingestion;Password=test-only;SSL Mode=VerifyFull",
            "https://portal.example");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddSecretIngestionPostgres(configuration);
        services.TryAddScoped<ICredentialIngestionProcessor, UnavailableCredentialIngestionProcessor>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableCredentialIngestionProcessor>(
            scope.ServiceProvider.GetRequiredService<ICredentialIngestionProcessor>());
        Assert.Null(provider.GetService<PostgresDatabase>());
    }

    [Theory]
    [InlineData("yo4x_control_api", "VerifyFull")]
    [InlineData("yo4x_secret_ingestion", "Require")]
    [InlineData("yo4x_secret_ingestion", "Disable")]
    public void WrongRoleOrTlsModeKeepsIngestionUnavailable(string role, string sslMode)
    {
        IConfiguration configuration = Configuration(
            $"Host=db.example;Database=yo4x;Username={role};Password=test-only;SSL Mode={sslMode}",
            "https://portal.example");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddExternalWriteOnlySecretBroker<TestSecretBroker>();

        services.TryAddSecretIngestionPostgres(configuration);
        services.TryAddScoped<ICredentialIngestionProcessor, UnavailableCredentialIngestionProcessor>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableCredentialIngestionProcessor>(
            scope.ServiceProvider.GetRequiredService<ICredentialIngestionProcessor>());
        Assert.Null(provider.GetService<PostgresDatabase>());
    }

    [Fact]
    public void InvalidConfiguredCorsOriginKeepsIngestionUnavailable()
    {
        IConfiguration configuration = Configuration(
            "Host=db.example;Database=yo4x;Username=yo4x_secret_ingestion;Password=test-only;SSL Mode=VerifyFull",
            "https://portal.example/path");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddExternalWriteOnlySecretBroker<TestSecretBroker>();

        services.TryAddSecretIngestionPostgres(configuration);
        services.TryAddScoped<ICredentialIngestionProcessor, UnavailableCredentialIngestionProcessor>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableCredentialIngestionProcessor>(
            scope.ServiceProvider.GetRequiredService<ICredentialIngestionProcessor>());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static IConfiguration Configuration(string connectionString, string origin) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["SecretIngestion:ApprovedClientOrigin"] = origin
        }).Build();

    private sealed class TestSecretBroker : IWriteOnlySecretBroker
    {
        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public Task<SecretWriteReceipt> WriteAsync(
            SecretWriteBinding binding,
            SecretMaterial material,
            CancellationToken cancellationToken) =>
            Task.FromException<SecretWriteReceipt>(new InvalidOperationException("Test provider does not write."));

        public ValueTask<bool> VerifyReceiptAsync(
            SecretWriteReceipt receipt,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The body must not be read.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
