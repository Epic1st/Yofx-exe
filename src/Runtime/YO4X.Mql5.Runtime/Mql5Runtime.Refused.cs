namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 built-ins this runtime refuses outright: the whole file and folder
/// surface. Every member here is <b>Unsupported</b> and throws
/// <see cref="Mql5UnsupportedOperationException"/> naming itself.
///
/// They are declared rather than omitted so that a converted strategy which calls one
/// still compiles and then fails loudly at the exact call, naming the function. An
/// omitted member would fail at code generation with a less useful message; a
/// silently-succeeding stub would be far worse, because the strategy would carry on
/// with data it never read.
///
/// <b>Why files stay refused.</b> A file outlives the run. A strategy that reads its
/// own state back from one replays differently depending on what the previous run
/// wrote, and the corpus shows exactly that shape: the one expert here that opens a
/// file uses it to stamp a trial-period start date and then refuses to trade once the
/// stamp is old enough. MetaTrader's own tester does give each agent a file sandbox,
/// but that sandbox persists between passes, and <c>FILE_COMMON</c> - which that
/// expert passes - escapes it into a folder shared by every terminal on the machine.
/// So even MetaTrader's model does not make this reproducible; there is no version of
/// file I/O that a backtest can honestly depend on.
///
/// Terminal global variables used to be refused here too, on the grounds that they
/// outlive the program. In the tester they do not: see
/// <c>Mql5Runtime.Globals.cs</c>, which implements the family as per-run storage.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>FileOpen</c>. Unsupported.</summary>
    int FileOpen(string? fileName, int openFlags, short delimiter = 9, uint codepage = 0);

    /// <summary>MQL5 <c>FileClose</c>. Unsupported.</summary>
    void FileClose(int fileHandle);

    /// <summary>MQL5 <c>FileDelete</c>. Unsupported.</summary>
    bool FileDelete(string? fileName, int commonFlag = 0);

    /// <summary>MQL5 <c>FileCopy</c>. Unsupported.</summary>
    bool FileCopy(string? sourceFileName, int commonFlag, string? destinationFileName, int modeFlags);

    /// <summary>MQL5 <c>FileMove</c>. Unsupported.</summary>
    bool FileMove(string? sourceFileName, int commonFlag, string? destinationFileName, int modeFlags);

    /// <summary>MQL5 <c>FileIsExist</c>. Unsupported.</summary>
    bool FileIsExist(string? fileName, int commonFlag = 0);

    /// <summary>MQL5 <c>FileIsEnding</c>. Unsupported.</summary>
    bool FileIsEnding(int fileHandle);

    /// <summary>MQL5 <c>FileIsLineEnding</c>. Unsupported.</summary>
    bool FileIsLineEnding(int fileHandle);

    /// <summary>MQL5 <c>FileSeek</c>. Unsupported.</summary>
    bool FileSeek(int fileHandle, long offset, int origin);

    /// <summary>MQL5 <c>FileSize</c>. Unsupported.</summary>
    ulong FileSize(int fileHandle);

    /// <summary>MQL5 <c>FileTell</c>. Unsupported.</summary>
    ulong FileTell(int fileHandle);

    /// <summary>MQL5 <c>FileFlush</c>. Unsupported.</summary>
    void FileFlush(int fileHandle);

    /// <summary>MQL5 <c>FileReadString</c>. Unsupported.</summary>
    string FileReadString(int fileHandle, int length = -1);

    /// <summary>MQL5 <c>FileReadDouble</c>. Unsupported.</summary>
    double FileReadDouble(int fileHandle);

    /// <summary>MQL5 <c>FileReadInteger</c>. Unsupported.</summary>
    int FileReadInteger(int fileHandle, int size = 4);

    /// <summary>MQL5 <c>FileReadLong</c>. Unsupported.</summary>
    long FileReadLong(int fileHandle);

    /// <summary>MQL5 <c>FileReadNumber</c>. Unsupported.</summary>
    double FileReadNumber(int fileHandle);

    /// <summary>MQL5 <c>FileReadBool</c>. Unsupported.</summary>
    bool FileReadBool(int fileHandle);

    /// <summary>MQL5 <c>FileReadDatetime</c>. Unsupported.</summary>
    long FileReadDatetime(int fileHandle);

    /// <summary>MQL5 <c>FileReadFloat</c>. Unsupported.</summary>
    float FileReadFloat(int fileHandle);

    /// <summary>MQL5 <c>FileReadArray</c>. Unsupported.</summary>
    uint FileReadArray<T>(int fileHandle, ref T[]? array, int start = 0, int count = Mql5Constants.WholeArray);

    /// <summary>MQL5 <c>FileReadStruct</c>. Unsupported.</summary>
    uint FileReadStruct(int fileHandle, int size = -1);

    /// <summary>MQL5 <c>FileWrite</c>. Unsupported.</summary>
    uint FileWrite(int fileHandle, params object?[]? arguments);

    /// <summary>MQL5 <c>FileWriteString</c>. Unsupported.</summary>
    uint FileWriteString(int fileHandle, string? text, int length = -1);

    /// <summary>MQL5 <c>FileWriteDouble</c>. Unsupported.</summary>
    uint FileWriteDouble(int fileHandle, double value);

    /// <summary>MQL5 <c>FileWriteInteger</c>. Unsupported.</summary>
    uint FileWriteInteger(int fileHandle, int value, int size = 4);

    /// <summary>MQL5 <c>FileWriteLong</c>. Unsupported.</summary>
    uint FileWriteLong(int fileHandle, long value);

    /// <summary>MQL5 <c>FileWriteFloat</c>. Unsupported.</summary>
    uint FileWriteFloat(int fileHandle, float value);

    /// <summary>MQL5 <c>FileWriteArray</c>. Unsupported.</summary>
    uint FileWriteArray<T>(int fileHandle, T[]? array, int start = 0, int count = Mql5Constants.WholeArray);

    /// <summary>MQL5 <c>FileWriteStruct</c>. Unsupported.</summary>
    uint FileWriteStruct(int fileHandle, int size = -1);

    /// <summary>MQL5 <c>FolderCreate</c>. Unsupported.</summary>
    bool FolderCreate(string? folderName, int commonFlag = 0);

    /// <summary>MQL5 <c>FolderDelete</c>. Unsupported.</summary>
    bool FolderDelete(string? folderName, int commonFlag = 0);

    /// <summary>MQL5 <c>FolderClean</c>. Unsupported.</summary>
    bool FolderClean(string? folderName, int commonFlag = 0);

    /// <summary>MQL5 <c>FileFindFirst</c>. Unsupported.</summary>
    long FileFindFirst(string? fileFilter, ref string returnedFileName, int commonFlag = 0);

    /// <summary>MQL5 <c>FileFindNext</c>. Unsupported.</summary>
    bool FileFindNext(long searchHandle, ref string returnedFileName);

    /// <summary>MQL5 <c>FileFindClose</c>. Unsupported.</summary>
    void FileFindClose(long searchHandle);

}

