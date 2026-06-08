namespace RSIMasterpro.Indicators;

// Simple Moving Average. Pine: ta.sma(source, length).
public class SmaIndicator
{
    private readonly int _length;
    public string Name => $"SMA({_length})";

    public SmaIndicator(int length)
    {
        if (length <= 0) throw new ArgumentException("Length must be positive.", nameof(length));
        _length = length;
    }

    public double[] Calculate(double[] source)
    {
        int n = source.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i < _length - 1)
            {
                result[i] = double.NaN;
                continue;
            }
            double sum = 0;
            for (int j = i - _length + 1; j <= i; j++) sum += source[j];
            result[i] = sum / _length;
        }
        return result;
    }
}
