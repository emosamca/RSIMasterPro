namespace RSIMasterpro.Indicators;

// Wilder's smoothed moving average. Pine: ta.rma(source, length).
// Seed value is SMA of first `length` non-NaN samples; subsequent values use alpha = 1/length.
public class RmaIndicator
{
    private readonly int _length;
    public string Name => $"RMA({_length})";

    public RmaIndicator(int length)
    {
        if (length <= 0) throw new ArgumentException("Length must be positive.", nameof(length));
        _length = length;
    }

    public double[] Calculate(double[] source)
    {
        int n = source.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = double.NaN;

        int firstValid = -1;
        for (int i = 0; i < n; i++)
        {
            if (!double.IsNaN(source[i])) { firstValid = i; break; }
        }
        if (firstValid < 0) return result;

        int seedEnd = firstValid + _length - 1;
        if (seedEnd >= n) return result;

        double sum = 0;
        for (int i = firstValid; i <= seedEnd; i++) sum += source[i];
        result[seedEnd] = sum / _length;

        double alpha = 1.0 / _length;
        for (int i = seedEnd + 1; i < n; i++)
            result[i] = alpha * source[i] + (1.0 - alpha) * result[i - 1];

        return result;
    }
}
