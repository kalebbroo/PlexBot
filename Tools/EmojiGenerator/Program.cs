using PlexBot.Core.Discord.Design;
using SkiaSharp;
using DiscordColor = Discord.Color;

// Draws the bot's application emoji as PNGs and a contact sheet for checking legibility at Discord's sizes.
// Usage (from the repo root): dotnet run --project Tools/EmojiGenerator -- Images/Emoji Images/PlexBotBanner.png <sheet.png>

const int Size = 128;
SKColor white = new(255, 255, 255);
SKColor dark = new(30, 31, 34);

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: EmojiGenerator <outDir> <bannerPath> <contactSheetPath>");
    return 1;
}
string outDir = args[0];
string bannerPath = args[1];
string sheetPath = args[2];
Directory.CreateDirectory(outDir);

Dictionary<string, Action<SKCanvas>> glyphs = new()
{
    ["play"] = c => Fill(c, white, Poly(P(38, 26), P(38, 102), P(106, 64))),
    ["pause"] = c =>
    {
        Fill(c, white, RoundRect(32, 26, 26, 76, 6));
        Fill(c, white, RoundRect(70, 26, 26, 76, 6));
    },
    ["skip"] = c =>
    {
        Fill(c, white, Poly(P(22, 30), P(22, 98), P(72, 64)));
        Fill(c, white, RoundRect(84, 30, 18, 68, 5));
    },
    ["previous"] = c =>
    {
        Fill(c, white, Poly(P(106, 30), P(106, 98), P(56, 64)));
        Fill(c, white, RoundRect(26, 30, 18, 68, 5));
    },
    ["stop"] = c => Fill(c, white, RoundRect(30, 30, 68, 68, 10)),
    ["repeat"] = c => Repeat(c, false),
    ["repeat_track"] = c => Repeat(c, true),
    ["shuffle"] = c =>
    {
        Stroke(c, 10, white, P(26, 42), P(94, 86));
        Stroke(c, 10, white, P(26, 86), P(94, 42));
        Head(c, P(106, 90), P(94, 86));
        Head(c, P(106, 38), P(94, 42));
    },
    ["volume_up"] = c =>
    {
        Speaker(c);
        Stroke(c, 8, white, ArcPoints(62, 64, 18, -50, 50));
        Stroke(c, 8, white, ArcPoints(62, 64, 34, -50, 50));
    },
    ["volume_down"] = c =>
    {
        Speaker(c);
        Stroke(c, 8, white, ArcPoints(62, 64, 20, -50, 50));
    },
    ["volume_mute"] = c =>
    {
        Speaker(c);
        Stroke(c, 8, white, P(78, 48), P(104, 80));
        Stroke(c, 8, white, P(104, 48), P(78, 80));
    },
    ["radio"] = c =>
    {
        Outline(c, 10, white, RoundRect(22, 44, 84, 64, 12));
        Fill(c, white, Circle(82, 76, 11));
        Stroke(c, 6, white, P(54, 44), P(92, 14));
    },
    ["search"] = c =>
    {
        Outline(c, 12, white, Circle(56, 56, 28));
        Stroke(c, 14, white, P(78, 78), P(102, 102));
    },
    ["similar"] = c =>
    {
        Outline(c, 10, white, Circle(48, 64, 26));
        Outline(c, 10, white, Circle(80, 64, 26));
    },
    ["adventure"] = c =>
    {
        Outline(c, 8, white, Circle(64, 64, 46));
        Fill(c, white, Poly(P(92, 36), P(70, 70), P(36, 92), P(58, 58)));
    },
    ["queue"] = c =>
    {
        Fill(c, white, RoundRect(24, 28, 80, 13, 4));
        Fill(c, white, RoundRect(24, 57, 80, 13, 4));
        Fill(c, white, RoundRect(24, 86, 46, 13, 4));
    },
    ["playlist"] = c =>
    {
        Fill(c, white, RoundRect(20, 28, 64, 11, 4));
        Fill(c, white, RoundRect(20, 52, 64, 11, 4));
        Fill(c, white, RoundRect(20, 76, 34, 11, 4));
        Fill(c, white, Circle(92, 92, 12));
        Fill(c, white, RoundRect(99, 50, 10, 50, 4));
    },
    ["success"] = c => StatusBadge(c, DesignTokens.Success, white, g => Stroke(g, 12, white, P(40, 66), P(56, 82), P(88, 48))),
    ["error"] = c => StatusBadge(c, DesignTokens.Error, white, g =>
    {
        Stroke(g, 12, white, P(44, 44), P(84, 84));
        Stroke(g, 12, white, P(84, 44), P(44, 84));
    }),
    ["info"] = c => StatusBadge(c, DesignTokens.Info, white, g =>
    {
        Fill(g, white, Circle(64, 40, 7));
        Fill(g, white, RoundRect(57, 54, 14, 40, 6));
    }),
    ["warning"] = c => StatusBadge(c, DesignTokens.Warning, dark, g =>
    {
        Fill(g, dark, RoundRect(57, 28, 14, 50, 6));
        Fill(g, dark, Circle(64, 94, 8));
    }),
    ["loading"] = c => Stroke(c, 12, white, ArcPoints(64, 64, 38, 0, 300)),
};

