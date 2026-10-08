using System.Diagnostics;
using PlexBot.Utils;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Events;
using PlexBot.Core.Extensions;

namespace PlexBot.Core.Discord.Events;

/// <summary>Handles Discord events and interaction routing for slash commands and interactive components</summary>
/// <remarks>Initializes a new instance of the DiscordEventHandler class with necessary dependencies</remarks>
/// <param name="client">The Discord client for connecting to Discord's API</param>
/// <param name="interactions">The interaction service for handling slash commands</param>
/// <param name="services">The service provider for dependency injection</param>
public class DiscordEventHandler(DiscordSocketClient client, InteractionService interactions, IServiceProvider services)
{
    /// <summary>Short random ID for this process. Written into interaction logs so two bot instances
    /// sharing one token can be told apart in the logs.</summary>
    public static readonly string InstanceId = Guid.NewGuid().ToString("N")[..8];

    private int _setupStarted;
    private int _eventsWired;
    // Assemblies whose modules are already registered. Only failed loads are retried, so a retry never adds a
    // module definition twice (Discord.Net keeps earlier definitions when more are added).
    private readonly HashSet<Assembly> _loadedAssemblies = new();
    private readonly HashSet<ulong> _registeredGuilds = new();
    private bool _commandsRegistered;
    private bool _subscribed;

    /// <summary>Wires the Discord and interaction events. Runs once per process.</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    public Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _eventsWired, 1) == 1)
            return Task.CompletedTask;

        client.Log += LogAsync;
        interactions.Log += LogAsync;
        client.Ready += ReadyAsync;
        client.InteractionCreated += HandleInteractionAsync;

        // Commands run in async mode (off the gateway thread), so their failures never come back through
        // HandleInteractionAsync. These events are the central fallback: they send the user one response
        // when a handler threw before acknowledging, instead of Discord showing "application did not respond".
        interactions.SlashCommandExecuted += OnCommandExecutedAsync;
        interactions.ComponentCommandExecuted += OnCommandExecutedAsync;
        interactions.ModalCommandExecuted += OnCommandExecutedAsync;
        interactions.ContextCommandExecuted += OnCommandExecutedAsync;

