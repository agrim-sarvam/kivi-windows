// The kiwi's GAIT model while listening — a direct port of packages/orb-core/src/speechPace.ts
// (Orb/Core/SpeechPace.swift). Pure and deterministic: feed (level, dt), read Pace (0…1).
// Drives ANIMATION only (never take fate).
using System;

namespace Kivi.Core.Orb;

public sealed class SpeechPace
{
    public const double OnLevel = 0.3;
    public const double OffLevel = 0.12;
    public const double OnConfirm = 0.1;
    public const double SilenceHold = 0.9;
    public const double RiseTau = 0.28;
    public const double FallTau = 0.85;

    public bool Speaking = false;
    public double Pace = 0;
    private double _aboveFor = 0;
    private double _quietFor = 0;

    public void Feed(double level, double dt)
    {
        if (level >= OnLevel)
        {
            _aboveFor += dt;
            _quietFor = 0;
            if (!Speaking && _aboveFor >= OnConfirm) Speaking = true;
        }
        else
        {
            _aboveFor = 0;
            if (level <= OffLevel)
            {
                _quietFor += dt;
                if (Speaking && _quietFor >= SilenceHold) Speaking = false;
            }
            else
            {
                _quietFor = 0;
            }
        }
        double target = Speaking ? 1 : 0;
        double tau = target > Pace ? RiseTau : FallTau;
        Pace += (target - Pace) * Math.Min(1, dt / tau);
        if (Math.Abs(target - Pace) < 0.001) Pace = target;
    }

    public void Reset()
    {
        Speaking = false;
        Pace = 0;
        _aboveFor = 0;
        _quietFor = 0;
    }

    public double Eased => Pace * Pace * (3 - 2 * Pace);
}
