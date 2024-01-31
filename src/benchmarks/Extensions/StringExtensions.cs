using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DotNext.Buffers;

public static class StringExtensions
{
    private const int ByteStackallocThreshold = 256;

    public static string Reverse(this string s) =>
        string.Create(s.Length, s, static (span, str) =>
        {
            str.AsSpan().CopyTo(span);
            span.Reverse();
        });

#pragma warning disable CS8500
    public unsafe static string ReplaceNonAlphanumeric(this ReadOnlySpan<char> s, char replacement)
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

    [SkipLocalsInit]
    public static string GetSha256(this ReadOnlySpan<char> s)
    {
        int inputByteCount = Encoding.UTF8.GetByteCount(s);
        using SpanOwner<byte> encodedBytes = inputByteCount <= ByteStackallocThreshold ? new(stackalloc byte[ByteStackallocThreshold], inputByteCount) : new(inputByteCount);
        int encodedByteCount = Encoding.UTF8.GetBytes(s, encodedBytes.Span);
        var hash = (stackalloc byte[32]);
        SHA256.HashData(encodedBytes.Span[..encodedByteCount], hash);
        return Convert.ToHexString(hash);
    }
}
