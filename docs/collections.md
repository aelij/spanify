# Collections

## Using `CollectionsMarshal`

`CollectionsMarshal` provides **unsafe** access to a few of the basic collections.

> [!IMPORTANT]
> The reason these methods are not members of the collection classes is that they're unsafe. We recommend using them for performance-critical code.

> [!IMPORTANT]
> Do not modify the collection while using values returned from these methods, as they provide direct references to internal arrays, which can be replaced during various operations.

### `List<T>`: `AsSpan`

`List<T>` uses an internal array that gets copied to a larger one as needed. The array's size is represented by the `Capacity` property, while `Count` represents the number of items in the list.

By using `AsSpan` we can get a slice of that array that only contains the `Count` items in the list. This can both improve performance in some cases, and is useful if we want to pass the list to `Span`-based APIs without copying the data.

For example, we can compare the first `count` elements of two lists. With LINQ:

```cs
listA.Take(count).SequenceEqual(listB.Take(count));
```

Using span slices avoids the LINQ iterator wrappers:

```cs
static bool SequenceEqual<T>(this List<T> listA, List<T> listB, int count) =>
    CollectionsMarshal.AsSpan(listA).Take(count).SequenceEqual(CollectionsMarshal.AsSpan(listB).Take(count));

static Span<T> Take<T>(this Span<T> span, int count) =>
    span[..Math.Clamp(count, 0, span.Length)];
```

Comparing the first 1,000 elements of 10,000-element lists using BenchmarkDotNet shows about a 23.6x speedup:

| Method                     | Mean        | Error     | StdDev    | Ratio |
|--------------------------- |------------:|----------:|----------:|------:|
| ListPrefixEqualsEnumerable | 2,103.60 ns | 12.930 ns | 11.462 ns | 23.63 |
| ListPrefixEqualsSpan       |    89.02 ns |  0.405 ns |  0.359 ns |  1.00 |

The `Span` is writable, so we can use it to modify the list:

```cs
static void Fill<T>(this List<T> list, T value) =>
    CollectionsMarshal.AsSpan(list).Fill(value);
```

BenchmarkDotNet results for filling a list of 10,000 integers:

| Method        | Mean       | Error    | StdDev   | Ratio |
|-------------- |-----------:|---------:|---------:|------:|
| ListFillNaive | 4,034.4 ns | 27.66 ns | 21.60 ns |  6.62 |
| ListFillSpan  |   609.5 ns |  9.60 ns |  8.98 ns |  1.00 |

Shuffle a list:

```cs
static void Shuffle<T>(this List<T> list) =>
    RandomNumberGenerator.Shuffle(CollectionsMarshal.AsSpan(list));
```

Modifying the list using the `Span` bypasses the list's change tracking, so it's possible to use an enumerator while setting values. For example:

```cs
static void Multiply<T>(this List<T> list, T factor) where T : struct, INumber<T>
{
    foreach (ref var item in CollectionsMarshal.AsSpan(list))
    {
        item *= factor;
    }
}
```

Comparing it to `for` using BenchmarkDotNet shows about a 1.9x speedup for 10,000 integers multiplied by 41:

| Method           | Mean     | Error     | StdDev    | Ratio |
|----------------- |---------:|----------:|----------:|------:|
| ListMultiplyFor  | 5.053 us | 0.0071 us | 0.0059 us |  1.94 |
| ListMultiplySpan | 2.605 us | 0.0037 us | 0.0033 us |  1.00 |

### `List<T>`: `SetCount`

`AsSpan` is limited as it cannot change the `Count` of the list. `SetCount` will expand or shrink the list's array to the desired size.

For example, we can use it to copy items from a `Span` to a list.

```cs
static void AddRange<T>(this List<T> list, ReadOnlySpan<T> span)
{
    var oldCount = list.Count;
    CollectionsMarshal.SetCount(list, oldCount + span.Length);
    span.CopyTo(CollectionsMarshal.AsSpan(list)[oldCount..]);
}
```

### `Dictionary<T>`: `GetValueRefOrNullRef` and `GetValueRefOrAddDefault`

