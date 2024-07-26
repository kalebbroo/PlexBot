using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace PlexBot.Core.Players
{
    public class BuildImage
    {
        public static async Task<Image<Rgba64>> BuildPlayerImage(Dictionary<string, string> track)
        {
            string albumArtURL = track["Artwork"];
            Font font;
            try
            {
                // TODO: Make the font file part of env?
                string fontPath = "/app/Moderniz.otf";
                if (!File.Exists(fontPath))
                {
                    Console.WriteLine($"Font file '{fontPath}' does not exist.");
                    throw new FileNotFoundException($"Font file '{fontPath}' not found.");
                }
                FontCollection fontCollection = new FontCollection();
                using FileStream fontStream = new FileStream(fontPath, FileMode.Open, FileAccess.Read);
                FontFamily fontFamily = fontCollection.Add(fontStream);
                font = fontFamily.CreateFont(36);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Font loading error: {ex.Message}");
                throw;
            }
            Image<Rgba64> image;
            using (HttpClient httpClient = new())
            {
                byte[] imageBytes = await httpClient.GetByteArrayAsync(albumArtURL);
                using MemoryStream imageStream = new(imageBytes);
                image = Image.Load<Rgba64>(imageStream);
            }
            // Zoom in and crop the image to turn the square into a rectangle
            int cropWidth = image.Width / 2;
            int cropHeight = image.Height / 2;
            int cropX = (image.Width - cropWidth) / 2;
            int cropY = (image.Height - cropHeight) / 2;
            image.Mutate(ctx =>
            {
                ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight));
                ctx.Resize(800, 400); // Resize (adjust as needed)
                // Draw a semi-transparent black shadow to cover the album art. Makes the text more visible.
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
                ctx.DrawText(track.GetValueOrDefault("Artist", "Unknown Artist"), font, Color.White, new PointF(20, 20));
                ctx.DrawText(track.GetValueOrDefault("Title", "Unknown Title"), font, Color.White, new PointF(20, 70));
                ctx.DrawText(track.GetValueOrDefault("Album", "Unknown Album"), font, Color.White, new PointF(20, 120));
                ctx.DrawText(track.GetValueOrDefault("Studio", "Unknown Studio"), font, Color.White, new PointF(20, 170));
                ctx.DrawText(track.GetValueOrDefault("Progress", "0:00"), font, Color.White, new PointF(20, 220)); // TODO: Progress does not currently exist. Do something with this or remove it.
                ctx.DrawText(track.GetValueOrDefault("Duration", "0:00"), font, Color.White, new PointF(20, 270));
            });
            IPath roundedRectPath = CreateRoundedRectanglePath(new RectangleF(0, 0, 800, 400), 100); // Create the rounded edges
            image.Mutate(ctx =>
            {
                ctx.SetGraphicsOptions(new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.DestIn });
                ctx.Fill(Color.Black, roundedRectPath);
            });
            return image;
        }

        private static IPath CreateRoundedRectanglePath(RectangleF rect, float cornerRadius)
        {
            PathBuilder pathBuilder = new();
            pathBuilder.StartFigure();
            // Start at the top-left corner
            // Top-left corner arc
            pathBuilder.AddArc(new(rect.Left, rect.Top + cornerRadius), cornerRadius, cornerRadius, 45, false, true, new(rect.Left + cornerRadius, rect.Top));
            // Move to the top-right corner
            pathBuilder.AddLine(new(rect.Left + cornerRadius, rect.Top), new(rect.Right - cornerRadius, rect.Top));
            // Top-right corner arc
            pathBuilder.AddArc(new(rect.Right - cornerRadius, rect.Top), cornerRadius, cornerRadius, 45, false, true, new(rect.Right, rect.Top + cornerRadius));
            // Move to the bottom-right corner
            pathBuilder.AddLine(new(rect.Right, rect.Top + cornerRadius), new(rect.Right, rect.Bottom - cornerRadius));
            // Bottom-right corner arc
            pathBuilder.AddArc(new(rect.Right, rect.Bottom - cornerRadius), cornerRadius, cornerRadius, 45, false, true, new(rect.Right - cornerRadius, rect.Bottom));
            // Move to the bottom-left corner
            pathBuilder.AddLine(new(rect.Right - cornerRadius, rect.Bottom), new(rect.Left + cornerRadius, rect.Bottom));
            // Bottom-left corner arc
            pathBuilder.AddArc(new(rect.Left + cornerRadius, rect.Bottom), cornerRadius, cornerRadius, 45, false, true, new(rect.Left, rect.Bottom - cornerRadius));
            // Move to the top-left corner
            pathBuilder.AddLine(new(rect.Left, rect.Bottom - cornerRadius), new(rect.Left, rect.Top + cornerRadius));
            pathBuilder.CloseFigure();

            return pathBuilder.Build();
        }
    }
}
