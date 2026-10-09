using PlexBot.Core.Discord.Design;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using DiscordColor = Discord.Color;

// Draws the bot's application emoji as PNGs and a contact sheet for checking legibility at Discord's sizes.
// Usage (from the repo root): dotnet run --project Tools/EmojiGenerator -- Images/Emoji Images/PlexBotBanner.png <sheet.png>

const int Size = 128;
Rgba32 white = new(255, 255, 255, 255);
Rgba32 dark = new(30, 31, 34, 255);

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: EmojiGenerator <outDir> <bannerPath> <contactSheetPath>");
    return 1;
}
string outDir = args[0];
string bannerPath = args[1];
string sheetPath = args[2];
Directory.CreateDirectory(outDir);

Dictionary<string, Action<IImageProcessingContext>> glyphs = new()
{
    ["play"] = c => c.Fill(white, Poly(P(38, 26), P(38, 102), P(106, 64))),
    ["pause"] = c =>
    {
        c.Fill(white, RoundRect(32, 26, 26, 76, 6));
        c.Fill(white, RoundRect(70, 26, 26, 76, 6));
    },
    ["skip"] = c =>
    {
        c.Fill(white, Poly(P(22, 30), P(22, 98), P(72, 64)));
        c.Fill(white, RoundRect(84, 30, 18, 68, 5));
    },
    ["previous"] = c =>
    {
        c.Fill(white, Poly(P(106, 30), P(106, 98), P(56, 64)));
        c.Fill(white, RoundRect(26, 30, 18, 68, 5));
    },
    ["stop"] = c => c.Fill(white, RoundRect(30, 30, 68, 68, 10)),
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
        c.Draw(Pens.Solid(white, 10), RoundRect(22, 44, 84, 64, 12));
        c.Fill(white, Circle(82, 76, 11));
        Stroke(c, 6, white, P(54, 44), P(92, 14));
    },
    ["search"] = c =>
    {
        c.Draw(Pens.Solid(white, 12), Circle(56, 56, 28));
        Stroke(c, 14, white, P(78, 78), P(102, 102));
    },
    ["similar"] = c =>
    {
        c.Draw(Pens.Solid(white, 10), Circle(48, 64, 26));
        c.Draw(Pens.Solid(white, 10), Circle(80, 64, 26));
    },
    ["adventure"] = c =>
    {
        c.Draw(Pens.Solid(white, 8), Circle(64, 64, 46));
        c.Fill(white, Poly(P(92, 36), P(70, 70), P(36, 92), P(58, 58)));
    },
    ["queue"] = c =>
    {
        c.Fill(white, RoundRect(24, 28, 80, 13, 4));
        c.Fill(white, RoundRect(24, 57, 80, 13, 4));
        c.Fill(white, RoundRect(24, 86, 46, 13, 4));
    },
    ["playlist"] = c =>
    {
        c.Fill(white, RoundRect(20, 28, 64, 11, 4));
        c.Fill(white, RoundRect(20, 52, 64, 11, 4));
        c.Fill(white, RoundRect(20, 76, 34, 11, 4));
        c.Fill(white, Circle(92, 92, 12));
        c.Fill(white, RoundRect(99, 50, 10, 50, 4));
    },
    ["success"] = c => StatusBadge(c, DesignTokens.Success, white, g => Stroke(g, 12, white, P(40, 66), P(56, 82), P(88, 48))),
    ["error"] = c => StatusBadge(c, DesignTokens.Error, white, g =>
    {
        Stroke(g, 12, white, P(44, 44), P(84, 84));
        Stroke(g, 12, white, P(84, 44), P(44, 84));
    }),
    ["info"] = c => StatusBadge(c, DesignTokens.Info, white, g =>
    {
        g.Fill(white, Circle(64, 40, 7));
        g.Fill(white, RoundRect(57, 54, 14, 40, 6));
    }),
    ["warning"] = c => StatusBadge(c, DesignTokens.Warning, dark, g =>
    {
        g.Fill(dark, RoundRect(57, 28, 14, 50, 6));
        g.Fill(dark, Circle(64, 94, 8));
    }),
    ["loading"] = c => Stroke(c, 12, white, ArcPoints(64, 64, 38, 0, 300)),
};

foreach ((string name, Action<IImageProcessingContext> draw) in glyphs)
{
    using Image<Rgba32> image = new(Size, Size);
    image.Mutate(draw);
    image.Save(System.IO.Path.Combine(outDir, $"pb_{name}.png"));
}

// Plex badge: a rounded square in Plex's orange with a white P
using (Image<Rgba32> badge = new(Size, Size))
{
    badge.Mutate(c =>
    {
        c.Fill(new Rgba32(229, 160, 13, 255), RoundRect(14, 14, 100, 100, 22));
        c.DrawText(new RichTextOptions(SystemFonts.Collection.Families.First().CreateFont(78, FontStyle.Bold))
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Origin = P(64, 66),
        }, "P", Brushes.Solid(white));
    });
    badge.Save(System.IO.Path.Combine(outDir, "pb_plex.png"));
}

WriteContactSheet(outDir, sheetPath);
Console.WriteLine($"Wrote {glyphs.Count + 1} emoji to {outDir} and contact sheet to {sheetPath}");
return 0;

