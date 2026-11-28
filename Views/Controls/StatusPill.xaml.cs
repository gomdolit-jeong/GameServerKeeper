using System.Windows;
using System.Windows.Controls;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views.Controls;

/// <summary>점 + 글자 상태 배지 (실행 중 / 중지됨 ...)</summary>
public partial class StatusPill : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(Tone), typeof(StatusPill),
        new PropertyMetadata(Tone.Neutral));

    public StatusPill() => InitializeComponent();

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}
