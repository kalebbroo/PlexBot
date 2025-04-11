using PlexBot.Utils.Http;

using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Font = SixLabors.Fonts.Font;

namespace PlexBot.Utils;

/// <summary>Provides utilities for generating rich media player images with album art, track information, and visual effects for Discord embeds</summary>
public static class ImageBuilder
{
    private static readonly HttpClientWrapper? _httpClient;

    static ImageBuilder()
    {
        try
        {
            Logs.Debug("ImageBuilder initialization started");
            // Step 1: Initialize HttpClientWrapper
            try
            {
                // Create a standard HttpClient for the wrapper
                HttpClient client = new();
                _httpClient = new HttpClientWrapper(client, "ImageBuilder");
                Logs.Debug("HttpClientWrapper initialized successfully");
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to initialize HttpClientWrapper: {ex.Message}");
                // Rethrow to stop initialization if we can't have an HttpClient
                throw new InvalidOperationException("Failed to initialize essential HttpClientWrapper", ex);
            }
            
            Logs.Debug("ImageBuilder initialized successfully");
        }
        catch (Exception)
        {
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
                // Use HttpClientWrapper to download the image
                string tempFilePath = System.IO.Path.GetTempFileName();
                await _httpClient.DownloadFileAsync(artworkUrl, tempFilePath);
                // Load the image from the temp file
                image = Image.Load<Rgba32>(tempFilePath);
                // Delete the temp file
                try { File.Delete(tempFilePath); } catch { /* Ignore cleanup errors */ }
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
                    
                    // Simple font handling - just create a font when needed
                    Font font;
                    try
                    {
                        // Get any available system font - super simple
                        IReadOnlySystemFontCollection systemFontCollection = SystemFonts.Collection;
                        var fontFamilies = systemFontCollection.Families.ToList();
                        
                        if (fontFamilies.Count > 0)
                        {
                            // Use the first system font available
                            font = fontFamilies[0].CreateFont(36);
                        }
                        else
                        {
                            Logs.Error("No system fonts available - cannot draw text");
                            return; // Skip text drawing
                        }
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Font error: {ex.Message}");
                        return; // Skip text drawing if font creation fails
                    }
                    
                    // Add track information with the font we just created
                    float y = 20;
                    ctx.DrawText(track.GetValueOrDefault("Artist", "Unknown Artist"), font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText(track.GetValueOrDefault("Title", "Unknown Title"), font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText(track.GetValueOrDefault("Album", "Unknown Album"), font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText(track.GetValueOrDefault("Studio", "Unknown Studio"), font, Color.White, new PointF(20, y));
                    y += 50;
                    ctx.DrawText("Duration: " + track.GetValueOrDefault("Duration", "0:00"), font, Color.White, new PointF(20, y));
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