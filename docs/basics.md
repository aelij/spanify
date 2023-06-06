# Basics

## Start here

The .NET documentation has a good introduction for `Span`:

* [Memory- and span-related types](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)
* [Usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)

## APIs we'll use

### .NET

* `Span<T>` / `ReadOnlySpan<T>` - allocated only on the stack, can refer to arbitrary contiguous memory
* `Memory<T>` / `ReadOnlyMemory<T>` - can refer to contiguous memory
* `ReadOnlySequence<T>` - sequences of contiguous memory references
* `ArrayPool<T>` and `MemoryPool<T>` - rent memory rather than allocate it
* `IBufferWriter<T>` - write-only output sink used in high-performance scenarios

### dotNext

[dotNext](https://dotnet.github.io/dotNext) is a library with useful extensions for .NET. We'll use a few APIs from there to simplify the code.

* `MemoryRental<T>` - `Span<T>`/`ArrayPool<T>` wrapper that returns the rented array to the pool when disposed
* `PooledArrayBufferWriter<T>` - `IBufferWriter<T>` implementation that uses `ArrayPool<T>`

### Always be on the lookout for `Span`-aware APIs

Many .NET classes have been enhanced with `Span` support (and the list keeps growing). Some of the notable ones:

* Text: `String`, `StringBuilder`, `Regex`, `Encoding`, `Ascii`, `Utf8`
* Formatting: `Utf8Formatter`, `Utf8Parser`, `BinaryPrimitives`, `BitConverter`, `Base64`
* Cryptography: `RandomNumberGenerator`, `HashAlgorithm`, `AsymmetricAlgorithm`, `SymmetricAlgorithm` `X509Certificate`
* IO: `Path`, `FileSystemEntry`
* Streams and networking: `Socket`, `Stream`, `StreamReader`
* Value types (for example, `Int32`) implementing `ISpanFormattable`, `ISpanParseable`, :eight: `IUtf8SpanFormattable`, :eight: `IUtf8SpanParseable`

Additionally, `MemoryExtensions` has many extension methods.

When using APIs that accept **arrays or strings**, look for `Span`-based alternatives.

## Object pooling

Pools allow us to "rent" objects rather than allocate new ones, which can have significant performance benefits as it reduces GC work.

* `ArrayPool<T>` is for renting arrays.
* `MemoryPool<T>` is similar, only it returns `Memory<T>`.
* `ObjectPool<T>` (`Microsoft.Extensions.ObjectPool` package) can create pools for any object, such as `StringBuilder`.

:warning: Return the rented object when done or it could lead to decreased performance.

## Stack allocations

C# allows us to allocate arrays of [unmanaged types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/unmanaged-types) (value types or pointer types) on the stack using `stackalloc`. Historically, this expression's type was a pointer, but now it can also produce a `Span<T>` (which doesn't require `unsafe` code).

:bulb: Note the brackets around the expression are required if we want to use `var`.

```cs
var span = (stackalloc int[10]);
```

**Stack space is limited**, so we should only use this for small arrays (typically less than 1024 bytes) or we could end up with a stack overflow. If we don't know the size ahead of time, we'll need to set some threshold that would either incur an allocation or use some memory pooling option (for example, `ArrayPool<T>`). This leads to cumbersome code, as the code path that uses the rented memory also needs to return it to the pool. `MemoryRental<T>` (from dotNext) is a `ref struct` that abstracts this using the disposable pattern.

:warning: Avoid using `stackalloc` inside loops. Allocate the memory outside the loop. The analyzer [CA2014](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2014) provides a warning for this.

:bulb: It's a bit more efficient to stackalloc a const length rather than a variable length.

:bulb: When allocating large blocks, using `[SkipLocalsInit]` to skip zeroing the stack memory can lead to a measurable improvement.

### Example: Reverse a string

:bulb: `Span<char>.ToString` returns a string that contains the characters, rather than the type name.

:bulb: This method could be written more efficiently using `String.Create`. See example in [Strings](strings.md).

```cs
[SkipLocalsInit]
static string Reverse(string s)
{
    const int stackallocThreshold = 256;

    if (s.Length == 0) return s;
    using MemoryRental<char> result = s.Length <= stackallocThreshold ? new(stackalloc char[stackallocThreshold]) : new(s.Length);
    s.AsSpan().CopyTo(result.Span);
    result.Span.Reverse();
    return result.Span.ToString();
}
```

## `scoped` modifier

The `scoped` keyword can used to restrict the lifetime of a value.

The following method results in an error because the `Span` is used outside the `stackalloc` block.

```cs
void Method(bool condition)
{
    Span<int> span;
    if (condition)
    {
        span = stackalloc int[10]; // error CS8353
    }
    else
    {
        span = new int[100];
    }

    Parse(span);
}
```

Adding `scoped` fixes the error, as it limits the variable to the current method - it can't escape to callers.

```cs
void Method(bool condition)
{
    scoped Span<int> span;
    if (condition)
    {
        span = stackalloc int[10];
    }
    else
    {
        span = new int[100];
    }

    Parse(span);
}
```

If we try to copy the scoped `Span` to the caller, we get an error.

```cs
void Method(bool condition, out Span<int> result)
{
    scoped Span<int> span;
    if (condition)
    {
        span = stackalloc int[10];
    }
    else
    {
        span = new int[100];
    }

    Parse(span);
    result = span; // error CS8352
}
```

`scoped` can also be used to restrict parameters from being stored in fields.

For more information, see the [C# specification proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/low-level-struct-improvements.md#scoped-modifier).

## `Span<T>` vs `Memory<T>`

The following is also correct for the read-only counterparts.

|     | `Span<T>` | `Memory<T>` |
| --- | --- | --- |
| Storage | Stack only (`ref struct`) | Stack and heap |
| Supports | Arrays, pointers, managed references (`ref`) | Arrays, custom using `MemoryManager<T>` |
| Async/iterator/nested methods | No | Yes |
| Generic type parameters | No | Yes |
| Composition | Pointer and length | Object, length and index |
| Convertion | - | `AsSpan` |
| Performance | More efficient | Less efficient |
| Ownership | Stack | `IMemoryOwner<T>` |

## Working with `Memory<T>`

### Getting the underlying array

`Memory<T>` (unlike `Span<T>`) allows fetching the underlying array (if there is one) using `MemoryMarshal.TryGetArray<T>()`. This is useful when we need to pass the data to an API that accepts arrays.

:warning: Like many methods in the `System.Runtime.InteropServices` and the `System.Runtime.CompilerServices` namespaces, this method is considered unsafe, as it bypasses `ReadOnlyMemory<T>`'s immutability. It's advised to treat the returned array as read-only.

#### Example: `BinaryData`

`BinaryData` (from the `System.Memory.Data` package) has a `ToMemory()` method that's an $O(1)$ operation which does not copy data (a more fitting name would be `AsMemory`). However its `ToArray()` method is an $O(n)$ that does copy data. We can use `TryGetArray()` to avoid the copy.

```cs
var client = new BlobClient(...);
var result = await client.DownloadContentAsync(cancellationToken);
MemoryMarshal.TryGetArray(result.Value.Content.ToMemory(), out var bytes);
```
