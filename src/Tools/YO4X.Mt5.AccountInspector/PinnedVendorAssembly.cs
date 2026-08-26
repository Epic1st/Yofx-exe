using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace YO4X.Mt5.AccountInspector;

/// <summary>
/// Loads the reviewed MetaTrader vendor assembly, and only the reviewed bytes.
///
/// <para>
/// The SHA-256 is checked before <see cref="AssemblyLoadContext"/> is allowed to see the
/// stream, so an assembly that is not the approved one never gets the chance to run a module
/// initializer. The loaded type is cached because the default load context refuses to take
/// the same assembly identity twice.
/// </para>
/// </summary>
internal static class PinnedVendorAssembly
{
    /// <summary>The only vendor artifact this tool will execute.</summary>
    internal const string ApprovedArtifactSha256 =
        "EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6";

    private static readonly Lock Gate = new();
    private static Type? cachedApiType;

    /// <summary>Verifies and loads the artifact, returning the vendor client type.</summary>
    /// <param name="artifactPath">Path to the pinned vendor assembly.</param>
    internal static Type LoadApiType(string artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        lock (Gate)
        {
            if (cachedApiType is not null)
            {
                return cachedApiType;
            }

            using FileStream artifact = OpenVerifiedArtifact(Path.GetFullPath(artifactPath));
            Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(artifact);
            cachedApiType = assembly.GetType("mtapi.mt5.MT5API", throwOnError: true, ignoreCase: false)!;
            return cachedApiType;
        }
    }

    private static FileStream OpenVerifiedArtifact(string artifactPath)
    {
        var stream = new FileStream(
            artifactPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        try
        {
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, ApprovedArtifactSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The MT5 vendor artifact does not match the approved SHA-256.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
