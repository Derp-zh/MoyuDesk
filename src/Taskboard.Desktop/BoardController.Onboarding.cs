using System;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private readonly DeviceOnboarding? onboardingDevice;
    private readonly bool onboardingLegacy;
    private OnboardingSurface? onboarding;
    internal OnboardingSurface? OnboardingForVerification => onboarding;
    internal void ShowOnboarding()
    {
        if (exiting || dataTransition) return;
        if (onboarding is { IsVisible: true }) { onboarding.Activate(); return; }
        onboarding?.Destroy(false);
        var window = new OnboardingSurface(this) { SuppressActivation = Smoke };
        onboarding = window;
        window.Closed += (_, _) => { if (ReferenceEquals(onboarding, window)) onboarding = null; };
        RegisterAuxiliary(window, positionOnOpen: false); Clamp(window);
        if (!Smoke)
        {
            var area = WorkArea(Ball); window.Left = Math.Max(area.Left, area.Right - window.Width - 20); window.Top = area.Top + 24; Clamp(window);
            window.Show(); window.Activate();
        }
    }
    private void BeginFirstUseGuide(SurfaceWindow? initializationPrompt, bool show)
    {
        if (!show) return;
        if (initializationPrompt is { IsVisible: true }) initializationPrompt.Closed += (_, _) => ShowOnboarding();
        else ShowOnboarding();
    }
    internal void OpenOnboardingSettings(string section)
    { Settings(); settings!.SelectSection(section); onboarding?.Activate(); }
}
