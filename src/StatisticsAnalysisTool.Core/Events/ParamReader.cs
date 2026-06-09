using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StatisticsAnalysisTool.Core.Events;

/// <summary>
/// Helpers for reading values out of a Photon parameter dictionary. Albion boxes numbers as a mix
/// of byte/sbyte/short/int/long/float/double (and arrays/dictionaries of them), so each accessor
/// converts defensively and never throws.
/// </summary>
public static class ParamReader
{
    public static long? ToLong(object? value)
    {
        switch (value)
        {
            case null: return null;
            case long l: return l;
            case int i: return i;
            case short s: return s;
            case byte b: return b;
            case sbyte sb: return sb;
            case uint ui: return ui;
            case ushort us: return us;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static double ToDouble(object? value)
    {
        switch (value)
        {
            case null: return 0d;
            case double d: return d;
            case float f: return f;
            case long l: return l;
            case int i: return i;
            case short s: return s;
            case byte b: return b;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0d;
        }
    }

    public static int ToInt(object? value) => (int) (ToLong(value) ?? 0);

    public static short ToShort(object? value)
    {
        var l = ToLong(value) ?? 0;
        return (short) l;
    }

    public static string? ToStringValue(object? value) => value?.ToString();

    public static Guid? ToGuid(object? value)
    {
        if (value is byte[] { Length: 16 } bytes)
        {
            try
            {
                return new Guid(bytes);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Reads a parameter that may be a scalar, a typed array, an object[] or a dictionary.</summary>
    public static IReadOnlyList<double> ToDoubleList(object? value) => ToList(value, ToDouble);

    public static IReadOnlyList<long> ToLongList(object? value) => ToList(value, v => ToLong(v) ?? 0);

    private static IReadOnlyList<T> ToList<T>(object? value, Func<object?, T> convert)
    {
        switch (value)
        {
            case null:
                return Array.Empty<T>();
            case string:
                return [convert(value)];
            case IDictionary dictionary:
                return dictionary.Keys
                    .Cast<object>()
                    .OrderBy(k => ToLong(k) ?? 0)
                    .Select(k => convert(dictionary[k]))
                    .ToList();
            case IEnumerable enumerable:
                return enumerable.Cast<object?>().Select(convert).ToList();
            default:
                return [convert(value)];
        }
    }
}
