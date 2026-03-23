using System.Runtime.InteropServices;

namespace Core.Com.Structs;

/// <summary>
/// Marshal managed arrays to unmanaged memory blocks with a small header:
/// [int count][int reserved][payload...].
/// Ownership rule: receiver must call DestroyArray (or the matching Read*AndDestroy method).
/// </summary>
public static class NativeArrayMarshaller
{
    private const int HeaderSize = sizeof(int) * 2;

    public static IntPtr CreateStructArray<T>(IReadOnlyList<T> values) where T : struct
    {
        if (values.Count == 0)
            return IntPtr.Zero;

        var elementSize = Marshal.SizeOf<T>();
        var totalSize = checked(HeaderSize + values.Count * elementSize);
        var buffer = Marshal.AllocCoTaskMem(totalSize);

        Marshal.WriteInt32(buffer, 0, values.Count);
        Marshal.WriteInt32(buffer, sizeof(int), 0);

        var data = DataPtr(buffer);
        for (var i = 0; i < values.Count; i++)
            Marshal.StructureToPtr(values[i], IntPtr.Add(data, i * elementSize), false);

        return buffer;
    }

    public static T[] ReadStructArrayAndDestroy<T>(IntPtr buffer) where T : struct
    {
        if (buffer == IntPtr.Zero)
            return [];

        try
        {
            return ReadStructArray<T>(buffer);
        }
        finally
        {
            DestroyArray(buffer);
        }
    }

    public static T[] ReadStructArray<T>(IntPtr buffer) where T : struct
    {
        if (buffer == IntPtr.Zero)
            return [];

        var count = ReadCount(buffer);
        if (count == 0)
            return [];

        var elementSize = Marshal.SizeOf<T>();
        var data = DataPtr(buffer);
        var result = new T[count];

        for (var i = 0; i < count; i++)
            result[i] = Marshal.PtrToStructure<T>(IntPtr.Add(data, i * elementSize));

        return result;
    }

    public static IntPtr CreateBstrArray(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return IntPtr.Zero;

        var totalSize = checked(HeaderSize + values.Count * IntPtr.Size);
        var buffer = Marshal.AllocCoTaskMem(totalSize);
        Marshal.WriteInt32(buffer, 0, values.Count);
        Marshal.WriteInt32(buffer, sizeof(int), 0);

        var data = DataPtr(buffer);
        var initialized = 0;
        try
        {
            for (var i = 0; i < values.Count; i++)
            {
                var bstr = values[i] is null ? IntPtr.Zero : Marshal.StringToBSTR(values[i]);
                Marshal.WriteIntPtr(IntPtr.Add(data, i * IntPtr.Size), bstr);
                initialized++;
            }
        }
        catch
        {
            for (var i = 0; i < initialized; i++)
            {
                var p = Marshal.ReadIntPtr(IntPtr.Add(data, i * IntPtr.Size));
                if (p != IntPtr.Zero)
                    Marshal.FreeBSTR(p);
            }

            Marshal.FreeCoTaskMem(buffer);
            throw;
        }

        return buffer;
    }

    public static string[] ReadBstrArrayAndDestroy(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return [];

        try
        {
            return ReadBstrArray(buffer);
        }
        finally
        {
            DestroyArray(buffer);
        }
    }

    public static string[] ReadBstrArray(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return [];

        var count = ReadCount(buffer);
        if (count == 0)
            return [];

        var data = DataPtr(buffer);
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var p = Marshal.ReadIntPtr(IntPtr.Add(data, i * IntPtr.Size));
            result[i] = p == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringBSTR(p) ?? string.Empty);
        }

        return result;
    }

    public static IntPtr CreateIntArray(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return IntPtr.Zero;

        var totalSize = checked(HeaderSize + values.Count * sizeof(int));
        var buffer = Marshal.AllocCoTaskMem(totalSize);
        Marshal.WriteInt32(buffer, 0, values.Count);
        Marshal.WriteInt32(buffer, sizeof(int), 0);

        var copy = values as int[] ?? values.ToArray();
        Marshal.Copy(copy, 0, DataPtr(buffer), copy.Length);
        return buffer;
    }

    public static int[] ReadIntArrayAndDestroy(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return [];

        try
        {
            return ReadIntArray(buffer);
        }
        finally
        {
            DestroyArray(buffer);
        }
    }

    public static int[] ReadIntArray(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return [];

        var count = ReadCount(buffer);
        if (count == 0)
            return [];

        var result = new int[count];
        Marshal.Copy(DataPtr(buffer), result, 0, count);
        return result;
    }

    public static IntPtr CreateFloatArray(IReadOnlyList<float> values)
    {
        if (values.Count == 0)
            return IntPtr.Zero;

        var totalSize = checked(HeaderSize + values.Count * sizeof(float));
        var buffer = Marshal.AllocCoTaskMem(totalSize);
        Marshal.WriteInt32(buffer, 0, values.Count);
        Marshal.WriteInt32(buffer, sizeof(int), 0);

        var copy = values as float[] ?? values.ToArray();
        Marshal.Copy(copy, 0, DataPtr(buffer), copy.Length);
        return buffer;
    }

    public static float[] ReadFloatArrayAndDestroy(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return [];

        try
        {
            return ReadFloatArray(buffer);
        }
        finally
        {
            DestroyArray(buffer);
        }
    }

    public static float[] ReadFloatArray(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return [];

        var count = ReadCount(buffer);
        if (count == 0)
            return [];

        var result = new float[count];
        Marshal.Copy(DataPtr(buffer), result, 0, count);
        return result;
    }

    public static void DestroyBstrArray(IntPtr buffer)
    {
        if (buffer == IntPtr.Zero)
            return;

        var count = ReadCount(buffer);
        var data = DataPtr(buffer);
        for (var i = 0; i < count; i++)
        {
            var p = Marshal.ReadIntPtr(IntPtr.Add(data, i * IntPtr.Size));
            if (p != IntPtr.Zero)
                Marshal.FreeBSTR(p);
        }

        DestroyArray(buffer);
    }

    public static void DestroyArray(IntPtr buffer)
    {
        if (buffer != IntPtr.Zero)
            Marshal.FreeCoTaskMem(buffer);
    }

    private static int ReadCount(IntPtr buffer)
    {
        var count = Marshal.ReadInt32(buffer, 0);
        if (count < 0)
            throw new InvalidOperationException($"Invalid native array count: {count}.");
        return count;
    }

    private static IntPtr DataPtr(IntPtr buffer) => IntPtr.Add(buffer, HeaderSize);
}
