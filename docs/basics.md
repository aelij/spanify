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

* `SpanOwner<T>` (previously named `MemoryRental<T>`) - `Span<T>`/`ArrayPool<T>` wrapper that returns the rented array to the pool when disposed.
* `PoolingArrayBufferWriter<T>` (previously named `PooledArrayBufferWriter<T>`) - `IBufferWriter<T>` implementation that uses `ArrayPool<T>`.
* `Memory` - provides helper methods for allocating disposable `MemoryOwner<T>` from pools as well as constructing `ReadOnlySequence<T>` from a list of `Memory<T>` instances.

### RecyclableMemoryStream

* [Microsoft.IO.RecyclableMemoryStream](https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream) is a library that provides a memory stream implementation that relies on pooling to reduce allocations.
* Implements `IBufferWriter<byte>`.
* Can create a `ReadOnlySequence<byte>` from the stream.

### `Span`-aware APIs

Many .NET classes have been enhanced with `Span` support (and the list keeps growing). Some of the notable ones:

* Text: `String`, `StringBuilder`, `Regex`, `Encoding`, `Ascii`, `Utf8`, `Base64Url`
* Formatting: `Utf8Formatter`, `Utf8Parser`, `BinaryPrimitives`, `BitConverter`, `Base64`, `Convert`
* Value types (for example, `Int32`) implementing `ISpanFormattable`, `ISpanParseable`, `IUtf8SpanFormattable`, `IUtf8SpanParsable`
* Cryptography: `RandomNumberGenerator`, `HashAlgorithm`, `AsymmetricAlgorithm`, `SymmetricAlgorithm` `X509Certificate`
* IO: `Path`, `FileSystemEntry`, `File`, `FileStream`, `TextWriter`
* Streams and networking: `Socket`, `Stream`, `StreamReader`

Additionally, `MemoryExtensions` has many extension methods for spans.

When using APIs that accept **arrays or strings**, look for `Span`-based alternatives.

## Object pooling

Pools allow us to "rent" objects rather than allocate new ones, which can have significant performance benefits as it reduces GC work.

* `ArrayPool<T>` is for renting arrays.
* `MemoryPool<T>` is similar, only it returns `Memory<T>`.
* `ObjectPool<T>` (`Microsoft.Extensions.ObjectPool` package) can create pools for any object, such as `StringBuilder`.

> [!IMPORTANT]
> Return the rented object when done or it could lead to decreased performance.

## Stack allocations

C# allows us to allocate arrays of [unmanaged types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/unmanaged-types) (value types or pointer types) on the stack using `stackalloc`. Historically, this expression's type was a pointer, but now it can also produce a `Span<T>` (which doesn't require `unsafe` code).

> [!TIP]
> Note the brackets around the expression are required if we want to use `var`.

```cs
var span = (stackalloc int[10]);
```

**Stack space is limited**, so we should only use this for small arrays (typically less than 1024 bytes) or we could end up with a stack overflow. If we don't know the size ahead of time, we'll need to set some threshold that would either incur an allocation or use some memory pooling option (for example, `ArrayPool<T>`). This leads to cumbersome code, as the code path that uses the rented memory also needs to return it to the pool. `SpanOwner<T>` (from dotNext) is a `ref struct` that abstracts this using the disposable pattern.

> [!IMPORTANT]
> Avoid using `stackalloc` inside loops. Allocate the memory outside the loop. The analyzer [CA2014](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2014) provides a warning for this.

> [!TIP]
> It's a bit more efficient to `stackalloc` a const length rather than a variable length.

> [!TIP]
> When allocating large blocks, using `[SkipLocalsInit]` to skip zeroing the stack memory can lead to a measurable improvement.

### Example: Reverse a string

> [!TIP]
> `Span<char>.ToString` returns a string that contains the characters, rather than the type name.

> [!TIP]
> This method could be written more efficiently using `String.Create`. See example in [Strings](strings.md).

```cs
[SkipLocalsInit]
static string Reverse(string s)
{
    const int stackallocThreshold = 256;

    if (s.Length == 0) return s;
    using SpanOwner<char> result = s.Length <= stackallocThreshold ? new(stackalloc char[stackallocThreshold], s.Length) : new(s.Length);
    s.AsSpan().CopyTo(result.Span);
    result.Span.Reverse();
    return result.Span.ToString();
}
```

## Inline arrays and collection expressions

**Inline arrays** allow defining `struct`s that are treated as arrays of a fixed size. Like any other `struct`, they can hold managed references, which means we can use them to allocate inline arrays of managed objects - either on the stack or on the heap as class members. The compiler also supports indexers, conversion to span and iteration using `foreach`.

### Example: Using an inline array

```cs
class StringBuffer
{
    [InlineArray(10)]
    private struct Buffer10
    {
        private string _s;
    }

    private int _count;
    private Buffer10 _buffer;

    public void Add(string s)
    {
        _buffer[_count++] = s; // indexer access
    }

    public void CopyTo(Span<string> target)
    {
        Span<string> source = _buffer; // implicit conversion to span
        source[.._count].CopyTo(target);
    }

    public IEnumerator<string> GetEnumerator()
    {
        foreach (var item in _buffer) // iteration
        {
            yield return item;
        }
    }
}
```

**Collection expressions** are an alternative, terse syntax to initialize collections that also supports `Span<T>`, for which the compiler can generate an inline array type.

Together they provide a powerful mechanism to allocate stack arrays of statically-known sizes. They also support `params Span<T>`.

### Example: ContainsAny

We can use collection expressions whenever a method accepts spans as parameters. For example, `MemoryExtensions.ContainsAny`.

```cs
ReadOnlySpan<string> strings = ["a", "b", "c"];
strings.ContainsAny(["a", "d"]); // true
```

