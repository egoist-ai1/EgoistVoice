using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace Egoist.Voice.Services;

public enum EffectiveAppTheme
{
    Light,
    Dark,
    HighContrast
}

public sealed record AppThemeChangedEventArgs(
    AppTheme Preference,
    EffectiveAppTheme EffectiveTheme,
    bool ReducedMotion);

/// <summary>Applies one semantic WPF palette and follows Windows when requested.</summary>
public sealed class AppThemeService : IDisposable
{
    private ResourceDictionary? _activeDictionary;
    private bool _disposed;

    public AppThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler<AppThemeChangedEventArgs>? ThemeChanged;

    public AppTheme Preference { get; private set; } = AppTheme.System;

    public EffectiveAppTheme EffectiveTheme { get; private set; } = EffectiveAppTheme.Dark;

    public bool ReducedMotion { get; private set; }

    public void Apply(AppTheme preference)
    {
        Apply(preference, forceResourceRefresh: false);
    }

    private void Apply(AppTheme preference, bool forceResourceRefresh)
    {
        if (_disposed || WpfApplication.Current is null)
        {
            return;
        }

        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            _ = WpfApplication.Current.Dispatcher.BeginInvoke(() => Apply(preference, forceResourceRefresh));
            return;
        }

        var normalizedPreference = Enum.IsDefined(preference) ? preference : AppTheme.System;
        var preferenceChanged = normalizedPreference != Preference;
        Preference = normalizedPreference;
        var highContrast = SystemParameters.HighContrast;
        var effective = Resolve(Preference, SystemUsesLightApps(), highContrast);
        var reducedMotion = !SystemParameters.ClientAreaAnimation || highContrast;
        var presentationChanged = effective != EffectiveTheme ||
                                  reducedMotion != ReducedMotion ||
                                  _activeDictionary is null ||
                                  forceResourceRefresh;
        EffectiveTheme = effective;
        ReducedMotion = reducedMotion;

        if (!presentationChanged && !preferenceChanged)
        {
            return;
        }

        if (presentationChanged)
        {
            InstallResources(effective, reducedMotion);
        }

        // Preference-only changes still matter to the tray and Settings checkmarks. For example,
        // System and explicit Dark can currently resolve to the same palette while expressing a
        // different future-following policy.
        ThemeChanged?.Invoke(this, new AppThemeChangedEventArgs(Preference, effective, reducedMotion));
    }

    internal void ApplyDiagnostic(EffectiveAppTheme effectiveTheme, bool reducedMotion)
    {
        Preference = effectiveTheme == EffectiveAppTheme.Light ? AppTheme.Light : AppTheme.Dark;
        EffectiveTheme = effectiveTheme;
        ReducedMotion = reducedMotion || effectiveTheme == EffectiveAppTheme.HighContrast;
        InstallResources(EffectiveTheme, ReducedMotion);
        ThemeChanged?.Invoke(this, new AppThemeChangedEventArgs(Preference, EffectiveTheme, ReducedMotion));
    }

    private void InstallResources(EffectiveAppTheme effective, bool reducedMotion)
    {
        var fileName = effective switch
        {
            EffectiveAppTheme.Light => "Light.xaml",
            EffectiveAppTheme.HighContrast => "HighContrast.xaml",
            _ => "Dark.xaml"
        };
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Themes/{fileName}", UriKind.Relative)
        };
        var merged = WpfApplication.Current.Resources.MergedDictionaries;
        if (_activeDictionary is not null)
        {
            merged.Remove(_activeDictionary);
        }
        else
        {
            var initial = merged.FirstOrDefault(candidate =>
                candidate.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
            if (initial is not null)
            {
                merged.Remove(initial);
            }
        }
        merged.Insert(0, dictionary);
        _activeDictionary = dictionary;
        WpfApplication.Current.Resources["AppPopupAnimation"] = reducedMotion
            ? PopupAnimation.None
            : PopupAnimation.Fade;
    }

    internal static EffectiveAppTheme Resolve(AppTheme preference, bool systemUsesLightApps, bool highContrast) =>
        highContrast
            ? EffectiveAppTheme.HighContrast
            : preference switch
            {
                AppTheme.Light => EffectiveAppTheme.Light,
                AppTheme.Dark => EffectiveAppTheme.Dark,
                _ => systemUsesLightApps ? EffectiveAppTheme.Light : EffectiveAppTheme.Dark
            };

    private static bool SystemUsesLightApps()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        var paletteMayHaveChanged = args.Category is
            UserPreferenceCategory.Accessibility or
            UserPreferenceCategory.Color or
            UserPreferenceCategory.VisualStyle;
        RefreshFromSystem(paletteMayHaveChanged);
    }

    private void RefreshFromSystem(bool forceResourceRefresh)
    {
        if (_disposed || WpfApplication.Current is null)
        {
            return;
        }

        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            // Do not capture Preference here. A user can choose an explicit theme before this
            // queued OS callback runs; the UI-thread continuation must observe that latest choice.
            _ = WpfApplication.Current.Dispatcher.BeginInvoke(
                () => RefreshFromSystem(forceResourceRefresh));
            return;
        }

        if (Preference == AppTheme.System || forceResourceRefresh)
        {
            // High-contrast colours are materialized into the ResourceDictionary. Reinstall it
            // even when the effective theme enum remains HighContrast so a different Windows HC
            // colour scheme is reflected immediately. Preference is intentionally re-read here.
            Apply(Preference, forceResourceRefresh);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