// ---- shapes ----

static PointF P(float x, float y) => new(x, y);

static IPath Poly(params PointF[] points) => new Polygon(new LinearLineSegment(points));

static IPath Circle(float x, float y, float radius) => new EllipsePolygon(x, y, radius);

/// <summary>Rounded rectangle as a polygon, eight segments per corner</summary>
static IPath RoundRect(float x, float y, float w, float h, float r)
{
    List<PointF> points = [];
    void Corner(float cx, float cy, float startDeg)
    {
        for (int i = 0; i <= 8; i++)
        {
            float a = (startDeg + 90f * i / 8f) * MathF.PI / 180f;
            points.Add(P(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a)));
        }
    }
    Corner(x + w - r, y + r, -90);
    Corner(x + w - r, y + h - r, 0);
    Corner(x + r, y + h - r, 90);
    Corner(x + r, y + r, 180);
    return Poly([.. points]);
}

static PointF[] ArcPoints(float cx, float cy, float radius, float startDeg, float endDeg, int steps = 24)
{
    PointF[] points = new PointF[steps + 1];
    for (int i = 0; i <= steps; i++)
    {
        float a = (startDeg + (endDeg - startDeg) * i / steps) * MathF.PI / 180f;
        points[i] = P(cx + radius * MathF.Cos(a), cy + radius * MathF.Sin(a));
    }
    return points;
}

/// <summary>Stroked polyline with round ends, so short strokes don't look clipped at small sizes</summary>
static void Stroke(IImageProcessingContext c, float width, Rgba32 color, params PointF[] points)
{
    c.DrawLine(Pens.Solid(color, width), points);
    c.Fill(color, Circle(points[0].X, points[0].Y, width / 2));
    c.Fill(color, Circle(points[^1].X, points[^1].Y, width / 2));
}

/// <summary>Arrowhead at <paramref name="tip"/>, pointing away from <paramref name="from"/></summary>
static void Head(IImageProcessingContext c, PointF tip, PointF from)
{
    float dx = tip.X - from.X, dy = tip.Y - from.Y;
    float len = MathF.Sqrt(dx * dx + dy * dy);
    dx /= len;
    dy /= len;
    const float size = 20f;
    PointF back = P(tip.X - dx * size, tip.Y - dy * size);
    PointF side = P(-dy * size * 0.7f, dx * size * 0.7f);
    c.Fill(new Rgba32(255, 255, 255, 255), Poly(tip, P(back.X + side.X, back.Y + side.Y), P(back.X - side.X, back.Y - side.Y)));
}

static void Speaker(IImageProcessingContext c) =>
    c.Fill(new Rgba32(255, 255, 255, 255), Poly(P(20, 50), P(38, 50), P(60, 30), P(60, 98), P(38, 78), P(20, 78)));

/// <summary>Shared repeat icon: a stadium loop with arrowheads. A label sits in the middle for repeat-one.</summary>
static void Repeat(IImageProcessingContext c, bool repeatOne)
{
    Rgba32 white = new(255, 255, 255, 255);
    List<PointF> loop = [.. ArcPoints(84, 64, 20, -90, 90), .. ArcPoints(44, 64, 20, 90, 270)];
    c.Draw(Pens.Solid(white, 10), Poly([.. loop]));
    Head(c, P(98, 44), P(84, 44));
    Head(c, P(30, 84), P(44, 84));
    if (!repeatOne) return;

    // Drawn as polygons: text labels did not render in this glyph
    c.Fill(white, RoundRect(60, 52, 9, 26, 3));
    c.Fill(white, Poly(P(60, 54), P(52, 60), P(52, 66), P(60, 61)));
}

/// <summary>A filled circle in a status colour with a glyph on top</summary>
static void StatusBadge(IImageProcessingContext c, DiscordColor tokenColor, Rgba32 glyphColor, Action<IImageProcessingContext> glyph)
{
    Rgba32 fill = new(tokenColor.R, tokenColor.G, tokenColor.B, 255);
    c.Fill(fill, Circle(64, 64, 58));
    glyph(c);
}

static void WriteContactSheet(string outDir, string sheetPath)
{
    string[] files = Directory.GetFiles(outDir, "pb_*.png").OrderBy(f => f).ToArray();
    const int cell = 72;
    const int columns = 8;
    int rows = (files.Length + columns - 1) / columns;
    using Image<Rgba32> sheet = new(columns * cell, rows * cell + 40, new Rgba32(49, 51, 56, 255));
    for (int i = 0; i < files.Length; i++)
    {
        using Image<Rgba32> glyph = Image.Load<Rgba32>(files[i]);
        using Image<Rgba32> large = glyph.Clone(x => x.Resize(44, 44));
        using Image<Rgba32> small = glyph.Clone(x => x.Resize(22, 22));
        int x0 = (i % columns) * cell, y0 = (i / columns) * cell;
        sheet.Mutate(c => c.DrawImage(large, new Point(x0 + 14, y0 + 6), 1f).DrawImage(small, new Point(x0 + 25, y0 + 52), 1f));
    }
    sheet.Save(sheetPath);
}