`Dictionary<T>` uses an internal array that represents the buckets. We can use `GetValueRefOrNullRef` and `GetValueRefOrAddDefault` to get a managed reference (`ref T`) directly to the value, which we can then modify and it will **update the dictionary**. This can reduce the number of dictionary lookups.

Consider the following code - in each loop we're doing two lookups, one to retrieve the current value, and one to set it.

```cs
bool UnorderedSequenceEqual<T>(IEnumerable<T> a, IEnumerable<T> b) where T : notnull
{
    var counts = new Dictionary<T, int>(a.TryGetNonEnumeratedCount(out var capacity) ? capacity : 0);

    foreach (var item in a)
    {
        counts[item] = (counts.TryGetValue(item, out var count) ? count : 0) + 1;
    }

    foreach (var item in b)
    {
        if (!counts.TryGetValue(item, out var value))
            return false;

        counts[item] = value - 1;
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
```

Using `CollectionsMarshal` we can reduce the number of lookups to one per iteration. We use `Unsafe.IsNullRef` to check if the returned `ref T` is null (which, in this case, means the value was not found).

```cs
bool UnorderedSequenceEqual<T>(IEnumerable<T> a, IEnumerable<T> b) where T : notnull
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
```

The benchmark compares lists of 10,000 long strings with 1,000 distinct keys, with the second list reversed. BenchmarkDotNet shows about a 1.75x speedup for this workload:

| Method                      | Mean     | Error   | StdDev  | Ratio |
|---------------------------- |---------:|--------:|--------:|------:|
| UnorderedSequenceEqualNaive | 864.6 us | 2.13 us | 1.78 us |  1.75 |
| UnorderedSequenceEqualRef   | 495.0 us | 2.20 us | 1.84 us |  1.00 |

### `BitArray`: `AsBytes`

Returns a `Span<byte>` over the internal storage of a `BitArray`. This enables efficient bulk operations on bit data.

```cs
var bits = new BitArray(64);
bits[0] = true;
bits[8] = true;

Span<byte> bytes = CollectionsMarshal.AsBytes(bits);
// bytes[0] = 0b00000001, bytes[1] = 0b00000001, rest are 0

// Efficient bulk operations
bytes.Fill(0xFF); // Set all bits to 1
```

## `BitArray` construction and counting

Constructors accept `ReadOnlySpan<bool>`, `ReadOnlySpan<byte>` or `ReadOnlySpan<int>`, avoiding an intermediate `ToArray()` when the input is a slice or stack buffer. `PopCount()` counts set bits without a manual per-bit loop:

```cs
ReadOnlySpan<byte> data = [0b_0000_0101, 0b_0001_0000];
var bits = new BitArray(data);
int setBits = bits.PopCount(); // 3
```

The constructors still copy into the `BitArray`'s own storage; they do not alias the input.

## Frozen collections

The collections under the `System.Collections.Frozen` namespace are read-only and optimized for fast lookup and enumeration. Unlike immutable collections, there are no mutation methods that return a new collection; we can only provide the data during construction. They do have a few interesting performance features.

* Since they are created using factories, they can be optimized according to the number of items.
* `GetAlternateLookup` method (see below).
* The `GetValueRefOrNullRef` method (in `FrozenDictionary`), similar to the one from `CollectionsMarshal`, allows us to get a managed reference to the value. There is no `GetValueRefOrAddDefault` method, as the dictionary is read-only.

## `GetAlternateLookup` in sets and dictionaries

* The `GetAlternateLookup` method allows seeking keys using an alternate key type. The alternate key can be:
  - A `ReadOnlySpan<char>` (if the key type is a `string`), which can be used to search the collection for a substring with no additional allocations.
  - Any type created using the `Create` method of an `IAlternateEqualityComparer` configured as the collection's `IEqualityComparer`.
* Available in `HashSet`, `FrozenSet`, `Dictionary`, `FrozenDictionary`, and `ConcurrentDictionary`.

### Example: Using `GetAlternateLookup`

```cs
var dictionary = FrozenDictionary.ToFrozenDictionary<string, int>(
[
    new("one", 1),
    new("two", 2),
    new("three", 3),
]);

var searchValue = "x-two";
var searchValueSpan = searchValue.AsSpan()[2..];
var alternateLookup = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
alternateLookup.TryGetValue(searchValueSpan, out var value);
```
