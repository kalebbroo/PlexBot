using PlexBot.Utils.Http;

using Path = System.IO.Path;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Font = SixLabors.Fonts.Font;
using FontFamily = SixLabors.Fonts.FontFamily;
using PlexBot.Services.LavaLink;

namespace PlexBot.Utils;

/// <summary>Provides utilities for generating rich media player images with album art, track information, and visual effects for Discord embeds</summary>
public static class ImageBuilder
{
    private static readonly HttpClientWrapper? _httpClient;
    private static readonly FontFamily? _fontFamily;
    private static readonly FontCollection _fontCollection = new();
    private static readonly Dictionary<string, Image<Rgba32>> _iconCache = [];


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
                HttpClient client = new();
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
                // Try to find an appropriate font
                bool foundFont = false;
                foreach (string fontPath in _fontPaths)
                {
                    if (File.Exists(fontPath))
                    {
                        try
                        {
                            // Attempt to load the font
                            _fontCollection.Add(fontPath);
                            loadedFonts.Add(System.IO.Path.GetFileName(fontPath));
                            foundFont = true;
                            Logs.Debug($"Successfully loaded font: {fontPath}");
                        }
                        catch (Exception ex)
                        {
                            Logs.Warning($"Failed to load font {fontPath}: {ex.Message}");
                        }
                    }
                }
                if (foundFont)
                {
                    // Try to find a good CJK-compatible font first
                    string[] cjkFontNames = ["Noto", "CJK", "Gothic", "Mincho", "Apple"];
                    foreach (var family in _fontCollection.Families)
                    {
                        if (cjkFontNames.Any(cjk => family.Name.Contains(cjk, StringComparison.OrdinalIgnoreCase)))
                        {
                            _fontFamily = family;
                            Logs.Debug($"Selected CJK-compatible font: {family.Name}");
                            break;
                        }
                    }
                    // If no CJK font found, use any available font
                    if (_fontFamily == null && _fontCollection.Families.Any())
                    {

                        if (_fontFamily == null && _fontCollection.Families.Any())
                        {
                            _fontFamily = _fontCollection.Families.First();
                            Logs.Debug($"Selected font: {_fontFamily.Value.Name}");
                        }
                    }
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

    /// <summary>Creates a visually appealing player image by downloading album art and overlaying track details for Discord display</summary>
    /// <param name="track">Dictionary containing track information</param>
    /// <param name="player">Optional player object to get current state (volume and repeat mode)</param>
    /// <returns>The generated image</returns>
    /// <summary>Creates a visually appealing player image by downloading album art and overlaying track details</summary>
    public static async Task<Image> BuildPlayerImageAsync(CustomTrackQueueItem track, CustomLavaLinkPlayer player = null)
    {
        try
        {
            int volumePercent = 20; // Default value
            string repeatMode = "none"; // Default value
            if (player != null)
            {
                volumePercent = (int)(player.Volume * 100);
                repeatMode = player.RepeatMode.ToString().ToLower();
                Logs.Debug($"Using player state - Volume: {volumePercent}%, Repeat: {repeatMode}");
            }
            // Get the album artwork URL
            string artworkUrl = track.Artwork ?? "";
            if (string.IsNullOrEmpty(artworkUrl) || artworkUrl == "N/A")
            {
                artworkUrl = "https://via.placeholder.com/150"; // TODO: Add a real placeholder image
            }
            Image<Rgba32> albumArt;
            try
            {
                // Use HttpClientWrapper to download the image
                string tempFilePath = Path.GetTempFileName();
                await _httpClient.DownloadFileAsync(artworkUrl, tempFilePath);
                // Load the image from the temp file
                albumArt = Image.Load<Rgba32>(tempFilePath);
                // Delete the temp file
                try { File.Delete(tempFilePath); } catch { /* Ignore cleanup errors */ }
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to download artwork from {artworkUrl}: {ex.Message}");
                // Create a blank image if download fails
                albumArt = new Image<Rgba32>(400, 400, Color.DarkGray);
            }
            // Final image dimensions
            int width = 800;
            int height = 400;
            // Create our canvas
            Image<Rgba32> canvas = new(width, height, Color.Black);
            try
            {
                // Create a blurred copy of the album art for background
                Image<Rgba32> backgroundArt = albumArt.Clone();
                backgroundArt.Mutate(ctx =>
                {
                    // Resize to fill the background
                    ctx.Resize(new Size(width + 100, height + 100));
                    // Blur the image
                    ctx.GaussianBlur(10f);
                });
                // Draw blurred background
                canvas.Mutate(ctx => ctx.DrawImage(backgroundArt, new Point(-50, -50), 1f));
                // Add a semi-transparent overlay for better text contrast and darkening
                canvas.Mutate(ctx =>
                {
                    // Create a darker overlay
                    ctx.Fill(new Rgba32(0, 0, 0, 180), new RectangleF(0, 0, width, height));
                    // Add gradient effect
                    ctx.Fill(new LinearGradientBrush(
                        new PointF(0, 0),
                        new PointF(width, height),
                        GradientRepetitionMode.None,
                        new ColorStop(0f, new Rgba32(0, 0, 0, 50)),
                        new ColorStop(1f, new Rgba32(0, 0, 0, 100))
                    ), new RectangleF(0, 0, width, height));
                });
                // Create a clean version of album art for display
                Image<Rgba32> displayArt = albumArt.Clone();
                displayArt.Mutate(ctx =>
                {
                    // Make it square if it's not already
                    if (displayArt.Width != displayArt.Height)
                    {
                        int size = Math.Min(displayArt.Width, displayArt.Height);
                        ctx.Crop(new Rectangle(
                            (displayArt.Width - size) / 2,
                            (displayArt.Height - size) / 2,
                            size, size));
                    }

                    // Resize to fit our layout
                    ctx.Resize(new Size(280, 280));
                });
                // Draw album art on left side
                canvas.Mutate(ctx => ctx.DrawImage(displayArt, new Point(40, 60), 1f));
                // Add text information
                try
                {
                    // Get the font for our text
                    Font titleFont, artistFont, infoFont, smallInfoFont;
                    if (_fontFamily != null)
                    {
                        // Create fonts of different sizes from our loaded font
                        titleFont = _fontFamily.Value.CreateFont(40, FontStyle.Bold);
                        artistFont = _fontFamily.Value.CreateFont(32);
                        infoFont = _fontFamily.Value.CreateFont(20);
                        smallInfoFont = _fontFamily.Value.CreateFont(16);
                    }
                    else
                    {
                        // Emergency fallback - use any available system font
                        FontFamily fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
                        if (fallbackFamily == null)
                        {
                            throw new Exception("No fonts available!");
                        }
                        titleFont = fallbackFamily.CreateFont(40, FontStyle.Bold);
                        artistFont = fallbackFamily.CreateFont(32);
                        infoFont = fallbackFamily.CreateFont(20);
                        smallInfoFont = fallbackFamily.CreateFont(16);
                    }
                    // Helper function to truncate text
                    static string TruncateText(string text, Font font, int maxWidth)
                    {
                        if (string.IsNullOrEmpty(text)) return text;
                        FontRectangle size = TextMeasurer.MeasureSize(text, new TextOptions(font));
                        if (size.Width <= maxWidth) return text;
                        // Truncate and add ellipsis
                        for (int i = text.Length - 1; i >= 0; i--)
                        {
                            string truncated = text[..i] + "...";
                            size = TextMeasurer.MeasureSize(truncated, new TextOptions(font));
                            if (size.Width <= maxWidth) return truncated;
                        }
                        return "...";
                    }
                    int textX = 360;  // Start text after the album art
                    int maxWidth = 400; // Maximum width for text
                    // Draw track information
                    canvas.Mutate(ctx =>
                    {
                        // Title
                        string title = track.Title ?? "Unknown Title";
                        string truncatedTitle = TruncateText(title, titleFont, maxWidth);
                        ctx.DrawText(truncatedTitle, titleFont, Color.White, new PointF(textX, 70));
                        // Artist
                        string artist = track.Artist ?? "Unknown Artist";
                        string truncatedArtist = TruncateText(artist, artistFont, maxWidth);
                        ctx.DrawText(truncatedArtist, artistFont, new Rgba32(220, 220, 220, 255), new PointF(textX, 130));
                        // Album
                        string album = track.Album ?? "Unknown Album";
                        string truncatedAlbum = TruncateText(album, infoFont, maxWidth);
                        ctx.DrawText(truncatedAlbum, infoFont, new Rgba32(180, 180, 180, 255), new PointF(textX, 190));
                        // Load and draw the time icon
                        Image timeIcon = GetIcon("time.png");
                        ctx.DrawImage(timeIcon, new Point(textX, 230), 1.0f);
                        // Calculate spacing based on icon size
                        int timeIconWidth = timeIcon.Width;
                        int durationTextX = textX + timeIconWidth + 8; // 8px spacing between icon and text
                        // Draw duration text
                        string duration = track.Duration ?? "00:00";
                        ctx.DrawText(duration, infoFont, new Rgba32(180, 180, 180, 255), new PointF(durationTextX, 230));
                        // Volume indicator with actual value from player
                        int volumeIndicatorY = 280;
                        DrawVolumeIndicator(ctx, textX, volumeIndicatorY, 200, 8, volumePercent, infoFont, smallInfoFont);
                        // Repeat mode indicator with actual mode from player
                        int repeatY = 320;
                        DrawRepeatIndicator(ctx, textX, repeatY, repeatMode, infoFont, smallInfoFont);
                        // Additional info/credit
                        if (!string.IsNullOrEmpty(track.Studio))
                        {
                            string credit = "Requested by: " + track.RequestedBy;
                            string truncatedCredit = TruncateText(credit, smallInfoFont, maxWidth);
                            ctx.DrawText(truncatedCredit, smallInfoFont, new Rgba32(150, 150, 150, 255), new PointF(textX - 200, 365)); // Changed from 340
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error adding text: {ex.Message}");
                }
                // Apply rounded corners
                try
                {
                    // Create a simple mask using multiple filled circles and rectangles
                    Image<Rgba32> mask = new(width, height, Color.Transparent);
                    mask.Mutate(ctx =>
                    {
                        // Fill the center
                        ctx.Fill(Color.White, new Rectangle(12, 0, width - 24, height));
                        ctx.Fill(Color.White, new Rectangle(0, 12, width, height - 24));
                        // Add the rounded corners with circles
                        int radius = 24; // Slightly larger radius for smoother corners
                        ctx.Fill(Color.White, new EllipsePolygon(radius, radius, radius));
                        ctx.Fill(Color.White, new EllipsePolygon(width - radius, radius, radius));
                        ctx.Fill(Color.White, new EllipsePolygon(radius, height - radius, radius));
                        ctx.Fill(Color.White, new EllipsePolygon(width - radius, height - radius, radius));
                    });
                    // Apply the mask
                    canvas.Mutate(ctx =>
                    {
                        ctx.SetGraphicsOptions(new GraphicsOptions
                        {
                            AlphaCompositionMode = PixelAlphaCompositionMode.DestIn
                        });
                        ctx.DrawImage(mask, new Point(0, 0), 1f);
                    });
                }
                catch (Exception ex)
                {
                    Logs.Warning($"Failed to apply rounded corners: {ex.Message}");
                }
                return canvas;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to process image: {ex.Message}");
                return new Image<Rgba32>(width, height, Color.Black); // Return simple fallback
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to build player image: {ex.Message}");
            // Create and return a fallback image
            return new Image<Rgba32>(800, 400, Color.Black);
        }
    }

    // Helper method to draw a rounded rectangle
    private static void DrawRoundedRectangle(IImageProcessingContext ctx, float x, float y, float width, float height, float radius, Color color, bool fill = true)
    {
        // Make sure radius isn't too large for the rectangle
        radius = Math.Min(radius, Math.Min(width / 2, height / 2));
        IPath path = new PathBuilder()
            .AddArc(new PointF(x + radius, y + radius), radius, radius, 0, 180, 90) // Top-left corner
            .AddLine(x + radius, y, x + width - radius, y) // Top edge
            .AddArc(new PointF(x + width - radius, y + radius), radius, radius, 0, 270, 90) // Top-right corner
            .AddLine(x + width, y + radius, x + width, y + height - radius) // Right edge
            .AddArc(new PointF(x + width - radius, y + height - radius), radius, radius, 0, 0, 90) // Bottom-right corner
            .AddLine(x + width - radius, y + height, x + radius, y + height) // Bottom edge
            .AddArc(new PointF(x + radius, y + height - radius), radius, radius, 0, 90, 90) // Bottom-left corner
            .AddLine(x, y + height - radius, x, y + radius) // Left edge
            .CloseFigure()
            .Build();
        // Fill or draw the rectangle
        if (fill)
        {
            ctx.Fill(color, path);
        }
        else
        {
            ctx.Draw(color, 1f, path);
        }
    }

    private static Image<Rgba32> GetIcon(string iconName)
    {
        // Check if the icon is already cached
        if (_iconCache.TryGetValue(iconName, out Image<Rgba32> cachedIcon))
        {
            return cachedIcon;
        }
        // Define possible paths where the icon might be located
        string[] possiblePaths =
        [
            Path.Combine("/app/Images/Icons", iconName),
            Path.Combine(AppContext.BaseDirectory, "Images/Icons", iconName),
            Path.Combine(Directory.GetCurrentDirectory(), "Images/Icons", iconName)
        ];
        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    Logs.Debug($"Loading icon from {path}");
                    Image<Rgba32> icon = Image.Load<Rgba32>(path);
                    _iconCache[iconName] = icon;
                    return icon;
                }
                catch (Exception ex)
                {
                    Logs.Error($"Failed to load icon from {path}: {ex.Message}");
                    // Continue to the next path
                }
            }
            else
            {
                Logs.Debug($"Icon path not found: {path}");
            }
        }
        // If we get here, we couldn't load the icon
        Logs.Error($"Could not find icon file: {iconName}");
        // Return a blank image
        return new Image<Rgba32>(24, 24, Color.Transparent);
    }

    private static void DrawVolumeIndicator(IImageProcessingContext ctx, int x, int y, int width, int height, int volumePercent, Font labelFont, Font valueFont)
    {
        // Ensure volume is between 0-100
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        // Load and draw the volume icon
        Image<Rgba32> icon = GetIcon("audio.png");
        ctx.DrawImage(icon, new Point(x, y), 1.0f);
        // Calculate spacing based on icon size
        int iconWidth = icon.Width;
        int textX = x + iconWidth + 8; // 8px spacing between icon and text
        // Draw volume label
        ctx.DrawText("Volume", labelFont, Color.White, new PointF(textX, y));
        // Draw the value text
        ctx.DrawText($"{volumePercent}%", valueFont, Color.White, new PointF(x + width + 10, y + 4));
        // Background track - with rounded corners
        int cornerRadius = height; // Make it fully rounded at the ends
        DrawRoundedRectangle(ctx, x, y + icon.Height + 6, width, height, cornerRadius, new Rgba32(80, 80, 80, 200), true);
        // Active volume level - with rounded corners
        float fillWidth = (width * volumePercent) / 100f;
        if (fillWidth > 0)
        {
            // Only draw if we have a non-zero width
            DrawRoundedRectangle(ctx, x, y + icon.Height + 6, (int)fillWidth, height, cornerRadius, new Rgba32(65, 105, 225, 255), true);
        }
    }

    private static void DrawRepeatIndicator(IImageProcessingContext ctx, int x, int y, string repeatMode, Font labelFont, Font valueFont)
    {
        Color indicatorColor = new Rgba32(120, 120, 120, 255);
        string displayText = "Off";
        int yOffset = 10; // Offset for the text position to give more padding between volume bar and repeat indicator
        // Determine the proper display based on repeat mode
        switch (repeatMode.ToLower())
        {
            case "track":
                displayText = "Track";
                indicatorColor = new Rgba32(65, 105, 225, 255);
                break;
            case "queue":
                displayText = "Queue";
                indicatorColor = new Rgba32(65, 105, 225, 255);
                break;
            case "none":
            default:
                // Default values already set
                break;
        }
        // Load and draw the repeat icon
        Image<Rgba32> icon = GetIcon("repeat.png");
        ctx.DrawImage(icon, new Point(x, y + yOffset), 1.0f);
        // Calculate spacing based on icon size
        int iconWidth = icon.Width;
        int textX = x + iconWidth + 8; // 8px spacing between icon and text
        ctx.DrawText(" Repeat", labelFont, Color.White, new PointF(textX, y + yOffset));
        // Draw mode text (position relative to the label)
        ctx.DrawText(displayText, valueFont, indicatorColor, new PointF(textX + 100, y + yOffset + 4));
    }
}