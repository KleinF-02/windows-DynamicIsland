using System.Windows.Media.Animation;

namespace DynamicIsland.App.Animations;

/// <summary>
/// Damped harmonic oscillator used as a WPF easing function. It gives the
/// window bounds a short spring overshoot without a separate physics loop.
/// </summary>
public sealed class SpringEasingFunction : EasingFunctionBase
{
    public double Damping { get; set; } = 0.9;

    public double Frequency { get; set; } = 1.5;

    protected override System.Windows.Freezable CreateInstanceCore()
        => new SpringEasingFunction();

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

        var damping = Math.Clamp(Damping, 0.15, 2.0);
        var omega = 2.0 * Math.PI * Math.Clamp(Frequency, 0.2, 4.0);

        if (damping >= 0.999)
        {
            return 1.0 - (Math.Exp(-omega * normalizedTime) * (1.0 + (omega * normalizedTime)));
        }

        var dampedOmega = omega * Math.Sqrt(1.0 - (damping * damping));
        var value = 1.0 - (Math.Exp(-damping * omega * normalizedTime)
                           * (Math.Cos(dampedOmega * normalizedTime)
                              + ((damping * omega / dampedOmega) * Math.Sin(dampedOmega * normalizedTime))));

        return normalizedTime >= 0.995 ? 1.0 : value;
    }
}
