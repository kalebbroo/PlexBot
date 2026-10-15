using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using PlexBot.Core.Discord.Design;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Core.Services.Music;
using PlexBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlexBot.Core.Discord.Embeds
{
    /// <summary>Dynamic flags system for categorizing buttons</summary>
    public class ButtonFlag
    {
        private readonly long _value;
        private readonly string _name;

        private ButtonFlag(long value, string name)
        {
            _value = value;
            _name = name;
        }

        // Pre-defined flags (core system)
        public static readonly ButtonFlag None = new(0, "None");
        public static readonly ButtonFlag VisualPlayer = new(1L << 0, "VisualPlayer");
        public static readonly ButtonFlag QueueOptions = new(1L << 1, "QueueOptions");
        public static readonly ButtonFlag PlaylistOptions = new(1L << 2, "PlaylistOptions");
        public static readonly ButtonFlag Idle = new(1L << 3, "Idle");
        // Play, Search and one search per provider. The idle card and, on a static channel, the playing card show them.
        public static readonly ButtonFlag Entry = new(1L << 4, "Entry");

        // Registry of all flags
        private static readonly Dictionary<string, ButtonFlag> _registry = new()
        {
            { "None", None },
            { "VisualPlayer", VisualPlayer },
            { "QueueOptions", QueueOptions },
            { "PlaylistOptions", PlaylistOptions },
            { "Idle", Idle },
            { "Entry", Entry }
        };

        // Bit position tracking for dynamic registration
        private static int _nextBitPosition = 5; // Start after pre-defined flags

        /// <summary>Register a new button flag</summary>
        /// <param name="name">Unique name for the flag</param>
        /// <returns>The newly created flag</returns>
        public static ButtonFlag Register(string name)
        {
            lock (_registry)
            {
                // Check if already registered
                if (_registry.TryGetValue(name, out ButtonFlag existingFlag))
                {
                    return existingFlag;
                }
                // Create new flag
                if (_nextBitPosition >= 63)
                {
                    throw new InvalidOperationException("Maximum number of button flags reached");
                }
                ButtonFlag flag = new(1L << _nextBitPosition, name);
                _nextBitPosition++;
                _registry.Add(name, flag);
                Logs.Debug($"New button flag registered: {name} with bit position {_nextBitPosition - 1}");
                return flag;
            }
        }

        /// <summary>Get a registered flag by name</summary>
        /// <param name="name">The flag name</param>
        /// <returns>The flag, or None if not found</returns>
        public static ButtonFlag GetByName(string name)
        {
            return _registry.TryGetValue(name, out ButtonFlag flag) ? flag : None;
        }

        /// <summary>Combine multiple flags</summary>
        public static ButtonFlag operator |(ButtonFlag a, ButtonFlag b) => new(a._value | b._value, $"{a._name}|{b._name}");

        /// <summary>Check if this flag contains another flag</summary>
        public bool HasFlag(ButtonFlag flag) => (_value & flag._value) == flag._value;

        /// <summary>Convert to string representation</summary>
        public override string ToString() => _name;
    }

    /// <summary>Context object for button creation that contains information needed for dynamic buttons</summary>
    public class ButtonContext
    {
        public VisualPlayer? VisualPlayer { get; set; }
        public CustomLavaLinkPlayer? Player { get; set; }
        public IDiscordInteraction? Interaction { get; set; }
        public Dictionary<string, object> CustomData { get; set; } = [];
    }

    /// <summary>Delegate for creating button builders with context</summary>
    public delegate ButtonBuilder ButtonFactory(ButtonContext context);

    /// <summary>Central management system for Discord buttons</summary>
    public class DiscordButtonBuilder
    {
        private readonly ConcurrentDictionary<string, (ButtonFlag Flags, int Priority, ButtonFactory Factory)> _buttonFactories = new();
        private readonly EmojiRegistry _emojis;
        private readonly MusicProviderRegistry _providers;

        /// <summary>One button as the layout sees it: which card it belongs to, where it sorts, and how to build it</summary>
        private readonly record struct ButtonEntry(string Id, ButtonFlag Flags, int Priority, ButtonFactory Factory);

        /// <summary>Priority of the provider search buttons. Providers with equal priority keep the registry's order.</summary>
        private const int ProviderSearchPriority = 82;

        public DiscordButtonBuilder(EmojiRegistry emojis, MusicProviderRegistry providers)
        {
            _emojis = emojis;
            _providers = providers;
            RegisterDefaultButtons();
        }

        /// <summary>Label, emoji and action for the pause button. Pure, so the state mapping can be tested.</summary>
        public static (string Label, string EmojiName, string Fallback, string Action) PauseLook(bool paused) =>
            paused
                ? ("Resume", "pb_play", "\u25B6\uFE0F", "pause_resume:resume")
                : ("Pause", "pb_pause", "\u23F8\uFE0F", "pause_resume:pause");

        /// <summary>Label, emoji and style for the repeat button. Repeat-one and repeat-all are highlighted.</summary>
        public static (string Label, string EmojiName, string Fallback, ButtonStyle Style) RepeatLook(TrackRepeatMode mode) => mode switch
        {
            TrackRepeatMode.Track => ("Repeat 1", "pb_repeat_track", "\uD83D\uDD02", ButtonStyle.Primary),
            TrackRepeatMode.Queue => ("Repeat All", "pb_repeat", "\uD83D\uDD01", ButtonStyle.Primary),
            _ => ("Repeat", "pb_repeat", "\uD83D\uDD01", ButtonStyle.Secondary),
        };

        private ButtonBuilder Button(string label, string emojiName, string fallback, string customId, ButtonStyle style) =>
            new ButtonBuilder()
                .WithEmote(_emojis.Resolve(emojiName, fallback))
                .WithLabel(label)
                .WithCustomId(customId)
                .WithStyle(style);

        /// <summary>A button with a plain unicode emoji, for buttons that have no application emoji to sync</summary>
        private static ButtonBuilder UnicodeButton(string label, string emoji, string customId, ButtonStyle style) =>
            new ButtonBuilder()
                .WithEmote(new Emoji(emoji))
                .WithLabel(label)
                .WithCustomId(customId)
                .WithStyle(style);

        /// <summary>Registers the default set of buttons used by the core application</summary>
        private void RegisterDefaultButtons()
        {
            // Rows are filled by priority, five buttons to a row: row 1 playback, row 2 volume and extras, row 3 stop.
            // The entry row (Play, Search, providers, priorities 80 to 82) sorts after Stop, so on a static channel it
            // fills the rest of row 3. Idle Radio and Adventure (90, 91) come after it on the idle card only.
            RegisterButton("previous", ButtonFlag.VisualPlayer, 5, _ =>
                Button("Back", "pb_previous", "\u23EE\uFE0F", "previous:back", ButtonStyle.Secondary));
            RegisterButton("pause_resume", ButtonFlag.VisualPlayer, 10, context =>
            {
                (string label, string emoji, string fallback, string action) = PauseLook(context.Player?.State == PlayerState.Paused);
                return Button(label, emoji, fallback, action, ButtonStyle.Secondary);
            });
            RegisterButton("skip", ButtonFlag.VisualPlayer, 20, _ =>
                Button("Skip", "pb_skip", "\u23ED\uFE0F", "skip:skip", ButtonStyle.Secondary));
            RegisterButton("repeat", ButtonFlag.VisualPlayer, 30, context =>
            {
                (string label, string emoji, string fallback, ButtonStyle style) = RepeatLook(context.Player?.RepeatMode ?? TrackRepeatMode.None);
                return Button(label, emoji, fallback, "repeat:cycle", style);
            });
            RegisterButton("shuffle", ButtonFlag.VisualPlayer, 40, _ =>
                Button("Shuffle", "pb_shuffle", "\uD83D\uDD00", "queue_options:shuffle:1", ButtonStyle.Secondary));
            RegisterButton("queue_options", ButtonFlag.VisualPlayer, 50, _ =>
                Button("Queue", "pb_queue", "\uD83D\uDCCB", "queue_options:options:1", ButtonStyle.Secondary));
            RegisterButton("vol_down", ButtonFlag.VisualPlayer, 60, _ =>
                Button("Vol -", "pb_volume_down", "\uD83D\uDD09", "volume:down", ButtonStyle.Secondary));
            RegisterButton("vol_up", ButtonFlag.VisualPlayer, 61, _ =>
                Button("Vol +", "pb_volume_up", "\uD83D\uDD0A", "volume:up", ButtonStyle.Secondary));
            RegisterButton("radio", ButtonFlag.VisualPlayer, 65, _ =>
                Button("Radio", "pb_radio", "\uD83D\uDCFB", "radio:start", ButtonStyle.Secondary));
            RegisterButton("similar", ButtonFlag.VisualPlayer, 66, _ =>
                Button("Similar", "pb_similar", "\uD83D\uDD0D", "sonic:similar", ButtonStyle.Secondary));
            RegisterButton("adventure", ButtonFlag.VisualPlayer, 67, _ =>
                Button("Adventure", "pb_adventure", "\uD83E\uDDED", "sonic:adventure", ButtonStyle.Secondary));
            RegisterButton("kill", ButtonFlag.VisualPlayer, 70, _ =>
                Button("Stop", "pb_stop", "\u23F9\uFE0F", "kill:kill", ButtonStyle.Danger));
            // Play and Search work on the idle card and on a static channel's playing card. Radio and Adventure need a
            // track name when nothing is playing, so they are idle-only. The playing card keeps its own Radio and Adventure.
            RegisterButton("entry_play", ButtonFlag.Idle | ButtonFlag.Entry, 80, _ =>
                UnicodeButton("Play", "\U0001F3B5", "entry:play", ButtonStyle.Primary));
            RegisterButton("entry_search", ButtonFlag.Idle | ButtonFlag.Entry, 81, _ =>
                Button("Search", "pb_search", "\U0001F50D", "entry:search", ButtonStyle.Primary));
            RegisterButton("idle_radio", ButtonFlag.Idle, 90, _ =>
                Button("Radio", "pb_radio", "\U0001F4FB", "idle:radio", ButtonStyle.Secondary));
            RegisterButton("idle_adventure", ButtonFlag.Idle, 91, _ =>
                Button("Adventure", "pb_adventure", "\U0001F9ED", "idle:adventure", ButtonStyle.Secondary));
            // Queue Options buttons
            RegisterButton("view_queue", ButtonFlag.QueueOptions, 10, context => {
                int currentPage = 1;
                // Get current page from context if available
                if (context.CustomData.TryGetValue("currentPage", out var page) && page is int pageNum)
                {
                    currentPage = pageNum;
                }
                return new ButtonBuilder()
                    .WithLabel("View Queue")
                    .WithCustomId($"queue_options:view:{currentPage}")
                    .WithStyle(ButtonStyle.Success);
            });
            RegisterButton("shuffle_queue", ButtonFlag.QueueOptions, 20, context => {
                int currentPage = 1;
                if (context.CustomData.TryGetValue("currentPage", out var page) && page is int pageNum)
                {
                    currentPage = pageNum;
                }
                return new ButtonBuilder()
                    .WithLabel("Shuffle")
                    .WithCustomId($"queue_options:shuffle:{currentPage}")
                    .WithStyle(ButtonStyle.Primary);
            });
            RegisterButton("clear_queue", ButtonFlag.QueueOptions, 30, context => {
                int currentPage = 1;
                if (context.CustomData.TryGetValue("currentPage", out var page) && page is int pageNum)
                {
                    currentPage = pageNum;
                }
                return new ButtonBuilder()
                    .WithLabel("Clear")
                    .WithCustomId($"queue_options:clear:{currentPage}")
                    .WithStyle(ButtonStyle.Danger);
            });
        }

        /// <summary>Registers a new button factory with the manager</summary>
        /// <param name="id">Unique identifier for the button</param>
        /// <param name="flags">Flags indicating which UI areas this button should appear in</param>
        /// <param name="priority">Order priority (lower numbers appear first)</param>
        /// <param name="factory">Factory function to create the button</param>
        /// <returns>True if button was registered, false if it replaced an existing button</returns>
        public bool RegisterButton(string id, ButtonFlag flags, int priority, ButtonFactory factory)
        {
            bool isNew = _buttonFactories.TryAdd(id, (flags, priority, factory));
            if (!isNew)
            {
                _buttonFactories[id] = (flags, priority, factory);
            }
            Logs.Debug($"Button {(isNew ? "registered" : "updated")}: {id} with flags {flags} and priority {priority}");
            return isNew;
        }

        /// <summary>Unregisters a button by its ID</summary>
        /// <param name="id">The button ID to remove</param>
        /// <returns>True if button was found and removed, otherwise false</returns>
        public bool UnregisterButton(string id)
        {
            bool result = _buttonFactories.TryRemove(id, out _);
            if (result)
            {
                Logs.Debug($"Button unregistered: {id}");
            }
            return result;
        }

        /// <summary>Builds a ComponentBuilder containing all buttons matching the specified flags</summary>
        /// <param name="flags">The button flags to include</param>
        /// <param name="context">Context object for button creation</param>
        /// <returns>A ComponentBuilder with all matching buttons arranged in rows</returns>
        public ComponentBuilder BuildButtons(ButtonFlag flags, ButtonContext context = null) =>
            Layout(flags.ToString(), entry => entry.Flags.HasFlag(flags), context);

        /// <summary>Builds the buttons that carry any one of the given flags, in a single priority order. BuildButtons
        /// needs every flag, so it cannot give a card two sets at once; this can.</summary>
        public ComponentBuilder BuildAnyButtons(ButtonContext context, params ButtonFlag[] anyOf) =>
            Layout(string.Join("|", anyOf.Select(flag => flag.ToString())),
                entry => anyOf.Any(flag => entry.Flags.HasFlag(flag)), context);

        /// <summary>The playing card's buttons. A static channel's card also carries the entry row (Play, Search and the
        /// provider searches) after the playback controls. Every playing card is built here, so the two cannot drift apart.</summary>
        public ComponentBuilder BuildPlayingButtons(ButtonContext context, bool staticChannel) =>
            staticChannel
                ? BuildAnyButtons(context, ButtonFlag.VisualPlayer, ButtonFlag.Entry)
                : BuildButtons(ButtonFlag.VisualPlayer, context);

        /// <summary>Every registered button, followed by one search button per provider. Provider buttons are made here,
        /// on each build, so a provider registered after the button builder was created is still shown.</summary>
        private IEnumerable<ButtonEntry> AllButtons() =>
            _buttonFactories
                .Select(kv => new ButtonEntry(kv.Key, kv.Value.Flags, kv.Value.Priority, kv.Value.Factory))
                .Concat(ProviderSearchButtons());

        /// <summary>One search button per available provider that can search. Plex is left out: its search is the
        /// Search button. A provider only gets a button when it is registered and available.</summary>
        private IEnumerable<ButtonEntry> ProviderSearchButtons() =>
            _providers.GetAvailableProviders()
                .Where(provider => provider.Capabilities.HasFlag(MusicProviderCapabilities.Search)
                    && !provider.Id.Equals("plex", StringComparison.OrdinalIgnoreCase))
                .Select(provider => new ButtonEntry(
                    $"provider_search:{provider.Id}",
                    ButtonFlag.Idle | ButtonFlag.Entry,
                    ProviderSearchPriority,
                    _ => UnicodeButton(Truncate(provider.DisplayName, 80), "\U0001F3AC",
                        $"provider:search:{provider.Id}", ButtonStyle.Secondary)));

        private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];

        /// <summary>Orders the buttons that pass the filter by priority and lays them out five to a row, up to five rows</summary>
        private ComponentBuilder Layout(string label, Func<ButtonEntry, bool> include, ButtonContext context)
        {
            context ??= new ButtonContext();
            ComponentBuilder components = new();
            try
            {
                var entries = AllButtons()
                    .Where(include)
                    .OrderBy(entry => entry.Priority)
                    .ToList();
                Logs.Debug($"Building components with flags {label}, found {entries.Count} matching buttons");
                int rowCount = 0;
                int buttonCount = 0;
                foreach (ButtonEntry entry in entries)
                {
                    if (buttonCount >= 5)
                    {
                        buttonCount = 0;
                        rowCount++;

                        if (rowCount >= 5)
                        {
                            Logs.Warning($"Maximum number of button rows reached for flags {label}. Some buttons will not be displayed.");
                            break;
                        }
                    }
                    try
                    {
                        ButtonBuilder button = entry.Factory(context);
                        components.WithButton(button, rowCount);
                        buttonCount++;
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Error creating button {entry.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error building components: {ex.Message}");
            }
            return components;
        }
    }
}