## Runtime optimizations

The JIT performs several memory-related optimizations. They depend on inlining, code shape and compilation mode, so they aren't guaranteed - code shouldn't rely on them, and explicit `stackalloc` remains the way to guarantee stack memory.

* **Struct promotion** - places the fields of struct locals and arguments in registers rather than in stack memory, regardless of the number or kind of fields.
* **Address exposure tracking** - taking a local's address (for example, passing it by `ref` to a method that isn't inlined) inhibits optimizations such as promotion; the JIT avoids unnecessary exposure, but keep hot locals free of address-taking where possible.
* **Unrolled memory operations** - memory operations such as comparison, copying and zeroing can be unrolled and auto-vectorized with SIMD when their sizes can be determined at compile time. `string.Equals` and `SequenceEqual` over two constants fold to the result.
* **Bounds-check elimination** - checks proven redundant are removed: in loops bounded by the length (including `i + constant < length`), for index-from-end access such as `values[^1]`, and on `span[0]` after an `IsEmpty` check. Prefer clear, safe span code before reaching for `Unsafe`.
* **Thread-static access** - access to `[ThreadStatic]` fields is optimized, making them a cheap option for per-thread caches such as reusable buffers.
* **Runtime async** - when enabled using `<Features>runtime-async=on</Features>`, async methods reuse continuation objects, avoid saving unchanged locals and avoid allocations in `await Task.Yield()`. Independently, continuations skip `ExecutionContext` capture when no `AsyncLocal<T>` state is in use.

### Stack allocation of objects

The JIT can allocate an object on the stack rather than the GC heap when *escape analysis* proves it doesn't outlive its method. An object escapes when it's stored in a non-local location (such as a field) or passed to a method that isn't inlined. Stack-allocated objects aren't tracked by the GC, and the JIT may go further and replace an object with its scalar values.

Candidates include:

* **Boxes** - value types boxed to `object` or an interface, for example when passed to an inlined method.
* **Small, fixed-size arrays** - of both value types and reference types.
* **Objects referenced by local struct fields** - as long as the struct itself doesn't escape.
* **Delegates** - a delegate that's only invoked locally. The closure holding captured variables is still heap-allocated.
* **Enumerators** - for example, enumerating an array via `IEnumerable<T>`, enabled by devirtualization and conditional escape analysis.

```cs
static int Sum()
{
    int[] numbers = { 1, 2, 3 }; // small, constant size, doesn't escape
    int sum = 0;
    foreach (var number in numbers)
    {
        sum += number;
    }

    return sum;
}
```

Since passing an object to a non-inlined call makes it escape, devirtualization and inlining are what make many allocations eligible. Keep temporary objects local to hot methods to benefit, and confirm the effect with BenchmarkDotNet's `[MemoryDiagnoser]`.

> [!IMPORTANT]
> Benchmarks should use representative runtime inputs: comparisons between literals may be optimized away entirely. Measure on the actual deployment runtime and hardware; JIT, ReadyToRun and NativeAOT results can differ.

## Code analysis

.NET comes with a few analyzers that produce `Span`- and `Memory`-related diagnostics and fixes. There are [various ways](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options) to enable them. We recommend setting [`AnalysisMode`](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#analysismode) to `Minimum`, `Recommended` or `All` in `Directory.Build.props`.

```xml
<PropertyGroup>
  <AnalysisMode>Minimum</AnalysisMode>
</PropertyGroup>
```

If this gets too noisy, we can enable only performance analyzers.

```xml
<PropertyGroup>
  <AnalysisModePerformance>Minimum</AnalysisModePerformance>
</PropertyGroup>
```

### Partial list of analyzers

* `CA1832` and `CA1833`: Use `AsSpan` or `AsMemory` instead of `Range`-based indexers
* `CA1835`: Prefer the `Memory`-based overloads for `ReadAsync` and `WriteAsync`
* `CA1844`: Provide `Memory`-based overrides of async methods when subclassing `Stream`
* `CA1845`: Use `Span`-based `string.Concat`
* `CA1846`: Prefer `AsSpan` over `Substring`

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
| Async/iterator methods | In synchronous regions; not across `await` / `yield` | Yes |
| Generic type parameters | Yes - with `allows ref struct` | Yes |
| Composition | Pointer and length | Object, length and index |
| Conversion | - | `AsSpan` |
| Performance | More efficient | Less efficient |
| Ownership | Stack | `IMemoryOwner<T>` |

`Span<T>` cannot be captured by a lambda or local function. Runtime-native async does not remove these lifetime restrictions; use `Memory<T>` when a buffer must survive an `await`.

## Working with `Memory<T>`

### Getting the underlying array

`Memory<T>` (unlike `Span<T>`) allows fetching the underlying array (if there is one) using `MemoryMarshal.TryGetArray<T>`. This is useful when we need to pass the data to an API that accepts arrays.

> [!IMPORTANT]
> Like many methods in the `System.Runtime.InteropServices` and the `System.Runtime.CompilerServices` namespaces, this method is considered unsafe, as it bypasses `ReadOnlyMemory<T>`'s immutability. It's advised to treat the returned array as read-only.

#### Example: `BinaryData`

`BinaryData` (from the `System.Memory.Data` package) has a `ToMemory()` method that's an $O(1)$ operation which does not copy data (a more fitting name would be `AsMemory`). However its `ToArray()` method is an $O(n)$ operation that does copy data. We can use `TryGetArray()` to avoid the copy.

```cs
var client = new BlobClient(...);
var result = await client.DownloadContentAsync(cancellationToken);
MemoryMarshal.TryGetArray(result.Value.Content.ToMemory(), out var bytes);
```
