using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YO4X.Conversion.Worker;

internal sealed record Mql5ArtifactPathSet(
    string SourceRoot,
    IReadOnlyList<string> OutputPaths);

internal static class Mql5ArtifactOutputGuard
{
    public static Mql5ArtifactPathSet Resolve(
        string sourceRoot,
        params string[] outputPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(outputPaths);
        if (outputPaths.Length is < 1 or > 6
            || outputPaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("One to six artifact output paths are required.", nameof(outputPaths));
        }

        string fullSourceRoot = NormalizePath(sourceRoot);
        if (!Directory.Exists(fullSourceRoot))
        {
            throw new DirectoryNotFoundException("The MQL5 source root does not exist.");
        }

        EnsureExistingPathChainContainsNoReparsePoint(fullSourceRoot);
        string physicalSourceRoot = ResolvePhysicalPath(fullSourceRoot);
        string[] resolvedOutputs = outputPaths.Select(NormalizePath).ToArray();
        if (resolvedOutputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != resolvedOutputs.Length)
        {
            throw new ArgumentException("Every artifact output must use a different path.", nameof(outputPaths));
        }

        var physicalOutputs = new string[resolvedOutputs.Length];
        for (int index = 0; index < resolvedOutputs.Length; index++)
        {
            string outputPath = resolvedOutputs[index];
            string physicalOutputPath = ResolvePhysicalCandidatePath(outputPath);
            physicalOutputs[index] = physicalOutputPath;
            if (IsSameOrDescendant(outputPath, fullSourceRoot)
                || IsSameOrDescendant(physicalOutputPath, physicalSourceRoot))
            {
                throw new ArgumentException(
                    "Artifact outputs cannot overwrite or be created inside the source root.",
                    nameof(outputPaths));
            }

            EnsureOutputPathStillSafe(outputPath);
        }

        if (physicalOutputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != physicalOutputs.Length)
        {
            throw new ArgumentException(
                "Every artifact output must resolve to a different physical path.",
                nameof(outputPaths));
        }

        return new Mql5ArtifactPathSet(
            fullSourceRoot,
            Array.AsReadOnly(resolvedOutputs));
    }

    public static void EnsureOutputPathStillSafe(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullOutputPath = NormalizePath(outputPath);
        if (!string.Equals(fullOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The artifact output path must already be fully resolved.", nameof(outputPath));
        }

        if (Directory.Exists(fullOutputPath))
        {
            throw new IOException("An artifact output path cannot be an existing directory.");
        }

        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An artifact output directory is required.", nameof(outputPath));
        }

        EnsureExistingPathChainContainsNoReparsePoint(fullOutputPath);
        EnsureExistingPathChainContainsNoReparsePoint(outputDirectory);
    }

    private static string NormalizePath(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RejectAmbiguousWindowsPath(path);
        }

        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            RejectAmbiguousWindowsPath(fullPath);
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static void RejectAmbiguousWindowsPath(string path)
    {
        string windowsPath = path.Replace('/', '\\');
        if (windowsPath.StartsWith("\\\\", StringComparison.Ordinal)
            || windowsPath.StartsWith("\\??\\", StringComparison.Ordinal)
            || windowsPath.Length > 2 && windowsPath.AsSpan(2).Contains(':'))
        {
            throw new ArgumentException(
                "UNC, device-namespace, and alternate-data-stream artifact paths are not accepted.");
        }

        string[] segments = windowsPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment.EndsWith(' ') || segment.EndsWith('.') || LooksLikeDosShortName(segment))
            {
                throw new ArgumentException(
                    "Ambiguous Windows path aliases are not accepted for source or artifact paths.");
            }
        }
    }

    private static bool LooksLikeDosShortName(string segment)
    {
        int extensionIndex = segment.IndexOf('.');
        ReadOnlySpan<char> stem = extensionIndex < 0
            ? segment.AsSpan()
            : segment.AsSpan(0, extensionIndex);
        int tildeIndex = stem.LastIndexOf('~');
        if (tildeIndex < 1 || tildeIndex == stem.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> suffix = stem[(tildeIndex + 1)..];
        return suffix.Length <= 6 && suffix.IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        if (candidatePath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string rootedPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            || rootPath.EndsWith(Path.AltDirectorySeparatorChar)
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureExistingPathChainContainsNoReparsePoint(string path)
    {
        string? current = path;
        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "Artifact paths and their existing ancestors cannot be reparse points.");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static string ResolvePhysicalCandidatePath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return path;
        }

        var missingSegments = new Stack<string>();
        string? existingPath = path;
        while (!string.IsNullOrWhiteSpace(existingPath)
            && !File.Exists(existingPath)
            && !Directory.Exists(existingPath))
        {
            string name = Path.GetFileName(existingPath);
            if (string.IsNullOrEmpty(name))
            {
                throw new IOException(
                    "Artifact paths could not be resolved to a stable physical location.");
            }

            missingSegments.Push(name);
            existingPath = Path.GetDirectoryName(existingPath);
        }

        if (string.IsNullOrWhiteSpace(existingPath))
        {
            throw new IOException(
                "Artifact paths could not be resolved to a stable physical location.");
        }

        string physicalPath = ResolvePhysicalPath(existingPath);
        while (missingSegments.Count > 0)
        {
            physicalPath = Path.Combine(physicalPath, missingSegments.Pop());
        }

        return Path.TrimEndingDirectorySeparator(physicalPath);
    }

    private static string ResolvePhysicalPath(string existingPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return existingPath;
        }

        const uint FileFlagBackupSemantics = 0x02000000;
        const uint VolumeNameGuid = 0x1;
        using SafeFileHandle handle = CreateFileW(
            existingPath,
            desiredAccess: 0,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException(
                "Artifact paths could not be resolved to a stable physical location.");
        }

        var buffer = new char[32_768];
        uint length = GetFinalPathNameByHandleW(
            handle,
            buffer,
            checked((uint)buffer.Length),
            VolumeNameGuid);
        if (length == 0 || length >= checked((uint)buffer.Length))
        {
            throw new IOException(
                "Artifact paths could not be resolved to a stable physical location.");
        }

        return Path.TrimEndingDirectorySeparator(new string(buffer, 0, checked((int)length)));
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);
}
