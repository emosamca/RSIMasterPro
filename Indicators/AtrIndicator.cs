namespace RSIMasterpro.Indicators;

// Average True Range. Pine: ta.atr(length) = ta.rma(ta.tr, length).
// TR at i=0 is high-low (close[-1] is na in Pine).
public class AtrIndicator
{
    private readonly int _length;
    public string Name => $"ATR({_length})";

    public AtrIndicator(int length)
    {
        if (length <= 0) throw new ArgumentException("Length must be positive.", nameof(length));
        _length = length;
    }

    public double[] Calculate(double[] high, double[] low, double[] close)
    {
        int n = high.Length;
        if (low.Length != n || close.Length != n)
            throw new ArgumentException("high/low/close arrays must have equal length.");

        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = double.NaN;
        if (n == 0) return result;

        var tr = new double[n];
        tr[0] = high[0] - low[0];
        for (int i = 1; i < n; i++)
        {
            double a = high[i] - low[i];
            double b = Math.Abs(high[i] - close[i - 1]);
            double c = Math.Abs(low[i] - close[i - 1]);
            tr[i] = Math.Max(a, Math.Max(b, c));
        }

        if (n < _length) return result;

        double sum = 0;
        for (int i = 0; i < _length; i++) sum += tr[i];
        result[_length - 1] = sum / _length;

        double alpha = 1.0 / _length;
        for (int i = _length; i < n; i++)
            result[i] = alpha * tr[i] + (1.0 - alpha) * result[i - 1];

        return result;
    }
}
