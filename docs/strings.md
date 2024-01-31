# Strings

## Slicing strings

Before `Span`, we used `String.Substring` to get a part of a string, which allocates a new string and copies the characters to it.

Now we can just slice the `Span` using the range operator, an $O(1)$ operation. We can then pass this `Span` to a method that accepts a `ReadOnlySpan<char>` parameter, such as `Int32.Parse`. See [Basics](basics.md) for a partial list of `Span`-aware APIs.

### Example: Parsing a substring

```cs
int.Parse("12345".AsSpan()[1..3]) // results in 23
```

### Example: Concatenating strings


```cs
const string text = "abcdefghijklmnopq";

string ConcatSubstring() => text[10..] + "---" + text[..5];
string ConcatAsSpan() => string.Concat(text.AsSpan()[10..], "---", text.AsSpan()[..5]);
```

BenchmarkDotnet results - note the reduction in allocated memory:

|       Method |     Mean |    Error |   StdDev |  Gen 0 | Allocated |
|------------- |---------:|---------:|---------:|-------:|----------:|
|    Substring | 30.28 ns | 0.633 ns | 0.621 ns | 0.0306 |     128 B |
|       AsSpan | 17.30 ns | 0.377 ns | 0.476 ns | 0.0134 |      56 B |

## Creating strings of a known size using `String.Create`

When creating a new string whose size is known in advance, we can use the `String.Create` method to avoid additional allocations. This method works by allocating a string and providing a writable `Span` within a delegate. It's safe (meaning the string can't be mutated after creation) since the `Span` can't escape from the delegate.

> [!IMPORTANT]
> Avoid capturing variables in the delegate as they incur an allocation - pass all data using the `state` parameter. Use the `static` keyword to enforce this.

### Example: Reversing a string

```cs
static string Reverse(this string s) =>
    string.Create(s.Length, s, static (span, str) =>
    {
        str.AsSpan().CopyTo(span);
        span.Reverse();
    });
```

### Example: Creating a random string

> [!TIP]
> Can be written using `RandomNumberGenerator.GetString(size, "abcdefghijklmnopqrstuvwxyz")`, which is also cryptographically secure.

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

`ref struct`s such as `Span` can't be used in generic type parameters (since there's currently no way to prevent boxing), so if we want to base one string on another, we'll have to use `unsafe` code - a pointer - in order to pass it into the generic `SpanAction<T, TArg>` delegate.

> [!IMPORTANT]
> Be careful when using unsafe code - it could easily lead to memory corruption. Extensively cover it with tests.

```cs
#pragma warning disable CS8500
unsafe static string ReplaceNonAlphanumeric(this ReadOnlySpan<char> s, char replacement)
{
    return string.Create(s.Length, (replacement, spanPtr: (IntPtr)(&s)), static (span, state) =>
    {
        var sourceSpan = *(ReadOnlySpan<char>*)state.spanPtr;
        for (int i = 0; i < span.Length; i++)
        {
            var c = sourceSpan[i];
            span[i] = char.IsLetterOrDigit(c) ? c : state.replacement;
        }
    });
}
#pragma warning restore CS8500
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
string.Create(length: 3, " a^b ", static (span, str) =>
    str.AsSpan().Trim()[..span.Length].ReplaceNonAlphanumeric('_', span)); // results in "a_b"
```

## Interpolated string handlers

Interpolated string handlers are a C# feature that optimizes string creation by breaking them into multiple "append" calls, rather than allocating a string using `String.Format`, and potentially creating multiple interim strings (for example, formatting a number). This allows us to "hack" the compiler into all sorts of interesting optimizations.

Handlers available in .NET:

* `DefaultInterpolatedStringHandler` is used in a `String.Create` overload, and emitted by the compiler for regular interpolated strings.
* `AppendInterpolatedStringHandler` is used in `StringBuilder.Append`. No longer is it needed to break an interpolated string into multiple `Append` calls for efficiency.
* `AssertInterpolatedStringHandler` and `WriteIfInterpolatedStringHandler` are used in `Debug.Assert` and `Debug.WriteIf` respectively and completely skip writing according to the condition argument.
* `TryWriteInterpolatedStringHandler` is used by `MemoryExtensions.TryWrite` and allows us to efficiently write strings into a `Span<char>`. There's a similar one in `Utf8.TryWrite` to write into a `Span<byte>`.

dotNext also includes:
* `BufferWriterInterpolatedStringHandler` that writes into an `IBufferWriter<char>`.
* `PoolingInterpolatedStringHandler` which allows using a `Memory<T>` pool rather than allocating new buffers.

> [!TIP]
> We can reuse any of the above handlers to write custom ones.

### Example: `TryWrite`

The following method checks if the host & port part of a `Uri` matches a list of patterns. Rather than using `Uri.GetLeftPart` which allocates a string, we'll use `TryWrite`. Because it uses an interpolated string handler, there are no additional allocations (for example, to convert the port number into a string). `Regex` also works directly with `Span<char>`.

