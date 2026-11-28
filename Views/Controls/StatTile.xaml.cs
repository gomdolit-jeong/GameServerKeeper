using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views.Controls;

/// <summary>아이콘 + 제목 + 큰 값 + 설명 (+ 막대) 타일</summary>
public partial class StatTile : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(StatTile));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatTile));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(StatTile));
    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(nameof(Info), typeof(StatInfo), typeof(StatTile));
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(StatTile),
        new PropertyMetadata(false, (d, e) => ((StatTile)d).ValueText.FontSize = (bool)e.NewValue ? 17 : 22));

    public StatTile() => InitializeComponent();

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public StatInfo? Info { get => (StatInfo?)GetValue(InfoProperty); set => SetValue(InfoProperty, value); }

    /// <summary>타일이 좁을 때 값 글씨를 작게</summary>
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
}
