#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using YO4X.LocalSecrets.Windows;

namespace YO4X.Desktop;

/// <summary>
/// On-device vault writes and live strategy start/stop. Control Plane never
/// sees the MT5 password and never runs the strategy process.
/// </summary>
internal static class DesktopLocalRuntime
{
    private static readonly ConcurrentDictionary<string, ControlSession> ControlSessions =
        new(StringComparer.OrdinalIgnoreCase);
    internal static async Task StoreCredentialAsync(
        ulong login,
        string server,
        string bindingFingerprint,
        ReadOnlyMemory<byte> passwordUtf8,
        CancellationToken cancellationToken)
    {
        string expectedKey = LocalCredentialKey.Create(login, server);
        if (!FixedTimeHexEquals(expectedKey, bindingFingerprint))
        {
            throw new InvalidOperationException("The broker-account binding is invalid.");
        }

        byte[] owned = passwordUtf8.ToArray();
        try
        {
            using var credential = new LocalMt5Credential(login, server, owned);
            if (!FixedTimeHexEquals(credential.CredentialKey, expectedKey))
            {
                throw new InvalidOperationException("The broker-account binding is invalid.");
            }

            var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
            await vault.StoreAsync(
                    credential,
                    LocalCredentialWriteMode.CreateOrVerify,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    internal static Task StartBotAsync(DesktopBotInstance bot, CancellationToken cancellationToken) =>
        DesktopLiveBotHost.Instance.StartAsync(bot, cancellationToken);

    internal static async Task StartAuthorizedBotAsync(
        Guid botId,
        Uri controlApiOrigin,
        string accessToken,
        string? developmentCertificateSha256,
        CancellationToken cancellationToken)
    {
        if (ControlSessions.ContainsKey(botId.ToString("D")))
            return;
        var control = new DesktopControlPlaneRuntime(
            controlApiOrigin, accessToken, developmentCertificateSha256);
        DesktopExecutionBundle? acquired = null;
        try
        {
            acquired = await control.AcquireAsync(botId, cancellationToken).ConfigureAwait(false);
            using DesktopExecutionBundle bundle = acquired;
            await DesktopLiveBotHost.Instance.StartAuthorizedAsync(bundle, cancellationToken)
                .ConfigureAwait(false);
            await control.ReportAsync(
                bundle.ExecutionId, bundle.ExecutionToken, "RUNNING", null, cancellationToken)
                .ConfigureAwait(false);
            var session = new ControlSession(
                botId.ToString("D"), bundle.ExecutionId, bundle.ExecutionToken, control);
            if (!ControlSessions.TryAdd(session.BotId, session))
            {
                session.Dispose();
                await DesktopLiveBotHost.Instance.StopAsync(session.BotId, cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("This bot already has a local execution session.");
            }
            session.Timer = new Timer(
                _ => _ = HeartbeatAsync(session),
                null,
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(20));
            control = null!;
            acquired = null;
        }
        catch
        {
            if (acquired is not null)
            {
                try
                {
                    await control.ReportAsync(
                        acquired.ExecutionId,
                        acquired.ExecutionToken,
                        "FAULTED",
                        "The desktop rejected or failed to start the authorized package.",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }
            throw;
        }
        finally
        {
            acquired?.Dispose();
            control?.Dispose();
        }
    }

    internal static async Task StopBotAsync(string botId, CancellationToken cancellationToken)
    {
        await DesktopLiveBotHost.Instance.StopAsync(botId, cancellationToken).ConfigureAwait(false);
        if (ControlSessions.TryRemove(botId, out ControlSession? session))
        {
            try
            {
                await session.Control.ReportAsync(
                    session.ExecutionId, session.ExecutionToken, "STOPPED", null, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                session.Dispose();
            }
        }
    }

    private static async Task HeartbeatAsync(ControlSession session)
    {
        if (Interlocked.CompareExchange(ref session.HeartbeatGate, 1, 0) != 0)
            return;
        try
        {
            await session.Control.ReportAsync(
                session.ExecutionId, session.ExecutionToken, "RUNNING", null, CancellationToken.None)
                .ConfigureAwait(false);
            session.ConsecutiveFailures = 0;
        }
        catch
        {
            if (Interlocked.Increment(ref session.ConsecutiveFailures) >= 3
                && ControlSessions.TryRemove(session.BotId, out _))
            {
                try
                {
                    await DesktopLiveBotHost.Instance.StopAsync(session.BotId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                finally
                {
                    session.Dispose();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref session.HeartbeatGate, 0);
        }
    }

    internal static bool TryParseLogin(string? value, out ulong login)
    {
        login = 0;
        string text = value?.Trim() ?? string.Empty;
        return text.Length is >= 1 and <= 20
            && text.AsSpan().TrimStart('0').Length > 0
            && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out login)
            && login != 0;
    }

    private static bool FixedTimeHexEquals(string expected, string? candidate)
    {
        string trimmed = candidate?.Trim().ToLowerInvariant() ?? string.Empty;
        if (trimmed.Length != expected.Length
            || !trimmed.AsSpan().TrimStart("0123456789abcdef".AsSpan()).IsEmpty
            || expected.Length % 2 != 0)
        {
            return false;
        }

        try
        {
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
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class ControlSession(
        string botId,
        Guid executionId,
        string executionToken,
        DesktopControlPlaneRuntime control) : IDisposable
    {
        internal string BotId { get; } = botId;
        internal Guid ExecutionId { get; } = executionId;
        internal string ExecutionToken { get; } = executionToken;
        internal DesktopControlPlaneRuntime Control { get; } = control;
        internal Timer? Timer { get; set; }
        internal int HeartbeatGate;
        internal int ConsecutiveFailures;

        public void Dispose()
        {
            Timer?.Dispose();
            Control.Dispose();
        }
    }
}
