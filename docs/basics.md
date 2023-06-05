# Basics

## Start here

The .NET documentation has a good introduction for `Span`:

* [Memory- and span-related types](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)
* [Usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)

## APIs we'll use

### .NET

* `Span<T>` / `ReadOnlySpan<T>` - stack only, contiguous memory references
* `Memory<T>` / `ReadOnlyMemory<T>` - contiguous memory references
* `ReadOnlySequence<T>` - sequences of contiguous memory references
* `ArrayPool<T>` and `MemoryPool<T>` - rent memory rather than allocate it
* `IBufferWriter<T>` - write-only output sink used in high-performance scenarios

### dotNext

[dotNext](https://dotnet.github.io/dotNext) is a library with useful extensions for .NET. We'll use a few APIs from there to simplify the code.

* `MemoryRental<T>` - `Span<T>`/`ArrayPool<T>` wrapper that returns the rented array to the pool when disposed
* `PooledArrayBufferWriter<T>` - `IBufferWriter<T>` implementation that uses `ArrayPool<T>`

### Always be on the lookout for `Span`-aware APIs

Many .NET classes have been enhanced with `Span` support. A few notable ones:

* `BitConverter`
* `Encoding`
* `HashAlgorithm`
* `Path`
* `Regex`
* `Socket`
* `Stream`
* `StreamReader`
* `String`
* `StringBuilder`
* Primitive types (for example, `Int32`) implementing `ISpanFormattable` and `ISpanParseable`

When using APIs that accept **arrays or strings**, look for `Span`-based alternatives.

## Object pooling

Pools allow us to "rent" objects rather than allocate new ones, which can have significant performance benefits as it reduces GC work.

* `ArrayPool<T>` is for renting arrays.
* `MemoryPool<T>` is similar, only it returns `Memory<T>`.
* `ObjectPool<T>` (`Microsoft.Extensions.ObjectPool` package) can create pools for any object, such as `StringBuilder`.

⚠️ Return the rented object when done or it could lead to decreased performance.

## Stack allocations

C# allows us to allocate arrays of [unmanaged types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/unmanaged-types) (value types or pointer types) on the stack using `stackalloc`. Historically, this expression's type was a pointer, but now it can also produce a `Span<T>` (which doesn't require `unsafe` code).

💡 Note the brackets around the expression are required if we want to use `var`.

```cs
var span = (stackalloc int[10]);
```

**Stack space is limited**, so we should only use this for small arrays (typically less than 1024 bytes) or we could end up with a stack overflow. If we don't know the size ahead of time, we'll need to set some threshold that would either incur an allocation or use some memory pooling option (for example, `ArrayPool<T>`). This leads to cumbersome code, as the code path that uses the rented memory also needs to return it to the pool. `MemoryRental<T>` (from dotNext) is a `ref struct` that abstracts this using the disposable pattern.

⚠️ Avoid using `stackalloc` inside loops. Allocate the memory outside the loop.

### Example: Reverse a string

💡 `Span<char>.ToString` returns a string that contains the characters, rather than the type name.

💡 This method could be written more efficiently using `String.Create`. See [Strings](strings.md).

```cs
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

⚠️ This method bypasses `ReadOnlyMemory<T>`'s immutability, so use with caution. It's advised to treat the returned array as read-only.

#### Example: `BinaryData`

`BinaryData` (from the `System.Memory.Data` package) has a `ToMemory()` method that's an _O_(1) operation which does not copy data (a more fitting name would be `AsMemory`). However its `ToArray()` method is an _O_(n) that does copy data. We can use `TryGetArray()` to avoid the copy.

```cs
var client = new BlobClient(...);
var result = await client.DownloadContentAsync(cancellationToken);
MemoryMarshal.TryGetArray(result.Value.Content.ToMemory(), out var bytes);
```