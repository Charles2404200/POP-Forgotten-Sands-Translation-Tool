using System.Runtime.InteropServices;
using SkiaSharp;

namespace PopFs.Localization;

internal sealed record RasterGlyph(int Codepoint, int X, int Y, int Width, int Height,
    short OffsetX, short OffsetY, byte Advance);

internal sealed record RasterAtlas(int Width, int Height, byte[] Rgba,
    IReadOnlyList<RasterGlyph> Glyphs);

internal enum FontVisualStyle { Plain, Embossed, Soft, KeyLabel, CyanGlow, GoldGlow }

internal static class FontRasterizer
{
    public static RasterAtlas Render(string fontPath, IReadOnlyCollection<int> codepoints,
        int width, int height, float fontSize, string? fallbackPath = null,
        FontVisualStyle style = FontVisualStyle.Plain, float horizontalScale = 1f)
    {
        using var typeface = SKTypeface.FromFile(fontPath)
            ?? throw new InvalidDataException($"Cannot load font: {fontPath}");
        using var font = new SKFont(typeface, fontSize)
            { Edging = SKFontEdging.Antialias, ScaleX = horizontalScale };
        using var fallbackFace = fallbackPath is null ? null : SKTypeface.FromFile(fallbackPath);
        using var fallbackFont = fallbackFace is null ? null : new SKFont(fallbackFace, fontSize)
            { Edging = SKFontEdging.Antialias, ScaleX = horizontalScale };
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var margin = style switch
        {
            FontVisualStyle.Soft or FontVisualStyle.CyanGlow or FontVisualStyle.GoldGlow => 8,
            FontVisualStyle.Embossed or FontVisualStyle.KeyLabel => 6,
            _ => 4
        };
        var items = new List<(int Codepoint, string Text, SKRect Bounds, float Advance,
            int Width, int Height, SKFont Font)>();
        foreach (var codepoint in codepoints.Distinct().Order())
        {
            var text = char.ConvertFromUtf32(codepoint);
            var selected = font.GetGlyphs(text).All(g => g != 0) ? font :
                fallbackFont is not null && fallbackFont.GetGlyphs(text).All(g => g != 0) ? fallbackFont :
                throw new InvalidDataException($"Missing U+{codepoint:X4} in {Path.GetFileName(fontPath)} and fallback font.");
            var advance = selected.MeasureText(text, out var bounds, paint);
            var glyphWidth = Math.Max(1, (int)MathF.Ceiling(bounds.Width) + margin * 2);
            var glyphHeight = Math.Max(1, (int)MathF.Ceiling(bounds.Height) + margin * 2);
            items.Add((codepoint, text, bounds, advance, glyphWidth, glyphHeight, selected));
        }
        // Shelf packing keeps the output stable across runs.
        var placements = new Dictionary<int, (int X, int Y)>();
        var x = 0;
        var y = 0;
        var rowHeight = 0;
        foreach (var item in items.OrderByDescending(item => item.Height).ThenBy(item => item.Codepoint))
        {
            if (x + item.Width > width) { x = 0; y += rowHeight; rowHeight = 0; }
            if (y + item.Height > height)
                throw new InvalidDataException($"Font {Path.GetFileName(fontPath)} at {fontSize}px does not fit {width}x{height} ({items.Count} glyphs).");
            placements.Add(item.Codepoint, (x, y));
            x += item.Width;
            rowHeight = Math.Max(rowHeight, item.Height);
        }

        using var bitmap = new SKBitmap(new SKImageInfo(width, height,
            SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var item in items)
            {
                var place = placements[item.Codepoint];
                var left = place.X + margin - item.Bounds.Left;
                var baseline = place.Y + margin - item.Bounds.Top;
                if (style == FontVisualStyle.Plain)
                    canvas.DrawText(item.Text, left, baseline, item.Font, paint);
                else
                {
                    canvas.Save();
                    canvas.ClipRect(new SKRect(place.X, place.Y,
                        place.X + item.Width, place.Y + item.Height));
                    DrawEffect(canvas, item.Text, item.Font, left, baseline,
                        place.Y + margin, item.Bounds.Height, style);
                    canvas.Restore();
                }
            }
            canvas.Flush();
        }
        var rgba = new byte[checked(width * height * 4)];
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        // SKBitmap is premultiplied; PNG and the BC encoder expect straight RGBA.
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var alpha = rgba[i + 3];
            for (var channel = 0; channel < 3; channel++)
                rgba[i + channel] = alpha == 0 ? (byte)0 :
                    (byte)Math.Min(255, (rgba[i + channel] * 255 + alpha / 2) / alpha);
        }
        var glyphs = items.Select(item =>
        {
            var place = placements[item.Codepoint];
            return new RasterGlyph(item.Codepoint, place.X, place.Y, item.Width, item.Height,
                checked((short)Math.Clamp((int)MathF.Round(item.Bounds.Left) - margin, short.MinValue, short.MaxValue)),
                checked((short)Math.Clamp((int)MathF.Round(item.Bounds.Top) - margin, short.MinValue, short.MaxValue)),
                (byte)Math.Clamp((int)MathF.Round(item.Advance), 0, 255));
        }).ToList();
        return new RasterAtlas(width, height, rgba, glyphs);
    }

    private static void DrawEffect(SKCanvas canvas, string text, SKFont font,
        float x, float baseline, float top, float height, FontVisualStyle style)
    {
        var soft = style is FontVisualStyle.Soft or FontVisualStyle.CyanGlow or FontVisualStyle.GoldGlow;
        var cyan = style == FontVisualStyle.CyanGlow;
        var gold = style == FontVisualStyle.GoldGlow;
        var keyLabel = style == FontVisualStyle.KeyLabel;
        using var glowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal,
            cyan || gold ? 2.4f : soft ? 2.2f : keyLabel ? 1.3f : 1.1f);
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Color = gold ? new SKColor(255, 179, 68, 215) :
                cyan ? new SKColor(67, 184, 255, 220) :
                new SKColor(175, 212, 232, soft ? (byte)100 : (byte)75),
            MaskFilter = glowFilter,
            Style = cyan || gold ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = cyan || gold ? 3.2f : 0f
        };
        canvas.DrawText(text, x, baseline + (cyan || gold ? 0 : 1), font, glow);

        if (cyan || gold)
        {
            using var rim = new SKPaint
            {
                IsAntialias = true,
                Color = gold ? new SKColor(222, 137, 47, 210) :
                    new SKColor(70, 176, 239, 205),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                StrokeJoin = SKStrokeJoin.Round
            };
            canvas.DrawText(text, x, baseline, font, rim);
            using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
            canvas.DrawText(text, x, baseline, font, white);
            return;
        }

        using (var shadow = new SKPaint
        {
            IsAntialias = true, Color = new SKColor(17, 34, 49,
                soft ? (byte)100 : keyLabel ? (byte)240 : (byte)210),
            Style = SKPaintStyle.Stroke, StrokeWidth = soft ? 2.4f : keyLabel ? 1.5f : 2.1f
        })
            canvas.DrawText(text, x + 0.8f, baseline + 1.2f, font, shadow);

        using var fill = new SKPaint { IsAntialias = true };
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(x, top), new SKPoint(x, top + Math.Max(1, height)),
            soft
                ? [new SKColor(217, 235, 244, 175), new SKColor(141, 177, 199, 150)]
                : [new SKColor(249, 252, 254), new SKColor(111, 139, 157),
                    new SKColor(230, 238, 244)],
            soft ? [0f, 1f] : [0f, 0.57f, 1f], SKShaderTileMode.Clamp);
        fill.Shader = gradient;
        if (soft)
        {
            using var softFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal,
                0.8f);
            fill.MaskFilter = softFilter;
            canvas.DrawText(text, x, baseline, font, fill);
        }
        else
        {
            canvas.DrawText(text, x, baseline, font, fill);
            using var highlight = new SKPaint
            {
                IsAntialias = true, Color = new SKColor(244, 250, 255, 165),
                Style = SKPaintStyle.Stroke, StrokeWidth = 0.85f
            };
            canvas.DrawText(text, x - 0.3f, baseline - 0.5f, font, highlight);
        }
    }
}
