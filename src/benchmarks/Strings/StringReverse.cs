[MemoryDiagnoser]
public class StringReverse
{
    const string text = "abcdefghijklmnopqrstuvwxyz";

    [Benchmark]
    public string ReverseNaive() => new([.. text.Reverse()]);

    [Benchmark(Baseline = true)]
    public string ReverseStringCreate() => text.ReverseString();
}