public sealed partial class Mql5Runtime
{
    private const string FileReason =
        "this library performs no file I/O; a file outlives the run, so a strategy that reads its own state back from one - a saved position table, a trial-period stamp - replays differently depending on what the previous run wrote";

    /// <inheritdoc />
    public int FileOpen(string? fileName, int openFlags, short delimiter = 9, uint codepage = 0)
        => throw Refuse(nameof(FileOpen), FileReason);

    /// <inheritdoc />
    public void FileClose(int fileHandle) => throw Refuse(nameof(FileClose), FileReason);

    /// <inheritdoc />
    public bool FileDelete(string? fileName, int commonFlag = 0) => throw Refuse(nameof(FileDelete), FileReason);

    /// <inheritdoc />
    public bool FileCopy(string? sourceFileName, int commonFlag, string? destinationFileName, int modeFlags)
        => throw Refuse(nameof(FileCopy), FileReason);

    /// <inheritdoc />
    public bool FileMove(string? sourceFileName, int commonFlag, string? destinationFileName, int modeFlags)
        => throw Refuse(nameof(FileMove), FileReason);

    /// <inheritdoc />
    public bool FileIsExist(string? fileName, int commonFlag = 0) => throw Refuse(nameof(FileIsExist), FileReason);

    /// <inheritdoc />
    public bool FileIsEnding(int fileHandle) => throw Refuse(nameof(FileIsEnding), FileReason);

    /// <inheritdoc />
    public bool FileIsLineEnding(int fileHandle) => throw Refuse(nameof(FileIsLineEnding), FileReason);

