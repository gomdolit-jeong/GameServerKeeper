using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GameServerKeeper.Services;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views;

/// <summary>ViewModel 의 의미 색(Tone)을 실제 색으로. 화면 색은 전부 여기와 Theme.xaml 에서만 정한다.</summary>
public static class Palette
{
    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static readonly Dictionary<Tone, SolidColorBrush> Fg = new()
    {
        [Tone.Normal] = Brush("#181C23"), [Tone.Muted] = Brush("#6B7280"), [Tone.Faint] = Brush("#A0A6B2"),
        [Tone.Accent] = Brush("#4F46E5"), [Tone.Purple] = Brush("#7C3AED"), [Tone.Success] = Brush("#16A34A"),
        [Tone.Warning] = Brush("#D97706"), [Tone.Danger] = Brush("#DC2626"), [Tone.Info] = Brush("#2563EB"),
        [Tone.Neutral] = Brush("#6B7280"),
    };

    /// <summary>어두운 배경(사이드바)용 밝은 색</summary>
    private static readonly Dictionary<Tone, SolidColorBrush> FgDark = new()
    {
        [Tone.Normal] = Brush("#E5E7EB"), [Tone.Muted] = Brush("#8A94A6"), [Tone.Faint] = Brush("#6B7280"),
        [Tone.Accent] = Brush("#A5B4FC"), [Tone.Purple] = Brush("#C4B5FD"), [Tone.Success] = Brush("#4ADE80"),
        [Tone.Warning] = Brush("#FBBF24"), [Tone.Danger] = Brush("#F87171"), [Tone.Info] = Brush("#60A5FA"),
        [Tone.Neutral] = Brush("#8A94A6"),
    };

    private static readonly Dictionary<Tone, SolidColorBrush> Soft = new()
    {
        [Tone.Success] = Brush("#DCFCE7"), [Tone.Warning] = Brush("#FEF3C7"), [Tone.Danger] = Brush("#FEE2E2"),
        [Tone.Info] = Brush("#DBEAFE"), [Tone.Accent] = Brush("#EEF0FF"), [Tone.Purple] = Brush("#F3E8FF"),
    };
    private static readonly SolidColorBrush SoftDefault = Brush("#E5E7EB");

    public static SolidColorBrush Foreground(Tone t, bool dark = false) => (dark ? FgDark : Fg).GetValueOrDefault(t, Fg[Tone.Normal]);
    public static SolidColorBrush Background(Tone t) => Soft.GetValueOrDefault(t, SoftDefault);
}

/// <summary>Tone → 글자색. ConverterParameter="dark" 면 어두운 배경용.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Palette.Foreground(value is Tone t ? t : Tone.Normal, parameter as string == "dark");
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Tone → 연한 배경색 (상태 배지)</summary>
public sealed class ToneSoftBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Palette.Background(value is Tone t ? t : Tone.Neutral);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LogKindBrushConverter : IValueConverter
{
    private static readonly Brush Normal = Palette.Brush("#D6DAE2");
    private static readonly Brush Warn = Palette.Brush("#FBBF24");
    private static readonly Brush Error = Palette.Brush("#FF7878");
    private static readonly Brush Steam = Palette.Brush("#7DAAE6");
    private static readonly Brush Player = Palette.Brush("#6EDC96");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LogKind.Error => Error,
        LogKind.Warn => Warn,
        LogKind.SteamCmd => Steam,
        LogKind.Player => Player,
        _ => Normal
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>bool → Visible/Collapsed. ConverterParameter="invert" 면 반대.</summary>
public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool v = value is true;
        if (parameter as string == "invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>null 또는 빈 문자열이면 Collapsed</summary>
public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>0~1 비율 → 0~100 (ProgressBar)</summary>
public sealed class RatioPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Clamp(d, 0, 1) * 100 : 0.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>줄바꿈 여부 → TextWrapping</summary>
public sealed class WrapConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>줄바꿈 여부 → 가로 스크롤 (줄바꿈하려면 가로 스크롤을 꺼야 함)</summary>
public sealed class WrapScrollConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>선택 대화상자 버튼 종류 → 버튼 스타일</summary>
public sealed class ChoiceStyleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource(value switch
        {
            ChoiceKind.Primary => "PrimaryButton",
            ChoiceKind.Danger => "DangerButton",
            _ => "SecondaryButton"
        });
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
