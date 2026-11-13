using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class CollectionExtensions
{
    public static Span<T> Take<T>(this Span<T> span, int count) =>
        span[..Math.Clamp(count, 0, span.Length)];

    public static bool SequenceEqual<T>(this List<T> listA, List<T> listB, int count) =>
        CollectionsMarshal.AsSpan(listA).Take(count).SequenceEqual(CollectionsMarshal.AsSpan(listB).Take(count));

    public static void Fill<T>(this List<T> list, T value) =>
        CollectionsMarshal.AsSpan(list).Fill(value);

    public static void Multiply<T>(this List<T> list, T factor) where T : struct, INumber<T>
    {
        foreach (ref var item in CollectionsMarshal.AsSpan(list))
        {
            item *= factor;
        }
    }

    public static bool UnorderedSequenceEqual<T>(this IEnumerable<T> a, IEnumerable<T> b) where T : notnull
    {
        var counts = new Dictionary<T, int>(a.TryGetNonEnumeratedCount(out var capacity) ? capacity : 0);

        foreach (var item in a)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(counts, item, out _)++;
        }

        foreach (var item in b)
        {
            ref int count = ref CollectionsMarshal.GetValueRefOrNullRef(counts, item);
            if (Unsafe.IsNullRef(ref count))
            {
                return false;
            }

            count--;
        }

        foreach (var item in counts)
        {
            if (item.Value != 0)
            {
                return false;
            }
        }

        return true;
    }
}
