using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameServerKeeper.Views;

/// <summary>버튼 템플릿에서 쓰는 추가 속성: 아이콘 글리프, 마우스 올림/누름 색, 선택 상태(사이드바)</summary>
public static class ButtonAssist
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(ButtonAssist), new FrameworkPropertyMetadata(null));
    public static string? GetGlyph(DependencyObject d) => (string?)d.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject d, string? v) => d.SetValue(GlyphProperty, v);

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(ButtonAssist), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject d) => (Brush?)d.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject d, Brush? v) => d.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
        "PressedBackground", typeof(Brush), typeof(ButtonAssist), new FrameworkPropertyMetadata(null));
    public static Brush? GetPressedBackground(DependencyObject d) => (Brush?)d.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject d, Brush? v) => d.SetValue(PressedBackgroundProperty, v);

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(ButtonAssist), new FrameworkPropertyMetadata(false));
    public static bool GetIsSelected(DependencyObject d) => (bool)d.GetValue(IsSelectedProperty);
    public static void SetIsSelected(DependencyObject d, bool v) => d.SetValue(IsSelectedProperty, v);
}

/// <summary>PasswordBox.Password 는 바인딩이 안 되므로 첨부 속성으로 양방향 연결</summary>
public static class PasswordHelper
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordHelper),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(PasswordHelper), new PropertyMetadata(false));

    public static string? GetPassword(DependencyObject d) => (string?)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string? v) => d.SetValue(PasswordProperty, v);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (!(bool)box.GetValue(HookedProperty))
        {
            box.SetValue(HookedProperty, true);
            box.PasswordChanged += (_, _) => SetPassword(box, box.Password);
        }
        var value = e.NewValue as string ?? "";
        if (box.Password != value) box.Password = value;
    }
}
