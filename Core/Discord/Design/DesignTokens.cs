using Color = Discord.Color;

namespace PlexBot.Core.Discord.Design;

/// <summary>Accent colours shared by every Components V2 card. One place to change the look of a status or a surface.</summary>
public static class DesignTokens
{
    /// <summary>Successful action</summary>
    public static readonly Color Success = new(0, 255, 127);

    /// <summary>Failed action or error</summary>
    public static readonly Color Error = new(255, 69, 0);

    /// <summary>Neutral information, search results and help</summary>
    public static readonly Color Info = new(30, 144, 255);

    /// <summary>Something needs attention but did not fail</summary>
    public static readonly Color Warning = new(255, 215, 0);

    /// <summary>Music surfaces: the player, the queue and radio panels</summary>
    public static readonly Color Music = new(138, 43, 226);
}
