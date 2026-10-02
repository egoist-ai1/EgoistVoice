namespace Egoist.Voice.Controls;

/// <summary>Budgets meter work independently of the display refresh rate.</summary>
internal sealed class CapsuleAnimationCadence
{
    private bool _started;
    private bool _reducedMotion;
    private TimeSpan _lastFrame;
    private TimeSpan _nextFrame;

    internal void Reset() => _started = false;

    internal bool TryAdvance(TimeSpan renderingTime, bool reducedMotion, out double deltaSeconds)
    {
        var interval = TimeSpan.FromSeconds(reducedMotion ? 0.1 : 1d / 60d);
        deltaSeconds = 0;
        if (!_started || _reducedMotion != reducedMotion || renderingTime < _lastFrame)
        {
            _started = true;
            _reducedMotion = reducedMotion;
            _lastFrame = renderingTime;
            _nextFrame = renderingTime + interval;
            deltaSeconds = interval.TotalSeconds;
            return true;
        }
        if (renderingTime < _nextFrame) return false;

        deltaSeconds = Math.Clamp((renderingTime - _lastFrame).TotalSeconds, 1d / 240d,
            reducedMotion ? 0.2 : 0.05);
        _lastFrame = renderingTime;
        // Keep the phase on 144 Hz displays; setting next = now + interval would fall to 48 fps.
        // After a pause skip missed frames in one step instead of producing a catch-up burst.
        var skippedIntervals = (renderingTime - _nextFrame).Ticks / interval.Ticks + 1;
        _nextFrame += TimeSpan.FromTicks(skippedIntervals * interval.Ticks);
        return true;
    }
}

/// <summary>Owns one render-event subscription; hiding suspends a request, stopping cancels it.</summary>
internal sealed class CapsuleAnimationSubscription(Action attach, Action detach)
{
    internal bool IsRequested { get; private set; }
    internal bool IsAttached { get; private set; }

    internal void Start(bool eligible)
    {
        IsRequested = true;
        Refresh(eligible);
    }

    internal void Refresh(bool eligible)
    {
        var shouldAttach = IsRequested && eligible;
        if (shouldAttach == IsAttached) return;
        if (shouldAttach) attach();
        else detach();
        IsAttached = shouldAttach;
    }

    internal void Stop()
    {
        IsRequested = false;
        Refresh(eligible: false);
    }
}
