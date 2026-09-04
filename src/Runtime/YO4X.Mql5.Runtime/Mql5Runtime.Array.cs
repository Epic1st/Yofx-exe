using System.Text;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 array functions. Every one is <b>Native</b>.
///
/// MQL5 documents one overload per element type; these are generic instead, because
/// arity and reference-ness are what a caller needs and the element type carries
/// itself. The functions that can change an array's length take it by reference,
/// exactly as MQL5 writes them with <c>&amp;array[]</c>.
///
/// <c>ArraySetAsSeries</c> deserves a note. In MQL5 it flips the indexing direction of
/// a timeseries array so that element 0 is the newest bar. This runtime records the
/// flag against the array instance and honours it where the runtime itself fills an
/// array - the <c>Copy*</c> family reverses its output for a flagged target. It cannot
/// reverse the strategy's own subscript expressions; that is the code generator's job,
/// and <see cref="IMql5Runtime.ArrayGetAsSeries{T}"/> is how it reads the flag back.
///
/// Nothing here throws. An unallocated array has size 0, an out-of-range index is
/// ignored, and a bad argument produces MQL5's documented failure value with a code
/// left behind for <c>GetLastError</c>.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>ArraySize</c>. Returns 0 for an unallocated array. Native.</summary>
    int ArraySize<T>(T[]? array);

    /// <summary>
    /// MQL5 <c>ArrayResize</c>. Returns the new size, or -1 when
    /// <paramref name="newSize"/> is negative. Existing elements are preserved. Native.
    /// </summary>
    int ArrayResize<T>(ref T[]? array, int newSize, int reserveSize = 0);

    /// <summary>MQL5 <c>ArrayFree</c>. Releases the buffer and leaves a zero-length array. Native.</summary>
    void ArrayFree<T>(ref T[]? array);

    /// <summary>
    /// MQL5 <c>ArrayCopy</c>. Returns the number of elements copied, or -1 on error.
    /// A <paramref name="count"/> of <see cref="Mql5Constants.WholeArray"/> copies the
    /// rest of the source, and the destination grows to fit. Native.
    /// </summary>
    int ArrayCopy<T>(ref T[]? destination, T[]? source, int destinationStart = 0, int sourceStart = 0, int count = Mql5Constants.WholeArray);

    /// <summary>MQL5 <c>ArrayFill</c>. Native.</summary>
    void ArrayFill<T>(T[]? array, int start, int count, T value);

    /// <summary>MQL5 <c>ArrayInitialize</c>. Returns the number of elements written. Native.</summary>
    int ArrayInitialize<T>(T[]? array, T value);

    /// <summary>MQL5 <c>ArraySort</c>, ascending. Native.</summary>
    bool ArraySort<T>(T[]? array)
        where T : IComparable<T>;

    /// <summary>
    /// MQL5 <c>ArrayMaximum</c>. Returns the index of the largest element in the range,
    /// or -1 when the range is empty. Native.
    /// </summary>
    int ArrayMaximum<T>(T[]? array, int start = 0, int count = Mql5Constants.WholeArray)
        where T : IComparable<T>;

    /// <summary>
    /// MQL5 <c>ArrayMinimum</c>. Returns the index of the smallest element in the range,
    /// or -1 when the range is empty. Native.
    /// </summary>
    int ArrayMinimum<T>(T[]? array, int start = 0, int count = Mql5Constants.WholeArray)
        where T : IComparable<T>;

    /// <summary>
    /// MQL5 <c>ArrayBsearch</c> over an ascending array. Returns the index of the match,
    /// or of the nearest element when there is none, or -1 for an empty array. Native.
    /// </summary>
    int ArrayBsearch<T>(T[]? array, T value)
        where T : IComparable<T>;

    /// <summary>
    /// MQL5 <c>ArraySetAsSeries</c>. Records the timeseries indexing direction for
    /// <paramref name="array"/>; the <c>Copy*</c> family honours it. Native.
    /// </summary>
    bool ArraySetAsSeries<T>(T[]? array, bool flag);

    /// <summary>MQL5 <c>ArrayGetAsSeries</c>. Native.</summary>
    bool ArrayGetAsSeries<T>(T[]? array);

    /// <summary>MQL5 <c>ArrayIsSeries</c>. Native.</summary>
    bool ArrayIsSeries<T>(T[]? array);

    /// <summary>
    /// MQL5 <c>ArrayIsDynamic</c>. Every array this runtime hands out is a CLR array it
    /// can resize, so this is true for any allocated array. Native.
    /// </summary>
    bool ArrayIsDynamic<T>(T[]? array);

    /// <summary>
    /// MQL5 <c>ArrayRange</c>. Only rank 0 exists here - MQL5 multidimensional arrays
    /// are not part of the lowered corpus - so any other rank is 0. Native.
    /// </summary>
    int ArrayRange<T>(T[]? array, int rankIndex);

    /// <summary>MQL5 <c>ArrayReverse</c>. Native.</summary>
    bool ArrayReverse<T>(T[]? array, uint start = 0, uint count = uint.MaxValue);

    /// <summary>
    /// MQL5 <c>ArrayCompare</c>. Returns -1, 0 or 1, or -2 when the arguments cannot be
    /// compared. Native.
    /// </summary>
    int ArrayCompare<T>(T[]? first, T[]? second, int start1 = 0, int start2 = 0, int count = Mql5Constants.WholeArray)
        where T : IComparable<T>;

    /// <summary>MQL5 <c>ArrayInsert</c>. The destination grows to accommodate the insertion. Native.</summary>
    bool ArrayInsert<T>(ref T[]? destination, T[]? source, uint destinationStart, uint sourceStart = 0, uint count = uint.MaxValue);

    /// <summary>MQL5 <c>ArrayRemove</c>. The array shrinks by the number of elements removed. Native.</summary>
    bool ArrayRemove<T>(ref T[]? array, uint start, uint count = uint.MaxValue);

    /// <summary>MQL5 <c>ArraySwap</c>. Exchanges the two buffers. Native.</summary>
    bool ArraySwap<T>(ref T[]? first, ref T[]? second);

    /// <summary>
    /// MQL5 <c>ArrayPrint</c>. Writes a tabular dump to the log sink rather than to a
    /// terminal journal. Native.
    /// </summary>
    void ArrayPrint<T>(T[]? array, uint digits = 8, string? separator = null, ulong start = 0, ulong count = ulong.MaxValue, ulong flags = 0);
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public int ArraySize<T>(T[]? array) => array?.Length ?? 0;

    /// <inheritdoc />
    public int ArrayResize<T>(ref T[]? array, int newSize, int reserveSize = 0)
    {
        if (newSize < 0)
        {
            SetError(Mql5ErrorCodes.ArrayBadSize);
            return -1;
        }

        array ??= [];
        if (array.Length != newSize)
        {
            bool series = IsSeriesArray(array);
            T[] resized = array;
            Array.Resize(ref resized, newSize);
            array = resized;
            if (series)
            {
                SetSeriesArray(array, true);
            }
        }

        return newSize;
    }

    /// <inheritdoc />
    public void ArrayFree<T>(ref T[]? array) => array = [];

    /// <inheritdoc />
    public int ArrayCopy<T>(ref T[]? destination, T[]? source, int destinationStart = 0, int sourceStart = 0, int count = Mql5Constants.WholeArray)
    {
        if (source is null || destinationStart < 0 || sourceStart < 0)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return -1;
        }

        if (sourceStart >= source.Length)
        {
            return 0;
        }

        int available = source.Length - sourceStart;
        int wanted = count < 0 ? available : Math.Min(count, available);
        if (wanted <= 0)
        {
            return 0;
        }

        destination ??= [];
        int required = destinationStart + wanted;
        if (destination.Length < required)
        {
            T[] grown = destination;
            Array.Resize(ref grown, required);
            CarrySeriesFlag(destination, grown);
            destination = grown;
        }

        Array.Copy(source, sourceStart, destination, destinationStart, wanted);
        return wanted;
    }

    /// <inheritdoc />
    public void ArrayFill<T>(T[]? array, int start, int count, T value)
    {
        if (array is null || array.Length == 0 || count <= 0)
        {
            return;
        }

        int from = Math.Max(0, start);
        if (from >= array.Length)
        {
            return;
        }

        int limit = Math.Min(array.Length, from + count);
        for (int index = from; index < limit; index++)
        {
            array[index] = value;
        }
    }

    /// <inheritdoc />
    public int ArrayInitialize<T>(T[]? array, T value)
    {
        if (array is null || array.Length == 0)
        {
            return 0;
        }

        Array.Fill(array, value);
        return array.Length;
    }

    /// <inheritdoc />
    public bool ArraySort<T>(T[]? array)
        where T : IComparable<T>
    {
        if (array is null)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return false;
        }

        if (array.Length > 1)
        {
            Array.Sort(array);
        }

        return true;
    }

    /// <inheritdoc />
    public int ArrayMaximum<T>(T[]? array, int start = 0, int count = Mql5Constants.WholeArray)
        where T : IComparable<T>
        => Extremum(array, start, count, wantMaximum: true);

    /// <inheritdoc />
    public int ArrayMinimum<T>(T[]? array, int start = 0, int count = Mql5Constants.WholeArray)
        where T : IComparable<T>
        => Extremum(array, start, count, wantMaximum: false);

    /// <inheritdoc />
    public int ArrayBsearch<T>(T[]? array, T value)
        where T : IComparable<T>
    {
        if (array is null || array.Length == 0)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return -1;
        }

        int found = Array.BinarySearch(array, value);
        if (found >= 0)
        {
            return found;
        }

        // MQL5 answers a miss with the nearest element rather than a negative
        // complement, which is what the ~ recovers here.
        int insertion = ~found;
        if (insertion == 0)
        {
            return 0;
        }

        if (insertion >= array.Length)
        {
            return array.Length - 1;
        }

        return insertion - 1;
    }

    /// <inheritdoc />
    public bool ArraySetAsSeries<T>(T[]? array, bool flag)
    {
        if (array is null)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return false;
        }

        SetSeriesArray(array, flag);
        return true;
    }

    /// <inheritdoc />
    public bool ArrayGetAsSeries<T>(T[]? array) => IsSeriesArray(array);

    /// <inheritdoc />
    public bool ArrayIsSeries<T>(T[]? array) => IsSeriesArray(array);

    /// <inheritdoc />
    public bool ArrayIsDynamic<T>(T[]? array) => array is not null;

    /// <inheritdoc />
    public int ArrayRange<T>(T[]? array, int rankIndex) => rankIndex == 0 ? array?.Length ?? 0 : 0;

    /// <inheritdoc />
    public bool ArrayReverse<T>(T[]? array, uint start = 0, uint count = uint.MaxValue)
    {
        if (array is null)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return false;
        }

        if (start >= (uint)array.Length)
        {
            return false;
        }

        int from = (int)start;
        long span = Math.Min(count, (uint)(array.Length - from));
        if (span <= 1)
        {
            return true;
        }

        Array.Reverse(array, from, (int)span);
        return true;
    }

    /// <inheritdoc />
    public int ArrayCompare<T>(T[]? first, T[]? second, int start1 = 0, int start2 = 0, int count = Mql5Constants.WholeArray)
        where T : IComparable<T>
    {
        if (first is null || second is null || start1 < 0 || start2 < 0)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return -2;
        }

        int left = Math.Max(0, first.Length - start1);
        int right = Math.Max(0, second.Length - start2);
        int span = count < 0 ? Math.Max(left, right) : count;

        for (int offset = 0; offset < span; offset++)
        {
            bool hasLeft = offset < left;
            bool hasRight = offset < right;

            if (!hasLeft && !hasRight)
            {
                return 0;
            }

            if (!hasLeft)
            {
                return -1;
            }

            if (!hasRight)
            {
                return 1;
            }

            int comparison = first[start1 + offset].CompareTo(second[start2 + offset]);
            if (comparison != 0)
            {
                return Math.Sign(comparison);
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public bool ArrayInsert<T>(ref T[]? destination, T[]? source, uint destinationStart, uint sourceStart = 0, uint count = uint.MaxValue)
    {
        if (source is null)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return false;
        }

        destination ??= [];
        if (destinationStart > (uint)destination.Length || sourceStart > (uint)source.Length)
        {
            SetError(Mql5ErrorCodes.ArrayBadSize);
            return false;
        }

        int available = source.Length - (int)sourceStart;
        int wanted = count == uint.MaxValue ? available : (int)Math.Min(count, (uint)available);
        if (wanted <= 0)
        {
            return true;
        }

        T[] result = new T[destination.Length + wanted];
        Array.Copy(destination, 0, result, 0, (int)destinationStart);
        Array.Copy(source, (int)sourceStart, result, (int)destinationStart, wanted);
        Array.Copy(destination, (int)destinationStart, result, (int)destinationStart + wanted, destination.Length - (int)destinationStart);
        destination = result;
        return true;
    }

    /// <inheritdoc />
    public bool ArrayRemove<T>(ref T[]? array, uint start, uint count = uint.MaxValue)
    {
        if (array is null)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return false;
        }

        if (start >= (uint)array.Length)
        {
            SetError(Mql5ErrorCodes.ArrayBadSize);
            return false;
        }

        int from = (int)start;
        int available = array.Length - from;
        int wanted = count == uint.MaxValue ? available : (int)Math.Min(count, (uint)available);
        if (wanted <= 0)
        {
            return true;
        }

        T[] result = new T[array.Length - wanted];
        Array.Copy(array, 0, result, 0, from);
        Array.Copy(array, from + wanted, result, from, array.Length - from - wanted);
        array = result;
        return true;
    }

    /// <inheritdoc />
    public bool ArraySwap<T>(ref T[]? first, ref T[]? second)
    {
        (first, second) = (second, first);
        return true;
    }

    /// <inheritdoc />
    public void ArrayPrint<T>(T[]? array, uint digits = 8, string? separator = null, ulong start = 0, ulong count = ulong.MaxValue, ulong flags = 0)
    {
        if (array is null || array.Length == 0)
        {
            Emit(Mql5LogChannel.ArrayPrint, string.Empty);
            return;
        }

        string joiner = separator ?? " ";
        int from = start >= (ulong)array.Length ? array.Length : (int)start;
        long span = (long)Math.Min(count, (ulong)(array.Length - from));
        StringBuilder builder = new();

        for (long offset = 0; offset < span; offset++)
        {
            if (offset > 0)
            {
                builder.Append(joiner);
            }

            T element = array[from + offset];
            builder.Append(element is double number
                ? Mql5Format.Fixed(number, (int)Math.Min(digits, 16))
                : Mql5Format.Describe(element));
        }

        Emit(Mql5LogChannel.ArrayPrint, builder.ToString());
    }

    private int Extremum<T>(T[]? array, int start, int count, bool wantMaximum)
        where T : IComparable<T>
    {
        if (array is null || array.Length == 0)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return -1;
        }

        int from = Math.Max(0, start);
        if (from >= array.Length)
        {
            return -1;
        }

        int available = array.Length - from;
        int span = count < 0 ? available : Math.Min(count, available);
        if (span <= 0)
        {
            return -1;
        }

        int best = from;
        for (int index = from + 1; index < from + span; index++)
        {
            int comparison = array[index].CompareTo(array[best]);
            if (wantMaximum ? comparison > 0 : comparison < 0)
            {
                best = index;
            }
        }

        return best;
    }
}
