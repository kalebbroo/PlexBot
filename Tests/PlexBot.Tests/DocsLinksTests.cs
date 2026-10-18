using PlexBot.Core.Discord.Messages;
using Xunit;

namespace PlexBot.Tests;

/// <summary>Checks the troubleshooting anchors in DocsLinks against the headings in the guide. The guide is read from the
/// repository, so a renamed heading fails here instead of sending users to a dead link.</summary>
public class DocsLinksTests
{
    [Theory]
    [InlineData("Connection Issues", "connection-issues")]
    [InlineData("Unable to connect to Lavalink node", "unable-to-connect-to-lavalink-node")]
    [InlineData("\"Track Skipped\" — Plex Didn't Return the File", "track-skipped--plex-didnt-return-the-file")]
    public void Slug_FollowsGitHubHeadingRules(string heading, string expected)
    {
        Assert.Equal(expected, Slug(heading));
    }

    [Fact]
    public void GuideHeadings_IncludeTheTrackSkippedSlug()
    {
        Assert.Contains("track-skipped--plex-didnt-return-the-file", HeadingSlugs());
    }

    [Fact]
    public void EveryDocsLink_PointsAtAHeadingInTheGuide()
    {
        HashSet<string> slugs = HeadingSlugs();
        foreach (string link in new[] { DocsLinks.LavalinkConnection, DocsLinks.TrackLoadFailures, DocsLinks.TrackSkipped, DocsLinks.PlexServer })
        {
            Assert.StartsWith(DocsLinks.BaseUrl, link);
            int hash = link.IndexOf('#');
            Assert.True(hash > 0, $"{link} has no anchor");
            Assert.Contains(link[(hash + 1)..], slugs);
        }
    }

    /// <summary>GitHub's anchor for a heading: lower case, letters, digits, hyphens and underscores kept, each space a hyphen.
    /// Every other character, including quotes, apostrophes and dashes, is dropped, so a dash between spaces leaves a double hyphen.</summary>
    private static string Slug(string heading)
    {
        var slug = new System.Text.StringBuilder();
        foreach (char c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
                slug.Append(c);
            else if (c == ' ')
                slug.Append('-');
        }
        return slug.ToString();
    }

    private static HashSet<string> HeadingSlugs()
    {
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        bool inFence = false;
        foreach (string line in File.ReadAllLines(GuidePath()))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence || !line.StartsWith('#'))
                continue;
            slugs.Add(Slug(line.TrimStart('#').Trim()));
        }
        return slugs;
    }

    private static string GuidePath()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "Docs", "Guides", "Troubleshooting.md");
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException("Docs/Guides/Troubleshooting.md was not found above the test output folder");
    }
}
