using PlexBot.Utils.Http;

using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Font = SixLabors.Fonts.Font;

namespace PlexBot.Utils;

/// <summary>
/// Utility class for building player images with album art and track information.
/// Creates visually appealing player images by downloading album artwork, overlaying
/// track information, and applying visual effects like shadows and rounded corners.
/// </summary>
public static class ImageBuilder
{
    private static readonly HttpClientWrapper _httpClientWrapper;
    public static Font Font = new(SystemFonts.Get("Arial"), 36); // Fallback font if custom font fails

    static ImageBuilder()
    {
        // Initialize HttpClientWrapper with a long-lived HttpClient
        HttpClient httpClient = new();
        _httpClientWrapper = new HttpClientWrapper(httpClient, "ArtworkService");
        // Initialize font
        try
        {
            string fontPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Moderniz.otf");
            if (File.Exists(fontPath))
            {
                FontCollection fontCollection = new();
                using FileStream fontStream = new(fontPath, FileMode.Open, FileAccess.Read);
                FontFamily fontFamily = fontCollection.Add(fontStream);
                Font = fontFamily.CreateFont(36);
                Logs.Init($"Font loaded: {Font.Name}");
            }
            else
            {
                Logs.Warning($"Font file not found at {fontPath}, will use fallback font");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to load font: {ex.Message}");
        }
    }

    /// <summary>Builds a player image for a track.
    /// Downloads the album artwork, applies visual effects, and overlays track information.</summary>
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
                artworkUrl = "https://via.placeholder.com/800x400?text=No+Artwork";
            }
            // Download the image
            Image<Rgba32> image;
            try
            {
                byte[] imageBytes = await _httpClientWrapper.SendRequestForStringAsync(HttpMethod.Get, artworkUrl, null, null,
                    CancellationToken.None).ContinueWith(t => Convert.FromBase64String(Convert.ToBase64String(Encoding.UTF8.GetBytes(t.Result))));
                using MemoryStream imageStream = new(imageBytes);
                image = Image.Load<Rgba32>(imageStream);
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to download artwork from {artworkUrl}: {ex.Message}");
                // Create a blank image if download fails
                image = new Image<Rgba32>(800, 400, Color.DarkGray);
            }
            // Process the image
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
                    ctx.Fill(
                        new DrawingOptions
                        {
                            GraphicsOptions = new GraphicsOptions
                            {
                                BlendPercentage = 0.5f,
                                ColorBlendingMode = PixelColorBlendingMode.Normal,
                                AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver
                            }
                        },
                        Color.FromRgba(0, 0, 0, 150) // Semi-transparent black
                    );
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
                // Apply rounded corners
                IPath roundedRectPath = CreateRoundedRectanglePath(new RectangleF(0, 0, 800, 400), 20);
                image.Mutate(ctx =>
                {
                    ctx.SetGraphicsOptions(new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.DestIn });
                    ctx.Fill(Color.Black, roundedRectPath);
                });
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