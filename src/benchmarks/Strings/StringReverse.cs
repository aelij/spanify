[MemoryDiagnoser]
public class StringReverse
{
    const string text = "abcdefghijklmnopqrstuvwxyz";

    [Benchmark]
    public string ReverseNaive() => new(text.Reverse().ToArray());

    [Benchmark(Baseline = true)]
    public string ReverseStringCreate() => text.Reverse(); // from StringExtensions
}
