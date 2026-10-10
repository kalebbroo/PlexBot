using Lavalink4NET.Players.Queued;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils;
using SkiaSharp;
using Xunit;

namespace PlexBot.Tests;

public class TruncateTests
{
    private static float TenPixelsPerCharacter(string text) => text.Length * 10f;

    [Fact]
    public void TruncateToWidth_KeepsTextThatFits()
    {
        Assert.Equal("Short", ImageBuilder.TruncateToWidth("Short", TenPixelsPerCharacter, 100));
    }

    [Fact]
    public void TruncateToWidth_EndsWithTheLongestPrefixThatFitsPlusAnEllipsis()
    {
        Assert.Equal("Hel...", ImageBuilder.TruncateToWidth("Hello World", TenPixelsPerCharacter, 60));
    }

    [Fact]
    public void TruncateToWidth_ReturnsOnlyTheEllipsisWhenNothingFits()
    {
        Assert.Equal("...", ImageBuilder.TruncateToWidth("Hello", TenPixelsPerCharacter, 5));
    }
}

public class PlayerImageTests
{
    private sealed class StubPrefetch(byte[] artwork) : ITrackPrefetchService
    {
        public Task PrefetchNextAsync(QueuedLavalinkPlayer player, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public byte[]? GetCachedArtwork(string artworkUrl) => artwork;
    }

    [Fact]
    public async Task PlayerPng_IsARoundedCardWithTheArtworkDrawn()
    {
        CustomTrackQueueItem track = new()
        {
            SourceTrack = new Track
            {
                Title = "Killer Queen",
                Artist = "Queen",
                Album = "Sheer Heart Attack",
                ArtworkUrl = "https://example.invalid/art.png",
                DurationDisplay = "3:00",
            },
            RequestedBy = "Tester",
        };

        using MemoryStream png = await ImageBuilder.BuildPlayerPngAsync(track, null, null, new StubPrefetch(MakeArtwork()));
        using SKBitmap card = SKBitmap.Decode(png.ToArray());

        Assert.Equal(800, card.Width);
        Assert.Equal(400, card.Height);
        // The corner is cut off, and the middle of the card is solid
        Assert.Equal(0, card.GetPixel(0, 0).Alpha);
        Assert.Equal(255, card.GetPixel(400, 200).Alpha);
        // The artwork sits at (40, 60), 280 pixels square, and is drawn rather than left black
        SKColor artPixel = card.GetPixel(180, 200);
        Assert.True(artPixel.Red + artPixel.Green + artPixel.Blue > 0);
    }

    // A bright gradient, so the artwork region can't be mistaken for an empty canvas
    private static byte[] MakeArtwork()
    {
        using SKBitmap art = new(300, 300);
        using SKCanvas canvas = new(art);
        using SKShader gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(300, 300),
            [new SKColor(255, 140, 0), new SKColor(200, 30, 60)],
            SKShaderTileMode.Clamp);
        using SKPaint paint = new() { Shader = gradient };
        canvas.DrawRect(0, 0, 300, 300, paint);
        using SKData png = art.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
