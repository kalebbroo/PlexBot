using Discord;
using Discord.WebSocket;
using PlexBot.Core.Services.LavaLink;
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

        // Registry of all flags
        private static readonly Dictionary<string, ButtonFlag> _registry = new()
        {
            { "None", None },
            { "VisualPlayer", VisualPlayer },
            { "QueueOptions", QueueOptions },
            { "PlaylistOptions", PlaylistOptions }
        };

        // Bit position tracking for dynamic registration
        private static int _nextBitPosition = 3; // Start after pre-defined flags

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
        public VisualPlayer VisualPlayer;
        public CustomLavaLinkPlayer Player;
        public IDiscordInteraction Interaction;
        public Dictionary<string, object> CustomData = [];
    }

    /// <summary>Delegate for creating button builders with context</summary>
    public delegate ButtonBuilder ButtonFactory(ButtonContext context);

    /// <summary>Central management system for Discord buttons</summary>
    public class DiscordButtonBuilder
    {
        private static readonly Lazy<DiscordButtonBuilder> _instance = new(() => new DiscordButtonBuilder());
        public static DiscordButtonBuilder Instance => _instance.Value;

        private readonly Dictionary<string, (ButtonFlag Flags, int Priority, ButtonFactory Factory)> _buttonFactories = [];

        private DiscordButtonBuilder()
        {
            RegisterDefaultButtons();
        }

        /// <summary>Registers the default set of buttons used by the core application</summary>
        private void RegisterDefaultButtons()
        {
            // Player control buttons
            RegisterButton("pause_resume", ButtonFlag.VisualPlayer, 10, context =>
            {
                bool isPaused = context.Player?.State == PlayerState.Paused;
                return new ButtonBuilder()
                    .WithLabel(isPaused ? "Resume" : "Pause")
                    .WithCustomId(isPaused ? "pause_resume:resume" : "pause_resume:pause")
                    .WithStyle(isPaused ? ButtonStyle.Success : ButtonStyle.Secondary);
            });
            RegisterButton("skip", ButtonFlag.VisualPlayer, 20, _ =>
            {
                return new ButtonBuilder()
                    .WithLabel("Skip")
                    .WithCustomId("skip:skip")
                    .WithStyle(ButtonStyle.Primary);
            });
            RegisterButton("queue_options", ButtonFlag.VisualPlayer, 30, _ =>
            {
                return new ButtonBuilder()
                    .WithLabel("Queue Options")
                    .WithCustomId("queue_options:options:1")
                    .WithStyle(ButtonStyle.Success);
            });
            RegisterButton("repeat", ButtonFlag.VisualPlayer, 40, _ =>
            {
                return new ButtonBuilder()
                    .WithLabel("Repeat")
                    .WithCustomId("repeat:select")
                    .WithStyle(ButtonStyle.Secondary);
            });
            RegisterButton("volume", ButtonFlag.VisualPlayer, 50, _ =>
            {
                return new ButtonBuilder()
                    .WithLabel("Volume")
                    .WithCustomId("volume:select")
                    .WithStyle(ButtonStyle.Secondary);
            });
            RegisterButton("kill", ButtonFlag.VisualPlayer, 60, _ =>
            {
                return new ButtonBuilder()
                    .WithLabel("Kill")
                    .WithCustomId("kill:kill")
                    .WithStyle(ButtonStyle.Danger);
            });
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
            RegisterButton("back_to_player", ButtonFlag.QueueOptions, 40, context => {
                int currentPage = 1;
                if (context.CustomData.TryGetValue("currentPage", out var page) && page is int pageNum)
                {
                    currentPage = pageNum;
                }
                return new ButtonBuilder()
                    .WithLabel("Back")
                    .WithCustomId($"queue_options:back:{currentPage}")
                    .WithStyle(ButtonStyle.Secondary);
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
            bool isNew = !_buttonFactories.ContainsKey(id);
            _buttonFactories[id] = (flags, priority, factory);

            Logs.Debug($"Button {(isNew ? "registered" : "updated")}: {id} with flags {flags} and priority {priority}");
            return isNew;
        }

        /// <summary>Unregisters a button by its ID</summary>
        /// <param name="id">The button ID to remove</param>
        /// <returns>True if button was found and removed, otherwise false</returns>
        public bool UnregisterButton(string id)
        {
            bool result = _buttonFactories.Remove(id);
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
        public ComponentBuilder BuildButtons(ButtonFlag flags, ButtonContext context = null)
        {
            context ??= new ButtonContext();
            ComponentBuilder components = new();
            try
            {
                // Get button factories that match the flags
                var factories = _buttonFactories
                    .Where(kv => kv.Value.Flags.HasFlag(flags))
                    .OrderBy(kv => kv.Value.Priority)
                    .ToList();
                Logs.Debug($"Building components with flags {flags}, found {factories.Count} matching buttons");
                int rowCount = 0;
                int buttonCount = 0;
                foreach (var factory in factories)
                {
                    if (buttonCount >= 5)
                    {
                        buttonCount = 0;
                        rowCount++;

                        if (rowCount >= 5)
                        {
                            Logs.Warning($"Maximum number of button rows reached for flags {flags}. Some buttons will not be displayed.");
                            break;
                        }
                    }
                    try
                    {
                        ButtonBuilder button = factory.Value.Factory(context);
                        components.WithButton(button, rowCount);
                        buttonCount++;
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Error creating button {factory.Key}: {ex.Message}");
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