```cs
[SkipLocalsInit]
bool MatchesUriPattern(IEnumerable<Regex> patterns, Uri uri)
{
    var maxLength = uri.Scheme.Length + "://".Length + uri.Host.Length + ":".Length + 5;
    using SpanOwner<char> hostAndPort = maxLength <= 256 ? new(stackalloc char[256], maxLength) : new(maxLength);
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

### Example: Using substrings in interpolated strings

Interpolated string handlers have overloads for `ReadOnlySpan<char>`, which allows us to get substrings without allocating new strings.

> [!TIP]
> Consider `String.Create` and `String.Concat` before using an interpolated string, as they would be more efficient.

```cs
string Format(string str, int num)
{
    var index = str.IndexOf(':');
    return $"{str.AsSpan()[..index]} {num} {str.AsSpan()[(index + 1)..]}";
}
```

### Example: Using `DefaultInterpolatedStringHandler` instead of `StringBuilder`

`StringBuilder` is a reference type and often in order to avoid allocating new ones, we pool them, or store them in reusable thread-static fields. As an alternative, we can use `DefaultInterpolatedStringHandler` as an **append-only** value type string builder.

> [!TIP]
> `StringBuilder` works more like a linked list of arrays while `DefaultInterpolatedStringHandler` works more like a dynamic array. This means that `StringBuilder` might perform better when we can't approximate the final size of the string.

> [!TIP]
> When possible, it's preferable to use `stackalloc` to provide the initial buffer as it performs better. If we don't provide an initial buffer, it uses a rented array with a size calculated from the parameters `literalLength` and `formattedCount`.

> [!IMPORTANT]
> The handler uses `ArrayPool<char>` internally when it grows out of the initial buffer, so we must call `ToStringAndClear` to return the rented array rather than `ToString`.

```cs
string BuildString(int count)
{
    var initialBuffer = (stackalloc char[256]);
    var builder = new DefaultInterpolatedStringHandler(0, 0, CultureInfo.InvariantCulture, initialBuffer);
    builder.AppendLiteral("hello");
    for (int i = 0; i < count; i++)
    {
        builder.AppendLiteral(", ");
        builder.AppendFormatted(i);
    }
    
    return builder.ToStringAndClear();
}
```

## Hashing a string

To get a hexadecimal string representing the hash of a string, we'll use the `SHA256` algorithm (it could be replaced with others available in .NET).

> [!IMPORTANT]
> The methods below will produce different hashes.

### Using `MemoryMarshal`

We can use `MemoryMarshal` to reinterpret the string's char array as bytes (an $O(1)$ operation) rather than encode it.

However it can (depending on the input) double the time to hash the string as we'll get $length * 2$ bytes.

```cs
static string GetSha256(this string s)
{
    var hash = (stackalloc byte[32]);
    SHA256.HashData(MemoryMarshal.AsBytes(s.AsSpan()), hash);
    return Convert.ToHexString(hash);
}
```

### UTF-8 encoding with `ArrayPool`

UTF-8 encoding can produce a lower byte count for many strings, which would make the hash function faster. We'll use `SpanOwner` again to `stackalloc` or rent an array.

```cs
const int StackallocThreshold = 256;

[SkipLocalsInit]
static string GetSha256(this string s)
{
    int inputByteCount = Encoding.UTF8.GetByteCount(s);
    using SpanOwner<byte> encodedBytes = inputByteCount <= StackallocThreshold ? new(stackalloc byte[StackallocThreshold], inputByteCount) : new(inputByteCount);
    int encodedByteCount = Encoding.UTF8.GetBytes(s, encodedBytes.Span);
    var hash = (stackalloc byte[32]);
    SHA256.HashData(encodedBytes.Span[..encodedByteCount], hash);
    return Convert.ToHexString(hash);
}
```

## Splitting strings

`MemoryExtensions` methods `Split` and `SplitAny` allow us to split strings with no allocations. Unlike `String.Split`, it writes the results to a `Span<Range>`, which means we have to pre-allocate the ranges. If there are more matches than the ranges provide, the last range will contain the remainder of the string.

> [!TIP]
> We're using collection expressions to create a `ReadOnlySpan<string>` of separators (of course, it can also be allocated statically once).

```cs
var stringToSplit = ";;11==22";
var spanToSplit = stringToSplit.AsSpan();
var ranges = (stackalloc Range[2]);
ReadOnlySpan<string> separators = [ "==", ";;" ];
var count = spanToSplit.SplitAny(ranges, separators, StringSplitOptions.RemoveEmptyEntries);
Debug.Assert(count == 2);
var result = (int.Parse(spanToSplit[ranges[0]]), int.Parse(spanToSplit[ranges[1]])); // results in (11, 22)
```

## Optimized value searches using `StringValues`

Methods like `ContainsAny` and `IndexOfAny` can benefit from various optimizations, depending on the characters searched. For example, if the value contain only ASCII characters, or whether it's up to 5 characters, or represents a contiguous range (e.g. a-z). Determining the optimization is done once when `StringValues` is created.

Many .NET classes, such as JSON parsing, regular expressions and `Uri` have been enhanced with it.

```cs
private static readonly SearchValues<char> s_lineEndings = SearchValues.Create("\n\r\f\u0085\u2028\u2029");

int CountLineEndings(ReadOnlySpan<char> s)
{
    int count = 0;

    int pos;
    while ((pos = s.IndexOfAny(s_lineEndings)) >= 0)
    {
        count++;
        s = s.Slice(pos + 1);
    }

    return count;
}
```
