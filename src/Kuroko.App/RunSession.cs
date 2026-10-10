using Kuroko.App.Overlay;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.App;

/// <summary>
/// One call, from the hotkey to the result: the window under the hotkey, the reading of its focused element (started at
/// once, awaited later) and where the pills go.
/// </summary>
internal sealed record RunSession(TargetInfo? Target, Task<FocusProbe> Probe, Anchor Anchor);
