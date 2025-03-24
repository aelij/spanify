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

For example, we can use `MemoryExtensions.SequenceEqual` to compare two lists fast:

```cs
static bool SequenceEqual<T>(this List<T> listA, List<T> listB) =>
    CollectionsMarshal.AsSpan(listA).SequenceEqual(CollectionsMarshal.AsSpan(listB));
```

The `Span` is writable, so we can use it to modify the list:

```cs
static void Fill<T>(this List<T> list, T value) =>
    CollectionsMarshal.AsSpan(list).Fill(value);
```

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

Comparing it to `for` using BenchmarkDotNet shows nearly a x3 improvement:

|       Method |       Mean |    Error |   StdDev |
|------------- |-----------:|---------:|---------:|
| MultiplySpan |   796.6 ns | 10.96 ns |  9.72 ns |
|  MultiplyFor | 2,347.3 ns | 45.52 ns | 59.18 ns |

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
    var counts = new Dictionary<T, int>();

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

    return counts.All(kv => kv.Value == 0);
}
```

Using `CollectionMarshal` we can reduce the number of lookups to one per iteration. We use `Unsafe.IsNullRef` to check if the returned `ref T` is null (which, in this case, means the value was not found).

```cs
bool UnorderedSequenceEqual<T>(IEnumerable<T> a, IEnumerable<T> b) where T : notnull
{
    var counts = new Dictionary<T, int>();

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

    // for true high-performance code, replace LINQ with a loop
    return counts.All(kv => kv.Value == 0);
}
```

Comparing it to the original method using BenchmarkDotNet shows a x1.2 improvement:

|                        Method |     Mean |    Error |   StdDev |   Median |
|------------------------------ |---------:|---------:|---------:|---------:|
| UnorderedSequenceEqualMarshal | 40.38 us | 0.533 us | 0.473 us | 40.50 us |
|        UnorderedSequenceEqual | 48.16 us | 0.957 us | 2.019 us | 47.36 us |

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
