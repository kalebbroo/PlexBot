using PlexBot.Utils.Http;

using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Font = SixLabors.Fonts.Font;

namespace PlexBot.Utils;

/// <summary>Provides utilities for generating rich media player images with album art, track information, and visual effects for Discord embeds</summary>
public static class ImageBuilder
{
    private static readonly HttpClient _httpClient; // TODO: Use IHttpClientFactory
    public static Font Font;

    static ImageBuilder()
    {
        try
        {
            Logs.Debug("ImageBuilder initialization started");

            // Step 1: Initialize HttpClient
            try
            {
                _httpClient = new HttpClient();
                Logs.Debug("HttpClient initialized successfully");
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to initialize HttpClient: {ex.Message}");
                // Rethrow to stop initialization if we can't have an HttpClient
                throw new InvalidOperationException("Failed to initialize essential HttpClient", ex);
            }

            // Step 2: Initialize Font with a safe default first
            try
            {
                // Try to get Arial or any default system font
                var systemFontCollection = SystemFonts.Collection;
                if (systemFontCollection.TryGet("Arial", out var arialFamily))
                {
                    Font = arialFamily.CreateFont(36);
                    Logs.Debug("Default Arial font initialized");
                }
                else
                {
                    // Get the first available font
                    var firstFont = systemFontCollection.Families.FirstOrDefault();
                    if (firstFont != null)
                    {
                        Font = firstFont.CreateFont(36);
                        Logs.Debug($"Fallback to system font: {firstFont.Name}");
                    }
                    else
                    {
                        Logs.Error("No system fonts available!");
                        throw new InvalidOperationException("No system fonts available");
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to initialize default font: {ex.Message}");
                throw new InvalidOperationException("Cannot initialize any fonts", ex);
            }

            // Step 3: Try loading a custom font (optional - won't break if fails)
            try
            {
                string fontPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Moderniz.otf");
                Logs.Debug($"Looking for custom font at: {fontPath}");

                if (File.Exists(fontPath))
                {
                    Logs.Debug("Custom font file found, attempting to load");
                    var fontCollection = new FontCollection();
                    var family = fontCollection.Add(fontPath);
                    Font = family.CreateFont(36);
                    Logs.Debug($"Custom font loaded successfully: {Font.Name}");
                }
                else
                {
                    Logs.Debug("Custom font file not found, using system font");
                }
            }
            catch (Exception ex)
            {
                // Just log this error but continue with the system font
                Logs.Warning($"Failed to load custom font: {ex.Message}");
            }
            Logs.Debug("ImageBuilder initialized successfully");
        }
        catch (Exception ex)
        {
            Logs.Error($"CRITICAL ERROR in ImageBuilder initialization: {ex.Message}");
            Logs.Error($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Logs.Error($"Inner exception: {ex.InnerException.Message}");
                Logs.Error($"Inner stack trace: {ex.InnerException.StackTrace}");
            }
            // We can't throw here because it would prevent the application from starting,
            // but the image generation functionality won't work
            Logs.Error("ImageBuilder failed to initialize properly. Image generation will be unavailable.");
        }
    }

    /// <summary>Creates a visually appealing player image by downloading album art and overlaying track details for Discord display</summary>
    /// <param name="track">Dictionary containing track information</param>
    /// <returns>The generated image</returns>
    public static async Task<Image> BuildPlayerImageAsync(Dictionary<string, string> track)
    {
        try
        {
            // Get the album artwork URL
            string artworkUrl = track.GetValueOrDefault("Artwork", "");
            if (string.IsNullOrEmpty(artworkUrl) || artworkUrl == "N/A")
            {
                // Use a placeholder image if no artwork is available
                artworkUrl = "https://t3.ftcdn.net/jpg/06/04/96/54/360_F_604965492_lCfxDUwNF1YiogR3SN0lbmbdvFnfDCHa.jpg";
            }
            Image<Rgba32> image;
            try
            {
                // Direct download using HttpClient
                byte[] imageBytes = await _httpClient.GetByteArrayAsync(artworkUrl);
                using MemoryStream imageStream = new(imageBytes);
                image = Image.Load<Rgba32>(imageStream);
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to download artwork from {artworkUrl}: {ex.Message}");
                // Create a blank image if download fails
                image = new Image<Rgba32>(800, 400, Color.DarkGray);
            }
            try
            {
                // Resize if necessary to ensure we have proper dimensions
                if (image.Width != image.Height)
                {
                    // Crop to square from center
                    int size = Math.Min(image.Width, image.Height);
                    int x = (image.Width - size) / 2;
                    int y = (image.Height - size) / 2;
                    image.Mutate(ctx => ctx.Crop(new Rectangle(x, y, size, size)));
                }
                // Resize to target dimensions
                image.Mutate(ctx => ctx.Resize(800, 400));
                // Add semi-transparent overlay for better text visibility
                image.Mutate(ctx =>
                {
                    // Simplified overlay approach
                    ctx.Fill(Color.FromRgba(0, 0, 0, 150), new RectangleF(0, 0, 800, 400));
                    // Add track information
                    float y = 20;
                    ctx.DrawText(track.GetValueOrDefault("Artist", "Unknown Artist"), Font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText(track.GetValueOrDefault("Title", "Unknown Title"), Font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText(track.GetValueOrDefault("Album", "Unknown Album"), Font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText(track.GetValueOrDefault("Studio", "Unknown Studio"), Font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText("Duration: " + track.GetValueOrDefault("Duration", "0:00"), Font, Color.White, new PointF(20, y));
                });
                // Apply rounded corners - This was a bitch to get right
                try
                {
                    IPath roundedRectPath = CreateRoundedRectanglePath(new RectangleF(0, 0, 800, 400), 20);
                    image.Mutate(ctx =>
                    {
                        ctx.SetGraphicsOptions(new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.DestIn });
                        ctx.Fill(Color.Black, roundedRectPath);
                    });
                }
                catch (Exception ex)
                {
                    Logs.Warning($"Failed to apply rounded corners: {ex.Message}");
                    // Continue without rounded corners.....
                }
                return image;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to process image: {ex.Message}");
                return image; // Return unprocessed image
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to build player image: {ex.Message}");
            // Create and return a fallback image
            Image<Rgba32> fallbackImage = new(800, 400, Color.DarkGray);
            return fallbackImage;
        }
    }

    /// <summary>Creates a rounded rectangle path for image clipping.
    /// Used to apply rounded corners to the player image.</summary>
    /// <param name="rect">The rectangle to round</param>
    /// <param name="cornerRadius">The radius of the rounded corners</param>
    /// <returns>A path representing a rounded rectangle</returns>
    private static IPath CreateRoundedRectanglePath(RectangleF rect, float cornerRadius)
    {
        // Create a path builder for the rounded rectangle
        PathBuilder pathBuilder = new();
        pathBuilder.StartFigure();
        // Define the corners
        pathBuilder.AddArc(new(rect.Left, rect.Top), cornerRadius, cornerRadius, 0, 0, 90);
        pathBuilder.AddArc(new(rect.Right - cornerRadius * 2, rect.Top), cornerRadius, cornerRadius, 90, 0, 90);
        pathBuilder.AddArc(new(rect.Right - cornerRadius * 2, rect.Bottom - cornerRadius * 2), cornerRadius, cornerRadius, 180, 0, 90);
        pathBuilder.AddArc(new(rect.Left, rect.Bottom - cornerRadius * 2), cornerRadius, cornerRadius, 270, 0, 90);
        pathBuilder.CloseAllFigures();
        return pathBuilder.Build();
    }
}