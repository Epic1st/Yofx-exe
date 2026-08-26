namespace YO4X.Domain.Tests;

/// <summary>Locates the conversion corpus from the test binary's location.</summary>
internal static class Mql5CorpusPath
{
    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln"))) directory = directory.Parent;
        string root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return Path.Combine(root, "Testing", "Mq5");
    }
}
