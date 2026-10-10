using System.Net.Http;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils.Http;
using SkiaSharp;

using Path = System.IO.Path;

namespace PlexBot.Utils;

/// <summary>Provides utilities for generating rich media player images with album art, track information, and visual effects for Discord embeds</summary>
public static class ImageBuilder
{
    /// <summary>Royal blue used for the volume bar fill and the repeat indicator</summary>
    public static readonly SKColor AccentBlue = new(65, 105, 225);

    private static readonly HttpClientWrapper? _httpClient;
    private static readonly SKTypeface? _typeface;
    private static readonly Dictionary<string, SKBitmap> _iconCache = [];

    private const int CardWidth = 800;
    private const int CardHeight = 400;
    private const float CornerRadius = 24f;

    // The corner mask is the same for every card, so it is built once
    private static readonly Lazy<SKBitmap> _cornerMask = new(() => BuildCornerMask(CardWidth, CardHeight, CornerRadius));

    // One resampling choice for every resize, so shrinking the artwork and enlarging the blur look alike
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.CatmullRom);

    // These paths cover both standard Linux/Docker locations and system-specific ones
    private static readonly string[] _fontPaths =
    [
        // Linux/Docker font paths (based on apt-get packages)
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",           // fonts-dejavu
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",       // fonts-noto
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",    // fonts-noto-cjk
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", // fonts-liberation
        // Additional CJK fonts
        "/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf",         // fonts-ipafont-gothic
        "/usr/share/fonts/opentype/ipafont-mincho/ipam.ttf",         // fonts-ipafont-mincho
        // App-bundled font option
        Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf")
    ];

    static ImageBuilder()
    {
        try
        {
            Logs.Debug("ImageBuilder initialization started");
            // Initialize HttpClientWrapper
            try
            {
                SocketsHttpHandler handler = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 10
                };
                HttpClient client = new(handler);
                _httpClient = new HttpClientWrapper(client, "ImageBuilder");
                Logs.Debug("HttpClientWrapper initialized successfully");
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to initialize HttpClientWrapper: {ex.Message}");
                throw new InvalidOperationException("Failed to initialize essential HttpClientWrapper", ex);
            }
            // Initialize font system
            try
            {
                Logs.Debug("Attempting to find usable fonts...");
                List<string> loadedFonts = [];
                List<SKTypeface> typefaces = [];
                foreach (string fontPath in _fontPaths)
                {
                    if (!File.Exists(fontPath)) continue;
                    SKTypeface? typeface = SKTypeface.FromFile(fontPath);
                    if (typeface is null)
                    {
                        Logs.Warning($"Failed to load font {fontPath}");
                        continue;
                    }
                    typefaces.Add(typeface);
                    loadedFonts.Add(Path.GetFileName(fontPath));
                    Logs.Debug($"Successfully loaded font: {fontPath}");
                }
                // Prefer a CJK-capable family, then any font that loaded
                string[] cjkFontNames = ["Noto", "CJK", "Gothic", "Mincho", "Apple"];
                _typeface = typefaces.FirstOrDefault(t => cjkFontNames.Any(cjk => t.FamilyName.Contains(cjk, StringComparison.OrdinalIgnoreCase)))
                    ?? typefaces.FirstOrDefault();
                if (_typeface is not null)
                {
                    Logs.Debug($"Selected font: {_typeface.FamilyName}");
                }
                Logs.Info($"ImageBuilder initialized with {loadedFonts.Count} fonts: {string.Join(", ", loadedFonts)}");
            }
            catch (Exception ex)
            {
                Logs.Warning($"Font initialization error: {ex.Message}. Will use system default fonts.");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"ImageBuilder failed to initialize properly: {ex.Message}");
            // We won't rethrow since we don't want to prevent the application from starting
        }
    }

    /// <summary>Renders the player card for a track and encodes it as a PNG for a Discord attachment</summary>
    /// <param name="track">The track being played</param>
    /// <param name="player">Optional player, for the volume and repeat mode</param>
    /// <param name="upcomingTracks">Optional queue, shown as Next Up</param>
    /// <param name="prefetchService">Optional prefetch cache, checked before the artwork is downloaded</param>
    /// <returns>A stream of PNG bytes positioned at the start. The caller disposes it.</returns>
    public static async Task<MemoryStream> BuildPlayerPngAsync(CustomTrackQueueItem track, CustomLavaLinkPlayer? player = null, List<CustomTrackQueueItem>? upcomingTracks = null, ITrackPrefetchService? prefetchService = null)
    {
        using SKBitmap card = await BuildPlayerBitmapAsync(track, player, upcomingTracks, prefetchService);
        using SKData png = card.Encode(SKEncodedImageFormat.Png, 100);
        MemoryStream stream = new();
        png.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static async Task<SKBitmap> BuildPlayerBitmapAsync(CustomTrackQueueItem track, CustomLavaLinkPlayer? player, List<CustomTrackQueueItem>? upcomingTracks, ITrackPrefetchService? prefetchService)
    {
        try
        {
            // Get the album artwork URL
            string artworkUrl = track.Artwork ?? "";
            if (string.IsNullOrEmpty(artworkUrl) || artworkUrl == "N/A")
            {
                artworkUrl = "https://via.placeholder.com/150"; // TODO: Add a real placeholder image
            }
            using SKBitmap albumArt = await LoadArtworkAsync(artworkUrl, prefetchService);
            return RenderCard(track, player, upcomingTracks, albumArt);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to build player image: {ex.Message}");
            // Create and return a fallback image
            return Solid(CardWidth, CardHeight, SKColors.Black);
        }
    }

    private static SKBitmap RenderCard(CustomTrackQueueItem track, CustomLavaLinkPlayer? player, List<CustomTrackQueueItem>? upcomingTracks, SKBitmap albumArt)
    {
        SKBitmap card = new(CardWidth, CardHeight);
        try
        {
            using SKCanvas canvas = new(card);
            canvas.Clear(SKColors.Black);
            DrawBlurredBackground(canvas, albumArt);
            DrawOverlay(canvas);
            DrawDisplayArt(canvas, albumArt);
            DrawTrackText(canvas, track, player, upcomingTracks);
            ApplyRoundedCorners(canvas);
            return card;
        }
        catch
        {
            card.Dispose();
            throw;
        }
    }

    private static void DrawBlurredBackground(SKCanvas canvas, SKBitmap albumArt)
    {
        // Blur at thumbnail size, then scale up: the same soft look for a fraction of the work
        using SKBitmap thumbnail = Resized(albumArt, 160, 90);
        using SKBitmap blurred = Solid(160, 90, SKColors.Transparent);
        using SKImageFilter blur = SKImageFilter.CreateBlur(4f, 4f);
        using (SKCanvas blurCanvas = new(blurred))
        using (SKPaint blurPaint = new() { ImageFilter = blur })
        {
            blurCanvas.DrawBitmap(thumbnail, new SKPoint(0, 0), SKSamplingOptions.Default, blurPaint);
        }
        using SKBitmap background = Resized(blurred, CardWidth + 100, CardHeight + 100);
        canvas.DrawBitmap(background, new SKPoint(-50, -50), SKSamplingOptions.Default);
    }

    private static void DrawOverlay(SKCanvas canvas)
    {
        // A darker overlay for better text contrast, then a gradient
        using SKPaint overlay = new() { Color = new SKColor(0, 0, 0, 180), IsAntialias = true };
        canvas.DrawRect(0, 0, CardWidth, CardHeight, overlay);
        using SKShader gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(CardWidth, CardHeight),
            [new SKColor(0, 0, 0, 50), new SKColor(0, 0, 0, 100)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using SKPaint gradientPaint = new() { Shader = gradient, IsAntialias = true };
        canvas.DrawRect(0, 0, CardWidth, CardHeight, gradientPaint);
    }

    private static void DrawDisplayArt(SKCanvas canvas, SKBitmap albumArt)
    {
        // Crop to a square around the centre, then scale to fit the left side of the card
        int size = Math.Min(albumArt.Width, albumArt.Height);
        int left = (albumArt.Width - size) / 2;
        int top = (albumArt.Height - size) / 2;
        using SKBitmap square = new();
        albumArt.ExtractSubset(square, new SKRectI(left, top, left + size, top + size));
        using SKBitmap display = Resized(square, 280, 280);
        canvas.DrawBitmap(display, new SKPoint(40, 60), SKSamplingOptions.Default);
    }

    private static void DrawTrackText(SKCanvas canvas, CustomTrackQueueItem track, CustomLavaLinkPlayer? player, List<CustomTrackQueueItem>? upcomingTracks)
    {
        try
        {
            using SKFont titleFont = CreateFont(40);
            using SKFont artistFont = CreateFont(32);
            using SKFont infoFont = CreateFont(20);
            using SKFont smallInfoFont = CreateFont(16);
            int textX = 360;  // Start text after the album art
            int maxWidth = 400; // Maximum width for text
            // Title
            DrawText(canvas, Fit(track.Title ?? "Unknown Title", titleFont, maxWidth), titleFont, SKColors.White, textX, 70);
            // Artist
            DrawText(canvas, Fit(track.Artist ?? "Unknown Artist", artistFont, maxWidth), artistFont, new SKColor(220, 220, 220), textX, 130);
            // Album
            DrawText(canvas, Fit(track.Album ?? "Unknown Album", infoFont, maxWidth), infoFont, new SKColor(180, 180, 180), textX, 190);
            // Time icon, then the duration after it
            SKBitmap timeIcon = GetIcon("time.png");
            canvas.DrawBitmap(timeIcon, new SKPoint(textX, 230), SKSamplingOptions.Default);
            int durationTextX = textX + timeIcon.Width + 8; // 8px spacing between icon and text
            DrawText(canvas, track.Duration ?? "00:00", infoFont, new SKColor(180, 180, 180), durationTextX, 230);
            // Volume indicator
            int volumePercent = player != null ? (int)Math.Round(player.Volume * 100) : 20;
            DrawVolumeIndicator(canvas, textX, 275, 100, 8, volumePercent, infoFont, smallInfoFont);
            // Repeat indicator
            string repeatMode = player?.RepeatMode switch
            {
                TrackRepeatMode.Track => "Track",
                TrackRepeatMode.Queue => "Queue",
                _ => "None"
            };
            DrawRepeatIndicator(canvas, textX, 305, repeatMode, infoFont, smallInfoFont);
            // Next Up queue preview on the right side
            if (upcomingTracks is { Count: > 0 })
            {
                int nextUpX = 560; // Right portion of the image
                int nextUpY = 225;
                int nextUpMaxWidth = 200;
                DrawText(canvas, "Next Up", infoFont, new SKColor(180, 180, 180), nextUpX, nextUpY);
                for (int i = 0; i < Math.Min(upcomingTracks.Count, 2); i++)
                {
                    CustomTrackQueueItem upcoming = upcomingTracks[i];
                    int itemY = nextUpY + 28 + (i * 40);
                    DrawText(canvas, Fit(upcoming.Title ?? "Unknown", smallInfoFont, nextUpMaxWidth), smallInfoFont, SKColors.White, nextUpX, itemY);
                    DrawText(canvas, Fit(upcoming.Artist ?? "Unknown", smallInfoFont, nextUpMaxWidth), smallInfoFont, new SKColor(140, 140, 140), nextUpX, itemY + 18);
                }
            }
            // Requested by credit (bottom of image)
            string credit = "Requested by: " + (track.RequestedBy ?? "Unknown");
            DrawText(canvas, Fit(credit, smallInfoFont, maxWidth + 200), smallInfoFont, new SKColor(150, 150, 150), 40, 365);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error adding text: {ex.Message}");
        }
    }

    // Clears the pixels outside a rounded rectangle. The mask is built once; each card only composites it.
    private static void ApplyRoundedCorners(SKCanvas canvas)
    {
        using SKPaint paint = new() { BlendMode = SKBlendMode.DstIn };
        canvas.DrawBitmap(_cornerMask.Value, new SKPoint(0, 0), SKSamplingOptions.Default, paint);
    }

    internal static SKBitmap BuildCornerMask(int width, int height, float radius)
    {
        SKBitmap mask = Solid(width, height, SKColors.Transparent);
        using SKCanvas canvas = new(mask);
        using SKPaint paint = new() { Color = SKColors.White, IsAntialias = true };
        canvas.DrawRoundRect(SKRect.Create(0, 0, width, height), new SKSize(radius, radius), paint);
        return mask;
    }

    private static void DrawRoundedRectangle(SKCanvas canvas, float x, float y, float width, float height, float radius, SKColor color, bool fill = true)
    {
        // Make sure radius isn't too large for the rectangle
        radius = Math.Min(radius, Math.Min(width / 2, height / 2));
        using SKPaint paint = new()
        {
            Color = color,
            IsAntialias = true,
            Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(SKRect.Create(x, y, width, height), new SKSize(radius, radius), paint);
    }

    /// <summary>Draws text with its top at y, where the card's layout has always placed it</summary>
    private static void DrawText(SKCanvas canvas, string text, SKFont font, SKColor color, float x, float y)
    {
        using SKPaint paint = new() { Color = color, IsAntialias = true };
        canvas.DrawText(text, new SKPoint(x, y + BaselineOffset(font)), SKTextAlign.Left, font, paint);
    }

    // Places the baseline where the previous renderer put it. Its rule isn't exposed, so this is a fit: the font's
    // ascent and descent centred in a one-em line, which matched within about 1px on Noto Sans and Noto Sans CJK.
    private static float BaselineOffset(SKFont font)
    {
        SKFontMetrics metrics = font.Metrics;
        return (font.Size / 2) - ((metrics.Ascent + metrics.Descent) / 2);
    }

    private static string Fit(string text, SKFont font, float maxWidth) => TruncateToWidth(text, s => font.MeasureText(s), maxWidth);

    /// <summary>Shortens text to fit a width and ends it with an ellipsis. Takes a measure function so the rule is testable without a font.</summary>
    internal static string TruncateToWidth(string text, Func<string, float> measure, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || measure(text) <= maxWidth) return text;
        // Keep the longest prefix that still fits with an ellipsis
        for (int i = text.Length - 1; i >= 0; i--)
        {
            string truncated = text[..i] + "...";
            if (measure(truncated) <= maxWidth) return truncated;
        }
        return "...";
    }

    private static SKBitmap GetIcon(string iconName)
    {
        lock (_iconCache)
        {
            if (_iconCache.TryGetValue(iconName, out SKBitmap? cachedIcon))
            {
                return cachedIcon;
            }
            SKBitmap icon = LoadIcon(iconName);
            _iconCache[iconName] = icon;
            return icon;
        }
    }

    private static SKBitmap LoadIcon(string iconName)
    {
        string? path = AssetPaths.FindFile("Images", "Icons", iconName);
        if (path is not null)
        {
            try
            {
                Logs.Debug($"Loading icon from {path}");
                SKBitmap? icon = SKBitmap.Decode(File.ReadAllBytes(path));
                if (icon is not null) return icon;
                Logs.Error($"Failed to decode icon from {path}");
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to load icon from {path}: {ex.Message}");
            }
        }
        // Cached as blank, so a missing icon is logged and allocated once
        Logs.Error($"Could not find icon file: {iconName}");
        return Solid(24, 24, SKColors.Transparent);
    }

    private static void DrawVolumeIndicator(SKCanvas canvas, int x, int y, int width, int height, int volumePercent, SKFont labelFont, SKFont valueFont)
    {
        // Ensure volume is between 0-100
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        // Load and draw the volume icon (vertically centered with the bar)
        SKBitmap icon = GetIcon("audio.png");
        int iconY = y + (icon.Height - height) / 2 - 8; // Center icon relative to bar
        canvas.DrawBitmap(icon, new SKPoint(x, iconY), SKSamplingOptions.Default);
        // Bar starts after the icon with spacing
        int barX = x + icon.Width + 8;
        // Background track - with rounded corners
        int barY = y + icon.Height / 2 - height / 2; // Vertically center the bar with the icon
        int cornerRadius = height;
        DrawRoundedRectangle(canvas, barX, barY, width, height, cornerRadius, new SKColor(80, 80, 80, 200), true);
        // Active volume level - with rounded corners
        float fillWidth = (width * volumePercent) / 100f;
        if (fillWidth > 0)
        {
            DrawRoundedRectangle(canvas, barX, barY, (int)fillWidth, height, cornerRadius, AccentBlue, true);
        }
        // Percentage text after the bar (vertically centered)
        int textX = barX + width + 8;
        int textY = barY - 4; // Slight offset up to visually center text with bar
        DrawText(canvas, $"{volumePercent}%", valueFont, SKColors.White, textX, textY);
    }

    private static void DrawRepeatIndicator(SKCanvas canvas, int x, int y, string repeatMode, SKFont labelFont, SKFont valueFont)
    {
        SKColor indicatorColor = new(120, 120, 120, 255);
        string displayText = "Off";
        int yOffset = 10; // Offset for the text position to give more padding between volume bar and repeat indicator
        // Determine the proper display based on repeat mode
        switch (repeatMode.ToLower())
        {
            case "track":
                displayText = "Track";
                indicatorColor = AccentBlue;
                break;
            case "queue":
                displayText = "Queue";
                indicatorColor = AccentBlue;
                break;
            case "none":
            default:
                // Default values already set
                break;
        }
        // Load and draw the repeat icon
        SKBitmap icon = GetIcon("repeat.png");
        canvas.DrawBitmap(icon, new SKPoint(x, y + yOffset), SKSamplingOptions.Default);
        // Calculate spacing based on icon size
        int iconWidth = icon.Width;
        int textX = x + iconWidth + 8; // 8px spacing between icon and text
        DrawText(canvas, " Repeat", labelFont, SKColors.White, textX, y + yOffset);
        // Draw mode text (position relative to the label)
        DrawText(canvas, displayText, valueFont, indicatorColor, textX + 100, y + yOffset + 4);
    }

    // Unhinted, with sub-pixel placement, so glyph advances aren't rounded to whole pixels
    private static SKFont CreateFont(float size) => new(_typeface ?? SKTypeface.Default, size) { Hinting = SKFontHinting.None, Subpixel = true };

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        SKBitmap bitmap = new(width, height);
        bitmap.Erase(color);
        return bitmap;
    }

    private static SKBitmap Resized(SKBitmap source, int width, int height) =>
        source.Resize(new SKImageInfo(width, height), Sampling) ?? throw new InvalidOperationException($"Could not resize the image to {width}x{height}");

    private static async Task<SKBitmap> LoadArtworkAsync(string artworkUrl, ITrackPrefetchService? prefetchService)
    {
        try
        {
            // Check the prefetch cache first, otherwise download the artwork straight to memory
            byte[] imageBytes = prefetchService?.GetCachedArtwork(artworkUrl) ?? await _httpClient!.DownloadBytesAsync(artworkUrl);
            return SKBitmap.Decode(imageBytes) ?? throw new InvalidDataException("The artwork is not a supported image");
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to download artwork from {artworkUrl}: {ex.Message}");
            // Use a flat grey square if the artwork can't be fetched or decoded
            return Solid(400, 400, SKColors.DarkGray);
        }
    }
}
