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
    internal (bool Success, string Message) ResetOnboarding(bool showImmediately = true)
    {
        if (exiting || dataTransition) return (false, "当前正在切换或退出，未重置新手指引。");
        if (onboardingDevice is not null)
        {
            var (ok, error) = onboardingDevice.Reset();
            if (!ok) return (false, error);
        }
        if (showImmediately) ShowOnboarding();
        return onboardingDevice is not null
            ? (true, "已重置本机新手指引状态（不影响任务与设置），下次启动也会再次自动引导。")
            : (true, "已打开新手指引预览（隔离测试模式不写入本机首次使用记录）。");
    }
    internal void OpenOnboardingSettings(string section)
    { Settings(); settings!.SelectSection(section); onboarding?.Activate(); }
}
