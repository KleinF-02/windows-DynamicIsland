using System.Windows.Media.Animation;

namespace DynamicIsland.App.Animations;

/// <summary>
/// A cubic-bezier easing function matching fly-island's
/// <c>cubic-bezier(0.16, 1, 0.3, 1)</c> transition curve.
/// </summary>
public sealed class CubicBezierEasingFunction : EasingFunctionBase
{
    public double X1 { get; set; } = 0.16;

    public double Y1 { get; set; } = 1.0;

    public double X2 { get; set; } = 0.3;

    public double Y2 { get; set; } = 1.0;

    protected override System.Windows.Freezable CreateInstanceCore()
        => new CubicBezierEasingFunction();

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0)
        {
            return 0;
        }

        if (normalizedTime >= 1)
        {
            return 1;
        }

        var x1 = Math.Clamp(X1, 0, 1);
        var x2 = Math.Clamp(X2, 0, 1);
        var y1 = Math.Clamp(Y1, -2, 3);
        var y2 = Math.Clamp(Y2, -2, 3);

        var low = 0.0;
        var high = 1.0;
        var parameter = normalizedTime;

        // Solve x(parameter) = normalizedTime, then evaluate y(parameter).
        for (var i = 0; i < 20; i++)
        {
            parameter = (low + high) / 2.0;
            var x = Bezier(parameter, x1, x2);
            if (x < normalizedTime)
            {
                low = parameter;
            }
            else
            {
                high = parameter;
            }
        }

        return Bezier(parameter, y1, y2);
    }

    private static double Bezier(double t, double p1, double p2)
    {
        var inverse = 1.0 - t;
        return (3.0 * inverse * inverse * t * p1)
               + (3.0 * inverse * t * t * p2)
               + (t * t * t);
    }
}
