using System.Globalization;
using YO4X.LocalSecrets.Windows;

if (args.Length is < 1 or > 3)
    return 2;

string vaultRoot = Path.GetFullPath(args[0]);
string? expectedServer = args.Length == 2 ? args[1].Trim() : null;
string? expectedSuffix = args.Length == 3 ? args[2].Trim() : null;
if (args.Length == 3)
    expectedServer = args[1].Trim();
if (expectedServer is { Length: 0 } || !Directory.Exists(vaultRoot))
    return 2;

var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
var matches = new List<string>();
foreach (string path in Directory.EnumerateFiles(vaultRoot, "*.yo4xcred", SearchOption.TopDirectoryOnly))
{
    string key = Path.GetFileNameWithoutExtension(path);
    using LocalMt5Credential? credential = await vault.OpenAsync(key, CancellationToken.None)
        .ConfigureAwait(false);
    if (credential is null)
        continue;
    if (expectedServer is null)
        Console.WriteLine(
            credential.Server + "|***"
            + (credential.Login % 100).ToString("00", CultureInfo.InvariantCulture));
    else if (string.Equals(credential.Server, expectedServer, StringComparison.OrdinalIgnoreCase)
        && (expectedSuffix is null
            || credential.Login.ToString(CultureInfo.InvariantCulture).EndsWith(
                expectedSuffix,
                StringComparison.Ordinal)))
        matches.Add(credential.CredentialKey);
}

if (expectedServer is null)
    return 0;
if (matches.Count != 1)
    return 3;
Console.WriteLine(matches[0]);
return 0;
