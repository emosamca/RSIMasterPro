namespace RSIMasterpro.Indicators;

// Custom composite indicator from RSI Master Pro pine script:
//   div       = rsi(src, fast) - rsi(src, slow)
//   div_accel = div - div[1]
public class DivergenceIndicator
{
    private readonly RsiIndicator _fast;
    private readonly RsiIndicator _slow;
    public string Name { get; }

    public DivergenceIndicator(int fastLength, int slowLength)
    {
        _fast = new RsiIndicator(fastLength);
        _slow = new RsiIndicator(slowLength);
        Name = $"Divergence({fastLength},{slowLength})";
    }

    public (double[] Div, double[] DivAccel) Calculate(double[] source)
    {
        var fast = _fast.Calculate(source);
        var slow = _slow.Calculate(source);
        int n = source.Length;

        var div = new double[n];
        var accel = new double[n];

        for (int i = 0; i < n; i++)
        {
            div[i] = (double.IsNaN(fast[i]) || double.IsNaN(slow[i]))
                ? double.NaN
                : fast[i] - slow[i];
        }

        accel[0] = double.NaN;
        for (int i = 1; i < n; i++)
        {
            accel[i] = (double.IsNaN(div[i]) || double.IsNaN(div[i - 1]))
                ? double.NaN
                : div[i] - div[i - 1];
        }

        return (div, accel);
    }
}
