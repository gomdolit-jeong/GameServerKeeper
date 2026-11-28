#if DEBUG
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GameServerKeeper.Views;

/// <summary>
/// 개발용(DEBUG 빌드 전용): 환경 변수 GSK_SNAPSHOT_DIR 이 있으면 열린 창을 주기적으로 PNG 로 저장한다.
/// 화면 캡처 없이 UI 를 확인하기 위한 도구. Release 빌드에는 포함되지 않는다.
/// </summary>
internal static class DebugSnapshot
{
    public static void StartIfRequested()
    {
        var dir = Environment.GetEnvironmentVariable("GSK_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            int i = 0;
            foreach (Window w in Application.Current.Windows)
            {
                if (!w.IsVisible || w.ActualWidth < 10) continue;
                try { Save(w, Path.Combine(dir, $"{i++}_{Sanitize(w.Title)}.png")); }
                catch { /* 개발용 — 무시 */ }
            }
        };
        timer.Start();
    }

    private static void Save(Window w, string path)
    {
        // 창 템플릿의 최상위(클라이언트 영역 전체). 내용에 여백이 있어도 잘리지 않음
        if (VisualTreeHelper.GetChildrenCount(w) == 0 || VisualTreeHelper.GetChild(w, 0) is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(w);
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        // 창 배경을 먼저 칠한 뒤 내용을 그 위에 그림
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(dv);
        bmp.Render(root);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static string Sanitize(string s) =>
        string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
#endif