foreach ((string name, Action<SKCanvas> draw) in glyphs)
{
    using SKBitmap image = new(Size, Size);
    image.Erase(SKColors.Transparent);
    using (SKCanvas canvas = new(image))
    {
        draw(canvas);
    }
    SavePng(image, Path.Combine(outDir, $"pb_{name}.png"));
}

// Plex badge: a rounded square in Plex's orange with a white P
using (SKBitmap badge = new(Size, Size))
{
    badge.Erase(SKColors.Transparent);
    using (SKCanvas canvas = new(badge))
    {
        Fill(canvas, new SKColor(229, 160, 13), RoundRect(14, 14, 100, 100, 22));
        DrawCenteredText(canvas, "P", 78, white, P(64, 66));
    }
    SavePng(badge, Path.Combine(outDir, "pb_plex.png"));
}

WriteContactSheet(outDir, sheetPath);
Console.WriteLine($"Wrote {glyphs.Count + 1} emoji to {outDir} and contact sheet to {sheetPath}");
return 0;

// ---- shapes ----

static SKPoint P(float x, float y) => new(x, y);

static SKPath Poly(params SKPoint[] points)
{
    using SKPathBuilder builder = new();
    builder.MoveTo(points[0]);
    for (int i = 1; i < points.Length; i++)
    {
        builder.LineTo(points[i]);
    }
    builder.Close();
    return builder.Detach();
}

static SKPath Circle(float x, float y, float radius)
{
    using SKPathBuilder builder = new();
    builder.AddCircle(x, y, radius, SKPathDirection.Clockwise);
    return builder.Detach();
}

/// <summary>Rounded rectangle path</summary>
static SKPath RoundRect(float x, float y, float w, float h, float r)
{
    using SKPathBuilder builder = new();
    builder.AddRoundRect(new SKRoundRect(SKRect.Create(x, y, w, h), r, r), SKPathDirection.Clockwise);
    return builder.Detach();
}

static SKPoint[] ArcPoints(float cx, float cy, float radius, float startDeg, float endDeg, int steps = 24)
{
    SKPoint[] points = new SKPoint[steps + 1];
    for (int i = 0; i <= steps; i++)
    {
        float a = (startDeg + (endDeg - startDeg) * i / steps) * MathF.PI / 180f;
        points[i] = P(cx + radius * MathF.Cos(a), cy + radius * MathF.Sin(a));
    }
    return points;
}

/// <summary>Fills a path, anti-aliased. Takes ownership of the path and disposes it.</summary>
static void Fill(SKCanvas c, SKColor color, SKPath path)
{
    using SKPaint paint = new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    c.DrawPath(path, paint);
    path.Dispose();
}

/// <summary>Strokes a path with a solid pen, anti-aliased. Takes ownership of the path and disposes it.</summary>
static void Outline(SKCanvas c, float width, SKColor color, SKPath path)
{
    using SKPaint paint = new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
    c.DrawPath(path, paint);
    path.Dispose();
}

/// <summary>Stroked polyline with round ends, so short strokes don't look clipped at small sizes</summary>
static void Stroke(SKCanvas c, float width, SKColor color, params SKPoint[] points)
{
    using SKPathBuilder builder = new();
    builder.MoveTo(points[0]);
    for (int i = 1; i < points.Length; i++)
    {
        builder.LineTo(points[i]);
    }
    using SKPath line = builder.Detach();
    using SKPaint paint = new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };
    c.DrawPath(line, paint);
}

