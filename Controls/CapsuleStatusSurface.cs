using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Egoist.Voice.Controls;

/// <summary>A non-focusable surface with a real peer for polite state announcements.</summary>
public sealed class CapsuleStatusSurface : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new StatusSurfacePeer(this);

    private sealed class StatusSurfacePeer(CapsuleStatusSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(CapsuleStatusSurface);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }
}
