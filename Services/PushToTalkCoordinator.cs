namespace Egoist.Voice.Services;

internal enum PushToTalkSource
{
    Keyboard,
    Mouse
}

internal sealed class PushToTalkCoordinator
{
    private readonly HashSet<PushToTalkSource> _activeSources = [];

    internal bool Press(PushToTalkSource source) =>
        _activeSources.Add(source) && _activeSources.Count == 1;

    internal bool Release(PushToTalkSource source)
    {
        if (!_activeSources.Remove(source))
        {
            return false;
        }

        return _activeSources.Count == 0;
    }

    internal void Reset() => _activeSources.Clear();
}