    /// <inheritdoc />
    public bool FileSeek(int fileHandle, long offset, int origin) => throw Refuse(nameof(FileSeek), FileReason);

    /// <inheritdoc />
    public ulong FileSize(int fileHandle) => throw Refuse(nameof(FileSize), FileReason);

    /// <inheritdoc />
    public ulong FileTell(int fileHandle) => throw Refuse(nameof(FileTell), FileReason);

    /// <inheritdoc />
    public void FileFlush(int fileHandle) => throw Refuse(nameof(FileFlush), FileReason);

    /// <inheritdoc />
    public string FileReadString(int fileHandle, int length = -1) => throw Refuse(nameof(FileReadString), FileReason);

    /// <inheritdoc />
    public double FileReadDouble(int fileHandle) => throw Refuse(nameof(FileReadDouble), FileReason);

    /// <inheritdoc />
    public int FileReadInteger(int fileHandle, int size = 4) => throw Refuse(nameof(FileReadInteger), FileReason);

    /// <inheritdoc />
    public long FileReadLong(int fileHandle) => throw Refuse(nameof(FileReadLong), FileReason);

    /// <inheritdoc />
    public double FileReadNumber(int fileHandle) => throw Refuse(nameof(FileReadNumber), FileReason);

    /// <inheritdoc />
    public bool FileReadBool(int fileHandle) => throw Refuse(nameof(FileReadBool), FileReason);

    /// <inheritdoc />
    public long FileReadDatetime(int fileHandle) => throw Refuse(nameof(FileReadDatetime), FileReason);

    /// <inheritdoc />
    public float FileReadFloat(int fileHandle) => throw Refuse(nameof(FileReadFloat), FileReason);

    /// <inheritdoc />
    public uint FileReadArray<T>(int fileHandle, ref T[]? array, int start = 0, int count = Mql5Constants.WholeArray)
        => throw Refuse(nameof(FileReadArray), FileReason);

    /// <inheritdoc />
    public uint FileReadStruct(int fileHandle, int size = -1) => throw Refuse(nameof(FileReadStruct), FileReason);

    /// <inheritdoc />
    public uint FileWrite(int fileHandle, params object?[]? arguments) => throw Refuse(nameof(FileWrite), FileReason);

    /// <inheritdoc />
    public uint FileWriteString(int fileHandle, string? text, int length = -1) => throw Refuse(nameof(FileWriteString), FileReason);

    /// <inheritdoc />
    public uint FileWriteDouble(int fileHandle, double value) => throw Refuse(nameof(FileWriteDouble), FileReason);

    /// <inheritdoc />
    public uint FileWriteInteger(int fileHandle, int value, int size = 4) => throw Refuse(nameof(FileWriteInteger), FileReason);

    /// <inheritdoc />
    public uint FileWriteLong(int fileHandle, long value) => throw Refuse(nameof(FileWriteLong), FileReason);

    /// <inheritdoc />
    public uint FileWriteFloat(int fileHandle, float value) => throw Refuse(nameof(FileWriteFloat), FileReason);

    /// <inheritdoc />
    public uint FileWriteArray<T>(int fileHandle, T[]? array, int start = 0, int count = Mql5Constants.WholeArray)
        => throw Refuse(nameof(FileWriteArray), FileReason);

    /// <inheritdoc />
    public uint FileWriteStruct(int fileHandle, int size = -1) => throw Refuse(nameof(FileWriteStruct), FileReason);

    /// <inheritdoc />
    public bool FolderCreate(string? folderName, int commonFlag = 0) => throw Refuse(nameof(FolderCreate), FileReason);

    /// <inheritdoc />
    public bool FolderDelete(string? folderName, int commonFlag = 0) => throw Refuse(nameof(FolderDelete), FileReason);

    /// <inheritdoc />
    public bool FolderClean(string? folderName, int commonFlag = 0) => throw Refuse(nameof(FolderClean), FileReason);

    /// <inheritdoc />
    public long FileFindFirst(string? fileFilter, ref string returnedFileName, int commonFlag = 0)
        => throw Refuse(nameof(FileFindFirst), FileReason);

    /// <inheritdoc />
    public bool FileFindNext(long searchHandle, ref string returnedFileName) => throw Refuse(nameof(FileFindNext), FileReason);

    /// <inheritdoc />
    public void FileFindClose(long searchHandle) => throw Refuse(nameof(FileFindClose), FileReason);
}
