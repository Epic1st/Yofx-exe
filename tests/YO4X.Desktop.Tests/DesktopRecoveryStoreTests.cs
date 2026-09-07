namespace YO4X.Desktop.Tests;

public sealed class DesktopRecoveryStoreTests
{
    [Fact]
    public void InterruptedSessionReturnsOnlyBotsStillIntendedToRun()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-recovery-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "state.bin");
        Func<byte[], byte[]> copy = bytes => bytes.ToArray();
        Guid running = Guid.NewGuid();
        Guid explicitlyStopped = Guid.NewGuid();
        try
        {
            var firstLaunch = new DesktopRecoveryStore(path, copy, copy);
            Assert.Empty(firstLaunch.BeginSession());
            firstLaunch.RecordStart(running);
            firstLaunch.RecordStart(explicitlyStopped);
            firstLaunch.RecordStopped(explicitlyStopped);

            var recoveredLaunch = new DesktopRecoveryStore(path, copy, copy);
            Assert.Equal([running], recoveredLaunch.BeginSession());
            recoveredLaunch.RecordStopped(running);
            recoveredLaunch.CompleteCleanShutdown();

            var cleanLaunch = new DesktopRecoveryStore(path, copy, copy);
            Assert.Empty(cleanLaunch.BeginSession());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InvalidStateFailsClosedWithoutRequestingRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-recovery-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "state.bin");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(path, "not-json");
            Func<byte[], byte[]> copy = bytes => bytes.ToArray();
            var store = new DesktopRecoveryStore(path, copy, copy);
            Assert.Empty(store.BeginSession());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
