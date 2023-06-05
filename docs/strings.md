# Strings

## Creating strings of a known size using `String.Create`

When creating a new string whose size is known in advance, we can use the `String.Create` method to avoid additional allocations. This method works by allocating a string and providing a writable `Span` within a delegate. It's safe (meaning the string can't be mutated after creation) since the `Span` can't escape from the delegate.

⚠️ Avoid capturing variables in the delegate as they incur an allocation - pass all data using the `state` parameter. Use the `static` keyword to enforce this.

### Example: Reversing a string

```cs
static string Reverse(this string s) =>
    string.Create(s.Length, s, static (span, str) => { str.AsSpan().CopyTo(span); span.Reverse(); });
```

### Example: Creating a random string

```cs
static string GetRandomString(int size, char min = 'a', char max = 'z') =>
    string.Create(size, (min, max), static (span, state) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)Random.Shared.Next((int)state.min, (int)state.max + 1);
        }
    });
```

### Example: Passing a `Span<T>` to the `Create` method **(Advanced)**

`ref struct`s such as `Span` can't be used in generic type parameters (since there's currently no way to prevent boxing), so if we want to base one string on another, we'll have to use `unsafe` code - a pointer. For this purpose, we can create a generic struct that will host the pointer.

⚠️ Be very careful when using unsafe code - it could easily lead to memory corruption. When we create a `Span` using unsafe methods (for example, from a pointer), there are **no bounds checks**. Extensively cover such code with tests.

```cs
readonly unsafe struct UnsafeSpan<T>(T* Source, int Length) where T : unmanaged
{
    public Span<T> AsSpan() => new(Source, Length);
}
```

```cs
unsafe static string ReplaceNonAlphanumeric(this ReadOnlySpan<char> s, char replacement)
{
    fixed (char* ptr = &s[0])
    {
        return string.Create(s.Length, (replacement, span: new UnsafeSpan<char>(ptr, s.Length)), static (span, state) =>
        {
            var sourceSpan = state.span.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                var c = sourceSpan[i];
                span[i] = char.IsLetterOrDigit(c) ? c : state.replacement;
            }
        });
    }
}
```

Using `ReadOnlySpan<char>` as the parameter rather than a string, allows us a lot of flexibility - we can stack-allocate the string, and we can transform it without allocations, as we'll see in the next section.

## Mutating strings

We can use `Span<char>` to mutate strings while minimizing allocations. The `MemoryExtensions` class contains extension methods that are similar to `System.String` methods.

For example, we can use `MemoryExtensions.Trim`, and use the range operator to get a substring.

```cs
" abcde ".AsSpan().Trim()[..3] // results in "abc"
```

To use a method that transforms the string, such as `ToLowerInvariant`, we can use `String.Create`, chaining multiple mutations within the delegate.

```cs
static string SubstringToLower(this string s, int length) =>
  string.Create(length, (s, length), static (span, state) => state.s.AsSpan()[..state.length].ToLowerInvariant(span));
```

We can also create an alternative design for the `ReplaceNonAlphanumeric` method that does not use unsafe code by adding a parameter for the destination span.

```cs
static void ReplaceNonAlphanumeric(this ReadOnlySpan<char> source, Span<char> destination, char replacement)
{
    for (int i = 0; i < destination.Length; i++)
    {
        var c = source[i];
        destination[i] = char.IsLetterOrDigit(c) ? c : replacement;
    }
}
```

To use this method, we'll need to allocate the destination span. If our final destination is a `String` we can use `String.Create`, similarly to how we called `ToLowerInvariant`.

```cs
string.Create(length: 3, " a^b ", static (span, str) => str.AsSpan().Trim()[..span.Length].ReplaceNonAlphanumeric('_', span)).Dump(); // results in "a_b"
```

## Interpolated string handlers

Interpolated string handlers are a C# feature that optimizes string creation by breaking them into multiple "append" calls, rather than allocating a string using `String.Format`, and potentially creating multiple interim strings (for example, formatting a number). This allows us to "hack" the compiler into all sorts of interesting optimizations.

Handlers available in .NET:

* `DefaultInterpolatedStringHandler` is used in a `String.Create` overload, and emitted by the compiler for regular interpolated strings.
* `AppendInterpolatedStringHandler` is used in `StringBuilder.Append`. No longer is it needed to break an interpolated string into multiple `Append` calls for efficiency.
* `AssertInterpolatedStringHandler` and `WriteIfInterpolatedStringHandler` are used in `Debug.Assert` and `Debug.WriteIf` respectively and completely skip writing according to the condition argument.
* `TryWriteInterpolatedStringHandler` is used by `MemoryExtensions.TryWrite` and allows us to efficiently write strings into a `Span<char>` (.NET 8 adds `Utf8.TryWrite` to write into a `Span<byte>`).

dotNext also includes:
* `BufferWriterInterpolatedStringHandler` that writes into an `IBufferWriter<char>`.
* `PoolingInterpolatedStringHandler` which allows using a `Memory<T>` pool rather than allocating new buffers.

💡 We can reuse any of the above handlers to write custom ones.

### Example: `TryWrite`

The following method checks if the host & port part of a `Uri` matches a list of patterns. Rather than using `Uri.GetLeftPart` which allocates a string, we'll use `TryWrite`. Because it uses an interpolated string handler, there are no additional allocations (for example, to convert the port number into a string). `Regex` also works directly with `Span<char>`.

```cs
bool MatchesUriPattern(IEnumerable<Regex> patterns, Uri uri)
{
    var maxLength = uri.Scheme.Length + "://".Length + uri.Host.Length + ":".Length + 5;
    using MemoryRental<char> hostAndPort = maxLength <= 256 ? new(stackalloc char[256]) : new(maxLength);
    hostAndPort.Span.TryWrite($"{uri.Scheme}://{uri.Host}:{uri.Port}", out var written);
    var hostAndPortSpan = hostAndPort.Span[..written];

    foreach (var pattern in patterns)
    {
        if (pattern.IsMatch(hostAndPortSpan))
        {
            return true;
        }
    }

    return false;
}
```

## Hashing a string

To get a hexadecimal string representing the hash of a string, we'll use the `SHA256` algorithm (it could be replaced with others available in .NET).

⚠️ The methods below will produce different hashes.

### Using MemoryMarshal

We can use `MemoryMarshal` to reinterpret the string's char array as bytes (an _O_(1) operation) rather than encode it.

However it can (depending on the input) double the time to hash the string as we'll get (_length_ * 2) bytes.

```cs
static string GetSha256(this string s)
{
    var hash = (stackalloc byte[32]);
    SHA256.HashData(MemoryMarshal.AsBytes(s.AsSpan()), hash);
    return Convert.ToHexString(hash);
}
```

### UTF-8 encoding with ArrayPool

UTF-8 encoding can produce a lower byte count for many strings, which would make the hash function faster. We'll use `MemoryRental` again to `stackalloc` or rent an array.

```cs
const int StackallocThreshold = 256;

static string GetSha256(this string s)
{
    int inputByteCount = Encoding.UTF8.GetByteCount(s);
    using MemoryRental<char> encodedBytes = length <= StackallocThreshold ? new(stackalloc char[StackallocThreshold]) : new(length);
    int encodedByteCount = Encoding.UTF8.GetBytes(s, encodedBytes);
    var hash = (stackalloc byte[32]);
    SHA256.HashData(encodedBytes[..encodedByteCount], hash);
    return Convert.ToHexString(hash);
}
```
