namespace RSIMasterpro.Indicators;

// Relative Strength Index. Pine: ta.rsi(source, length).
// Uses Wilder's smoothing (RMA) on gains and losses.
public class RsiIndicator
{
    private readonly int _length;
    public string Name => $"RSI({_length})";

    public RsiIndicator(int length)
    {
        if (length <= 0) throw new ArgumentException("Length must be positive.", nameof(length));
        _length = length;
    }

    public double[] Calculate(double[] source)
    {
        int n = source.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = double.NaN;

        if (n < _length + 1) return result;

        var gain = new double[n];
        var loss = new double[n];
        for (int i = 1; i < n; i++)
        {
            double change = source[i] - source[i - 1];
            gain[i] = Math.Max(change, 0);
            loss[i] = Math.Max(-change, 0);
        }

        // Pine ta.rma seeds at the `length`-th non-NaN value; gain/loss are valid from i=1.
        double sumG = 0, sumL = 0;
        for (int i = 1; i <= _length; i++)
        {
            sumG += gain[i];
            sumL += loss[i];
        }

        var avgGain = new double[n];
        var avgLoss = new double[n];
        for (int i = 0; i < n; i++) { avgGain[i] = double.NaN; avgLoss[i] = double.NaN; }
        avgGain[_length] = sumG / _length;
        avgLoss[_length] = sumL / _length;

        double alpha = 1.0 / _length;
        for (int i = _length + 1; i < n; i++)
        {
            avgGain[i] = alpha * gain[i] + (1.0 - alpha) * avgGain[i - 1];
            avgLoss[i] = alpha * loss[i] + (1.0 - alpha) * avgLoss[i - 1];
        }

        for (int i = _length; i < n; i++)
        {
            if (avgLoss[i] == 0)
            {
                result[i] = 100.0;
            }
            else
            {
                double rs = avgGain[i] / avgLoss[i];
                result[i] = 100.0 - 100.0 / (1.0 + rs);
            }
        }
        return result;
    }
}