/// <summary>Arrowhead at <paramref name="tip"/>, pointing away from <paramref name="from"/></summary>
static void Head(SKCanvas c, SKPoint tip, SKPoint from)
{
    float dx = tip.X - from.X, dy = tip.Y - from.Y;
    float len = MathF.Sqrt(dx * dx + dy * dy);
    dx /= len;
    dy /= len;
    const float size = 20f;
    SKPoint back = P(tip.X - dx * size, tip.Y - dy * size);
    SKPoint side = P(-dy * size * 0.7f, dx * size * 0.7f);
    Fill(c, SKColors.White, Poly(tip, P(back.X + side.X, back.Y + side.Y), P(back.X - side.X, back.Y - side.Y)));
}

static void Speaker(SKCanvas c) =>
    Fill(c, SKColors.White, Poly(P(20, 50), P(38, 50), P(60, 30), P(60, 98), P(38, 78), P(20, 78)));

/// <summary>Shared repeat icon: a stadium loop with arrowheads. A label sits in the middle for repeat-one.</summary>
static void Repeat(SKCanvas c, bool repeatOne)
{
    SKColor white = SKColors.White;
    List<SKPoint> loop = [.. ArcPoints(84, 64, 20, -90, 90), .. ArcPoints(44, 64, 20, 90, 270)];
    Outline(c, 10, white, Poly([.. loop]));
    Head(c, P(98, 44), P(84, 44));
    Head(c, P(30, 84), P(44, 84));
    if (!repeatOne) return;

    // Drawn as polygons: text labels did not render in this glyph
    Fill(c, white, RoundRect(60, 52, 9, 26, 3));
    Fill(c, white, Poly(P(60, 54), P(52, 60), P(52, 66), P(60, 61)));
}

/// <summary>A filled circle in a status colour with a glyph on top</summary>
static void StatusBadge(SKCanvas c, DiscordColor tokenColor, SKColor glyphColor, Action<SKCanvas> glyph)
{
    SKColor fill = new(tokenColor.R, tokenColor.G, tokenColor.B);
    Fill(c, fill, Circle(64, 64, 58));
    glyph(c);
}

/// <summary>Draws text centred horizontally on the origin and vertically on its line box</summary>
static void DrawCenteredText(SKCanvas c, string text, float size, SKColor color, SKPoint origin)
{
    using SKTypeface typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Bold);
    using SKFont font = new(typeface, size);
    using SKPaint paint = new() { Color = color, IsAntialias = true };
    SKFontMetrics metrics = font.Metrics;
    float width = font.MeasureText(text);
    float baseline = origin.Y - ((metrics.Ascent + metrics.Descent) / 2);
    c.DrawText(text, new SKPoint(origin.X - (width / 2), baseline), SKTextAlign.Left, font, paint);
}

static void WriteContactSheet(string outDir, string sheetPath)
{
    string[] files = Directory.GetFiles(outDir, "pb_*.png").OrderBy(f => f).ToArray();
    const int cell = 72;
    const int columns = 8;
    int rows = (files.Length + columns - 1) / columns;
    SKSamplingOptions sampling = new(SKCubicResampler.Mitchell);
    using SKBitmap sheet = new(columns * cell, (rows * cell) + 40);
    sheet.Erase(new SKColor(49, 51, 56));
    using SKCanvas canvas = new(sheet);
    for (int i = 0; i < files.Length; i++)
    {
        using SKBitmap glyph = SKBitmap.Decode(File.ReadAllBytes(files[i]));
        using SKBitmap large = Scaled(glyph, 44, sampling);
        using SKBitmap small = Scaled(glyph, 22, sampling);
        int x0 = (i % columns) * cell, y0 = (i / columns) * cell;
        canvas.DrawBitmap(large, new SKPoint(x0 + 14, y0 + 6), sampling);
        canvas.DrawBitmap(small, new SKPoint(x0 + 25, y0 + 52), sampling);
    }
    SavePng(sheet, sheetPath);
}

static SKBitmap Scaled(SKBitmap source, int size, SKSamplingOptions sampling) =>
    source.Resize(new SKImageInfo(size, size), sampling) ?? throw new InvalidOperationException($"Could not scale the emoji to {size}px");

static void SavePng(SKBitmap bitmap, string path)
{
    using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    using FileStream file = File.Create(path);
    png.SaveTo(file);
}
