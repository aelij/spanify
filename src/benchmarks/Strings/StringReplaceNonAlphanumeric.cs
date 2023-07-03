using System.Text;

[MemoryDiagnoser]
public class StringReplaceNonAlphanumeric
{
    const string text = "a!b@c#d$e%f^g&h*i(j)k-l_m+n=";
    const char replacement = '_';

    [Benchmark]
    public string ReplaceStringBuilder()
    {
        var sb = new StringBuilder(text);
        for (int i = 0; i < sb.Length; i++)
        {
            if (!char.IsLetterOrDigit(sb[i]))
            {
                sb[i] = replacement;
            }
        }

        return sb.ToString();
    }

    [Benchmark(Baseline = true)]
    public unsafe string ReplaceSpan() => text.AsSpan().ReplaceNonAlphanumeric(replacement); // from StringExtensions
}
