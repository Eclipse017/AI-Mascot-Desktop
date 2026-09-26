using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIMascot.Core;

namespace AIMascot.Grok;

internal sealed class SpriteImage : Image
{
    private readonly Dictionary<PetFace, BitmapSource> _frames = new();
    private readonly Dictionary<PetFace, byte[]> _pixels = new();
    private PetFace _face;
    private readonly ImageBrush _silhouette;
    public bool UsesFallback { get; private set; }
    public SpriteImage()
    {
        foreach (var (face, name) in new[] { (PetFace.Open, "idle"), (PetFace.Closed, "closed"), (PetFace.Happy, "happy") })
        {
            try
            {
                var source = new BitmapImage(); source.BeginInit();
                source.UriSource = new Uri($"pack://application:,,,/AIMascot.Grok;component/Assets/{name}.png");
                source.CacheOption = BitmapCacheOption.OnLoad; source.DecodePixelWidth = 900; source.EndInit(); source.Freeze();
                var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); bitmap.Freeze();
                _frames[face] = bitmap;
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); _pixels[face] = pixels;
            }
            catch (Exception e) when (face != PetFace.Open && e is IOException or NotSupportedException or FormatException)
            { UsesFallback = true; _frames[face] = _frames[PetFace.Open]; _pixels[face] = _pixels[PetFace.Open]; }
        }
        _silhouette = new ImageBrush(_frames[PetFace.Open]) { Stretch = Stretch.Fill }; _silhouette.Freeze();
        Source = _frames[PetFace.Open]; Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }
    public void SetFace(PetFace face)
    {
        if (_face == face) return;
        _face = face; Source = _frames[face];
        OpacityMask = face == PetFace.Open ? null : _silhouette;
    }
    public bool Hit(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) return false;
        var bitmap = _frames[_face];
        int x = Math.Clamp((int)(point.X / ActualWidth * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1);
        int y = Math.Clamp((int)(point.Y / ActualHeight * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1);
        var original = _frames[PetFace.Open];
        int originalX = Math.Clamp((int)(point.X / ActualWidth * original.PixelWidth), 0, original.PixelWidth-1);
        int originalY = Math.Clamp((int)(point.Y / ActualHeight * original.PixelHeight), 0, original.PixelHeight-1);
        return _pixels[_face][(y * bitmap.PixelWidth + x) * 4 + 3] >= 8 &&
            _pixels[PetFace.Open][(originalY * original.PixelWidth + originalX) * 4 + 3] >= 8;
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => Hit(parameters.HitPoint) ? new PointHitTestResult(this, parameters.HitPoint) : null;
}

// Draw effects inside the character's lower body, keeping the surrounding pixels transparent.
internal sealed class PetEffects : FrameworkElement
{
    public CompanionFrame Frame { get; set; }
    private static readonly Brush SparkleBrush = new SolidColorBrush(Color.FromRgb(169, 127, 223));
    public PetEffects() { IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        if (Frame.Sparkle > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                double x = ActualWidth * (.40 + i * .10), y = ActualHeight * (.83 - .03 * Math.Sin(i + Frame.Sparkle * 5));
                double r = 2.5 + 2 * Frame.Sparkle;
                dc.PushOpacity(.6 * Frame.Sparkle);
                var shape = new StreamGeometry();
                using (var g = shape.Open())
                {
                    g.BeginFigure(new Point(x, y-r), true, true);
                    g.LineTo(new Point(x+r*.3, y-r*.3), true, false); g.LineTo(new Point(x+r, y), true, false);
                    g.LineTo(new Point(x+r*.3, y+r*.3), true, false); g.LineTo(new Point(x, y+r), true, false);
                    g.LineTo(new Point(x-r*.3, y+r*.3), true, false); g.LineTo(new Point(x-r, y), true, false);
                    g.LineTo(new Point(x-r*.3, y-r*.3), true, false);
                }
                dc.DrawGeometry(SparkleBrush, null, shape); dc.Pop();
            }
        }
    }
}