        Logs.Init($"Discord event handlers initialized (instance {InstanceId})");
        return Task.CompletedTask;
    }

    /// <summary>Handles the client ready event. Ready fires again on every reconnect, so setup work runs only on the first one.</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    private Task ReadyAsync()
    {
        if (Interlocked.Exchange(ref _setupStarted, 1) == 1)
        {
            Logs.Info($"[{InstanceId}] Gateway ready again (reconnect); setup already done");
            return Task.CompletedTask;
        }

        // Off the gateway thread: command registration and status calls can take seconds, and while Ready is
        // being awaited the gateway cannot deliver interactions, which is what makes them time out.
        _ = Task.Run(SetupAfterReadyAsync);
        return Task.CompletedTask;
    }

    /// <summary>Runs setup until it fully succeeds. A failed stage (module load or command registration) is retried
    /// with capped backoff, rather than leaving the process without commands until a restart.</summary>
    private async Task SetupAfterReadyAsync()
    {
        TimeSpan delay = TimeSpan.FromSeconds(10);
        while (!await TrySetupAsync())
        {
            Logs.Warning($"[{InstanceId}] Setup incomplete; retrying in {delay.TotalSeconds:N0}s");
            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
        }
    }

    /// <summary>One pass of setup. Returns false if any stage failed, so the caller retries. Stages that already
    /// succeeded are not repeated, so a retry does not add modules twice.</summary>
    private async Task<bool> TrySetupAsync()
    {
        try
        {
            Assembly entry = Assembly.GetEntryAssembly()!;
            if (_loadedAssemblies.Add(entry))
            {
                try
                {
                    await interactions.AddModulesAsync(entry, services);
                }
                catch
                {
                    _loadedAssemblies.Remove(entry);
                    throw;
                }
            }

            ExtensionManager extensionManager = services.GetRequiredService<ExtensionManager>();
            foreach (Extension ext in extensionManager.GetAllExtensions())
            {
                if (ext.SourceAssembly == null || ext.SourceAssembly == entry || !_loadedAssemblies.Add(ext.SourceAssembly))
                    continue;
                try
                {
                    await interactions.AddModulesAsync(ext.SourceAssembly, services);
                    Logs.Info($"Registered commands from extension: {ext.Name}");
                }
                catch
                {
                    _loadedAssemblies.Remove(ext.SourceAssembly);
                    throw;
                }
            }

            foreach (ModuleInfo module in interactions.Modules)
            {
                Logs.Info($"Module: {module.Name}, Commands: {module.SlashCommands.Count}");
                foreach (SlashCommandInfo cmd in module.SlashCommands)
                    Logs.Info($"  Command: {cmd.Name}");
            }

            if (!_commandsRegistered)
                _commandsRegistered = await RegisterCommandsWithRetryAsync();
            if (!_commandsRegistered)
                return false;

            await client.SetGameAsync("/help", type: ActivityType.Listening);

            if (!_subscribed)
            {
                _subscribed = true;
                BotEventBus eventBus = services.GetRequiredService<BotEventBus>();
                if (BotConfig.GetBool("bot.showNowPlaying", true))
                {
                    eventBus.Subscribe(BotEvents.TrackStarted, async e =>
                    {
                        string title = e.Data.GetValueOrDefault("title") as string ?? "Unknown";
                        string artist = e.Data.GetValueOrDefault("artist") as string ?? "Unknown";
                        string status = artist != "Unknown" ? $"{artist} - {title}" : title;
                        // Discord truncates activity text at 128 chars
                        if (status.Length > 128) status = status[..125] + "...";
                        await client.SetGameAsync(status, type: ActivityType.Listening);
                    });
                    eventBus.Subscribe(BotEvents.TrackEnded, async _ => await client.SetGameAsync("/help", type: ActivityType.Listening));
                    eventBus.Subscribe(BotEvents.PlayerDestroyed, async _ => await client.SetGameAsync("/help", type: ActivityType.Listening));
                    Logs.Init("Rich presence enabled — bot status will show now-playing track");
                }

                // Not gated by showNowPlaying: the visual player must lose its controls whenever the player disconnects
                eventBus.Subscribe(BotEvents.PlayerDestroyed, async e =>
                    await services.GetRequiredService<VisualPlayer>().HandlePlayerDestroyedAsync((ulong)e.Data["guildId"]));
            }

            Logs.Init($"[{InstanceId}] Bot is ready. Connected to {client.Guilds.Count} guilds");

            _ = services.GetRequiredService<BotEventBus>().PublishAsync(new BotEvent
            {
                EventType = BotEvents.BotReady,
                Data = new Dictionary<string, object>
                {
                    ["guildCount"] = client.Guilds.Count
                }
            });
            return true;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in ready setup: {ex.Message}");
            Logs.Error($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>Registers slash commands, retrying each guild with backoff. Guilds are retried independently, so one
    /// guild that keeps rejecting registration does not stop the others from getting commands.</summary>
    private async Task<bool> RegisterCommandsWithRetryAsync()
    {
        bool allRegistered = true;
        if (BotConfig.GetString("bot.environment") == "Development")
        {
            // Guild-scoped registration is immediate; global can take up to an hour to appear
            foreach (SocketGuild guild in client.Guilds)
            {
                // Guilds that already registered are skipped on later setup retries, so one failing guild does not
                // re-overwrite the commands of every other guild
                if (_registeredGuilds.Contains(guild.Id))
                    continue;
                bool ok = await RetryAsync($"guild {guild.Name} ({guild.Id})", () => interactions.RegisterCommandsToGuildAsync(guild.Id));
                if (ok) _registeredGuilds.Add(guild.Id);
                allRegistered &= ok;
            }
        }
        else
        {
            allRegistered = await RetryAsync("global", () => interactions.RegisterCommandsGloballyAsync());
        }
        return allRegistered;
    }

    /// <summary>Runs one registration with up to five attempts and exponential backoff. Gives up on this target only.</summary>
    private static async Task<bool> RetryAsync(string target, Func<Task> register)
    {
        TimeSpan delay = TimeSpan.FromSeconds(5);
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await register();
                Logs.Info($"Registered commands ({target})");
                return true;
            }
            catch (Exception ex)
            {
                Logs.Error($"Command registration failed for {target} (attempt {attempt}/5): {ex.Message}");
                if (attempt == 5) return false;
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 120));
            }
        }
        return false;
    }

    /// <summary>Routes incoming interactions to appropriate handlers and manages error responses</summary>
    /// <param name="interaction">The interaction to handle from Discord</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // Host-clock estimate only: CreatedAt comes from the snowflake, and the host clock may drift.
            // Discord enforces the 3-second window on its side, so this is for orientation, not the deadline.
            TimeSpan elapsed = DateTimeOffset.UtcNow - interaction.CreatedAt;
            Logs.Debug($"[{InstanceId}] Interaction received: type={interaction.Type}, host-clock elapsed={elapsed.TotalMilliseconds:F0}ms");

            SocketInteractionContext context = new(client, interaction);
            IResult result = await interactions.ExecuteCommandAsync(context, services);

            if (!result.IsSuccess)
            {
                Logs.Warning($"[{InstanceId}] Interaction failed: {result.Error} - {result.ErrorReason}");

                // Autocomplete interactions cannot receive component/embed responses.
                // Superseded autocomplete interactions (user kept typing) fail with 40060 — normal.
                if (interaction is SocketAutocompleteInteraction)
                    return;

                var errorComponents = result.Error.HasValue
                    ? ComponentV2Builder.CommandError(result.Error.Value, result.ErrorReason)
                    : ComponentV2Builder.Error("Command Error", result.ErrorReason);
                await SendFallbackAsync(interaction, errorComponents);
            }
        }
        catch (Exception ex)
        {
            if (interaction is SocketAutocompleteInteraction)
            {
                Logs.Debug($"Autocomplete interaction failed (likely superseded): {ex.Message}");
                return;
            }

            Logs.Error($"[{InstanceId}] Error handling interaction: {ex.Message}");
            await SendFallbackAsync(interaction,
                ComponentV2Builder.Error("Command Error", "An unexpected error occurred while processing your command. Please try again later."));
        }
    }

    /// <summary>Reports a command whose handler threw (async run mode hides these from the caller). Logs the
    /// failure with the instance ID and tells the user, once, unless the handler already responded.</summary>
    private async Task OnCommandExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess)
            return;

        string reason = result.ErrorReason ?? "unknown";
        Logs.Error($"[{InstanceId}] Command '{command.Name}' failed: {result.Error} - {reason}");

        if (context.Interaction is not SocketInteraction interaction)
            return;

        MessageComponent components = result.Error.HasValue
            ? ComponentV2Builder.CommandError(result.Error.Value, reason)
            : ComponentV2Builder.Error("Command Error", "Something went wrong running that command. Please try again.");
        await SendFallbackAsync(interaction, components);
    }

    /// <summary>Sends an ephemeral error. Follows up if the interaction was acknowledged, otherwise responds.
    /// A failure here is logged, never rethrown: the interaction may already have expired.</summary>
    private static async Task SendFallbackAsync(SocketInteraction interaction, MessageComponent components)
    {
        try
        {
            if (interaction.HasResponded)
                await interaction.FollowupAsync(components: components, ephemeral: true);
            else
                await interaction.RespondAsync(components: components, ephemeral: true);
        }
        catch (Exception responseEx)
        {
            Logs.Warning($"[{InstanceId}] Could not send error response (interaction likely expired or already answered): {responseEx.Message}");
        }
    }

    /// <summary>Processes Discord client log events and routes them to the application's logging system</summary>
    /// <param name="message">The log message from Discord</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private Task LogAsync(LogMessage message)
    {
        switch (message.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                Logs.Error($"[Discord] {message.Source}: {message.Message} {message.Exception}");
                break;

            case LogSeverity.Warning:
                Logs.Warning($"[Discord] {message.Source}: {message.Message}");
                break;

            case LogSeverity.Info:
                Logs.Info($"[Discord] {message.Source}: {message.Message}");
                break;

            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                Logs.Debug($"[Discord] {message.Source}: {message.Message}");
                break;
        }

        return Task.CompletedTask;
    }
}
