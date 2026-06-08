namespace RSIMasterpro.Indicators;

// Directional Movement Index. Pine: [diplus, diminus, adx] = ta.dmi(diLength, adxSmoothing).
public class DmiIndicator
{
    private readonly int _diLength;
    private readonly int _adxSmoothing;
    public string Name => $"DMI({_diLength},{_adxSmoothing})";

    public DmiIndicator(int diLength, int adxSmoothing)
    {
        if (diLength <= 0) throw new ArgumentException("diLength must be positive.", nameof(diLength));
        if (adxSmoothing <= 0) throw new ArgumentException("adxSmoothing must be positive.", nameof(adxSmoothing));
        _diLength = diLength;
        _adxSmoothing = adxSmoothing;
    }

    public (double[] PlusDi, double[] MinusDi, double[] Adx) Calculate(double[] high, double[] low, double[] close)
    {
        int n = high.Length;
        if (low.Length != n || close.Length != n)
            throw new ArgumentException("high/low/close arrays must have equal length.");

        var plusDM = new double[n];
        var minusDM = new double[n];
        var tr = new double[n];

        plusDM[0] = 0;
        minusDM[0] = 0;
        tr[0] = high[0] - low[0];

        for (int i = 1; i < n; i++)
        {
            double up = high[i] - high[i - 1];
            double down = low[i - 1] - low[i];
            plusDM[i] = (up > down && up > 0) ? up : 0;
            minusDM[i] = (down > up && down > 0) ? down : 0;

            double a = high[i] - low[i];
            double b = Math.Abs(high[i] - close[i - 1]);
            double c = Math.Abs(low[i] - close[i - 1]);
            tr[i] = Math.Max(a, Math.Max(b, c));
        }

        // tr is valid from i=0; plusDM/minusDM are na at i=0 in Pine (we use 0 here, but seed from i=1).
        var smTr = WildersSmoothing(tr, _diLength, startIndex: 0);
        var smPlusDM = WildersSmoothing(plusDM, _diLength, startIndex: 1);
        var smMinusDM = WildersSmoothing(minusDM, _diLength, startIndex: 1);

        var plusDi = new double[n];
        var minusDi = new double[n];
        var dx = new double[n];

        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(smTr[i]) || smTr[i] == 0 ||
                double.IsNaN(smPlusDM[i]) || double.IsNaN(smMinusDM[i]))
            {
                plusDi[i] = double.NaN;
                minusDi[i] = double.NaN;
                dx[i] = double.NaN;
                continue;
            }

            plusDi[i] = 100.0 * smPlusDM[i] / smTr[i];
            minusDi[i] = 100.0 * smMinusDM[i] / smTr[i];

            double sum = plusDi[i] + minusDi[i];
            dx[i] = sum == 0 ? 0 : Math.Abs(plusDi[i] - minusDi[i]) / sum * 100.0;
        }

        var adx = WildersSmoothingFromFirstValid(dx, _adxSmoothing);
        return (plusDi, minusDi, adx);
    }

    private static double[] WildersSmoothing(double[] source, int length, int startIndex)
    {
        int n = source.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = double.NaN;

        int seedEnd = startIndex + length - 1;
        if (seedEnd >= n) return result;

        double sum = 0;
        for (int i = startIndex; i <= seedEnd; i++) sum += source[i];
        result[seedEnd] = sum / length;

        double alpha = 1.0 / length;
        for (int i = seedEnd + 1; i < n; i++)
            result[i] = alpha * source[i] + (1.0 - alpha) * result[i - 1];

        return result;
    }

    private static double[] WildersSmoothingFromFirstValid(double[] source, int length)
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

        int seedEnd = firstValid + length - 1;
        if (seedEnd >= n) return result;

        double sum = 0;
        for (int i = firstValid; i <= seedEnd; i++) sum += source[i];
        result[seedEnd] = sum / length;

        double alpha = 1.0 / length;
        for (int i = seedEnd + 1; i < n; i++)
            result[i] = alpha * source[i] + (1.0 - alpha) * result[i - 1];

        return result;
    }
}
