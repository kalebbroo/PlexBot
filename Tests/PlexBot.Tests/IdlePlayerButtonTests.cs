using Discord;
using PlexBot.Core.Discord.Design;
using PlexBot.Core.Discord.Embeds;
using Xunit;

namespace PlexBot.Tests;

public class IdlePlayerButtonTests
{
    private static List<string> CustomIds(ButtonFlag flags) =>
        new DiscordButtonBuilder(new EmojiRegistry()).BuildButtons(flags).ActionRows
            .SelectMany(row => row.Components)
            .OfType<ButtonBuilder>()
            .Select(button => button.CustomId)
            .ToList();

    [Fact]
    public void IdleCard_OffersSearchAndRadioOnly()
    {
        Assert.Equal(new[] { "idle:search", "idle:radio" }, CustomIds(ButtonFlag.Idle));
    }

    [Fact]
    public void PlayingCard_HasNoIdleButtons()
    {
        List<string> ids = CustomIds(ButtonFlag.VisualPlayer);

        Assert.DoesNotContain("idle:search", ids);
        Assert.DoesNotContain("idle:radio", ids);
        Assert.Contains("sonic:adventure", ids);
    }
}
