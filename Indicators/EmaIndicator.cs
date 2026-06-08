namespace RSIMasterpro.Indicators;

// Exponential Moving Average. Pine: ta.ema(source, length).
// Seeded with SMA of first `length` samples, then alpha = 2/(length+1).
public class EmaIndicator
{
    private readonly int _length;
    public string Name => $"EMA({_length})";

    public EmaIndicator(int length)
    {
        if (length <= 0) throw new ArgumentException("Length must be positive.", nameof(length));
        _length = length;
    }

    public double[] Calculate(double[] source)
    {
        int n = source.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = double.NaN;

        if (n < _length) return result;

        double sum = 0;
        for (int i = 0; i < _length; i++) sum += source[i];
        result[_length - 1] = sum / _length;

        double alpha = 2.0 / (_length + 1);
        for (int i = _length; i < n; i++)
            result[i] = alpha * source[i] + (1.0 - alpha) * result[i - 1];

        return result;
    }
}
