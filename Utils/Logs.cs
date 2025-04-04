using System.Collections.Concurrent;

namespace PlexBot.Utils
{
    /// <summary>Central logging system for the entire application.
    /// Provides consistent, thread-safe logging capabilities with multiple output
    /// destinations and log levels. This class centralizes all logging operations
    /// and ensures proper formatting, timestamping, and persistence of log messages.</summary>
    public static class Logs
    {
        /// <summary>Thread lock to prevent console output from multiple threads overlapping.
        /// Ensures that log messages are written to the console as complete units.</summary>
        private static readonly object ConsoleLock = new();

        /// <summary>Path to the current log file.
        /// Generated based on the configured log path template and current date/time.</summary>
        public static string LogFilePath = string.Empty;

        /// <summary>Queue of logs waiting to be saved to file.
        /// Messages are added to this queue and periodically flushed to disk by the logger thread.</summary>
        private static ConcurrentQueue<string> LogsToSave = new();

        /// <summary>Thread that handles the background saving of logs to file.
        /// Runs at a regular interval to flush the log queue to disk.</summary>
        private static Thread? LogSaveThread = null;

        /// <summary>Event that signals when the log save thread has completed.
        /// Used during shutdown to ensure all logs are saved before the application exits.</summary>
        private static ManualResetEvent LogSaveCompletion = new(false);

        /// <summary>Timestamp of the last log message in Environment.TickCount64 format.
        /// Used to determine when to insert full timestamps in the log output.</summary>
        private static long LastLogTime = 0;

        /// <summary>Time between log messages after which a full timestamp header should be rendered.
        /// Helps with readability by grouping related log messages and providing temporal context.</summary>
        public static TimeSpan RepeatTimestampAfter = TimeSpan.FromMinutes(5);

        /// <summary>Defines the available log severity levels.
        /// Ordered from most verbose to most critical, with None representing no logging.</summary>
        public enum LogLevel : int
        {
            /// <summary>Very detailed logs for tracing execution flow, typically used for development.</summary>
            Verbose,

            /// <summary>Detailed information useful for debugging, typically used for development.</summary>
            Debug,

            /// <summary>General information about application operation, the default level.</summary>
            Info,

            /// <summary>Information related to application initialization and setup.</summary>
            Init,

            /// <summary>Potential problems that don't prevent the application from working.</summary>
            Warning,

            /// <summary>Problems that have caused a failure in some operation.</summary>
            Error,

            /// <summary>Used to disable logging entirely when set as the minimum level.</summary>
            None
        }

        /// <summary>Minimum log level to show in the console output.
        /// Messages with a severity below this level will be suppressed.</summary>
        public static LogLevel MinimumLevel = LogLevel.Info;

        /// <summary>Called during program initialization to set up log file saving.
        /// Creates the log directory if needed and starts the log save thread.</summary>
        /// <param name="settings">Dictionary containing logging configuration</param>
        public static void StartLogSaving(Dictionary<string, string> settings)
        {
            // Check if logging to file is enabled
            if (!bool.TryParse(settings.GetValueOrDefault("SaveToFile", "true"), out bool saveToFile) || !saveToFile)
            {
                LogSaveCompletion.Set();
                LogsToSave = new ConcurrentQueue<string>();
                return;
            }
            // Get log path from settings
            LogFilePath = settings.GetValueOrDefault("LogPath", "logs/plex-bot-[year]-[month]-[day].log");
            // Replace placeholders in the log path with actual values
            DateTimeOffset time = DateTimeOffset.Now;
            LogFilePath = LogFilePath
                .Replace("[year]", $"{time.Year:0000}")
                .Replace("[month]", $"{time.Month:00}")
                .Replace("[month_name]", $"{time:MMMM}")
                .Replace("[day]", $"{time.Day:00}")
                .Replace("[day_name]", $"{time:dddd}")
                .Replace("[hour]", $"{time.Hour:00}")
                .Replace("[minute]", $"{time.Minute:00}")
                .Replace("[second]", $"{time.Second:00}")
                .Replace("[pid]", $"{Environment.ProcessId}");
            // Ensure the log directory exists
            string? directory = System.IO.Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            // Start the log save thread
            LogSaveThread = new Thread(LogSaveInternalLoop) { Name = "logsaver", IsBackground = true };
            LogSaveThread.Start();
            Info($"Log saving started. Writing logs to: {LogFilePath}");
        }

        /// <summary>Internal thread loop for periodically saving logs to file.
        /// Runs until the application is shutting down, flushing the log queue at regular intervals.</summary>
        private static void LogSaveInternalLoop()
        {
            while (true)
            {
                try
                {
                    // Save any pending logs
                    SaveLogsToFileOnce();
                    // Wait for the next save interval
                    Thread.Sleep(15000);  // 15 seconds
                }
                catch (ThreadInterruptedException)
                {
                    // Final save on shutdown
                    SaveLogsToFileOnce();
                    break;
                }
                catch (Exception ex)
                {
                    // If logging itself fails, try to at least write to console
                    Console.WriteLine($"Error in log save thread: {ex.Message}");
                }
            }
            LogSaveCompletion.Set();
        }

        /// <summary>Stops the log saving thread and ensures all pending logs are saved.
        /// Called during application shutdown to make sure no logs are lost.</summary>
        public static void StopLogSaving()
        {
            if (LogSaveThread != null && LogSaveThread.IsAlive)
            {
                // Interrupt the thread to stop it
                LogSaveThread.Interrupt();
                // Wait for it to complete (with timeout)
                LogSaveCompletion.WaitOne(5000);
            }
        }

        /// <summary>Immediately saves all pending logs to the log file.
        /// Used both for periodic saves and final saves during shutdown.</summary>
        private static void SaveLogsToFileOnce()
        {
            if (LogsToSave.IsEmpty)
            {
                return;
            }
            try
            {
                StringBuilder toStore = new();
                while (LogsToSave.TryDequeue(out string? line))
                {
                    toStore.AppendLine(line);
                }
                if (toStore.Length > 0)
                {
                    File.AppendAllText(LogFilePath, toStore.ToString());
                }
            }
            catch (Exception ex)
            {
                // If logging itself fails, try to at least write to console
                Console.WriteLine($"Error saving logs to file: {ex.Message}");
            }
        }

        /// <summary>Helper class to track recent log messages for a specific log level.
        /// Used to maintain a history of recent logs for display in UIs or for diagnostics.</summary>
        public class LogTracker
        {
            /// <summary>Maximum number of log messages to keep in the history.
            /// Once this limit is reached, older messages are discarded.</summary>
            public static int MaxTracked = 1024;

            /// <summary>Queue of recent log messages, limited to MaxTracked entries.
            /// Older messages are removed when the queue exceeds this limit.</summary>
            public Queue<LogMessage> Messages = new(MaxTracked);

            /// <summary>HTML color code for this log type when displaying in web UIs.
            /// Matches the console color scheme for consistency.</summary>
            public string Color = "#707070";

            /// <summary>Global static sequence ID used to track message order.
            /// Incremented for each new log message across all trackers.</summary>
            public static long LastSequenceID = 0;

            /// <summary>Lock object to synchronize access to this tracker's state.
            /// Prevents data corruption when multiple threads access the same tracker.</summary>
            public readonly object Lock = new();

            /// <summary>Last sequence ID in this tracker.
            /// Used to determine if there are new messages since last check.</summary>
            public long LastSeq = 0;

            /// <summary>Additional identifying information for this log tracker.
            /// Useful when multiple trackers are used for different parts of the application.</summary>
            public string Identifier = string.Empty;

            /// <summary>Adds a new log message to this tracker.
            /// Updates the message queue and sequence numbers.</summary>
            /// <param name="message">The log message to track</param>
            public void Track(string message)
            {
                lock (Lock)
                {
                    long seq = Interlocked.Increment(ref LastSequenceID);
                    Messages.Enqueue(new LogMessage(DateTimeOffset.Now, message, seq));
                    LastSeq = seq;
                    if (Messages.Count > MaxTracked)
                    {
                        Messages.Dequeue();
                    }
                }
            }
        }

        /// <summary>Represents a single log message with its timestamp and content.
        /// Used for storing logs in the history trackers.</summary>
        public record struct LogMessage(DateTimeOffset Time, string Message, long Sequence);

        /// <summary>Array of log trackers for each log level.
        /// Provides a historical record of recent logs by severity.</summary>
        public static readonly LogTracker[] Trackers = new LogTracker[(int)LogLevel.None];

        /// <summary>Named collection of log trackers for specialized logging needs.
        /// Allows tracking logs from different parts of the application separately.</summary>
        public static readonly Dictionary<string, LogTracker> OtherTrackers = new();

        /// <summary>Static constructor to initialize the log trackers.
        /// Sets up trackers for each log level with appropriate colors.</summary>
        static Logs()
        {
            // Create trackers for each log level with appropriate colors
            Trackers[(int)LogLevel.Verbose] = new() { Color = "#606060" };
            Trackers[(int)LogLevel.Debug] = new() { Color = "#808080" };
            Trackers[(int)LogLevel.Info] = new() { Color = "#00FFFF" };
            Trackers[(int)LogLevel.Init] = new() { Color = "#00FF00" };
            Trackers[(int)LogLevel.Warning] = new() { Color = "#FFFF00" };
            Trackers[(int)LogLevel.Error] = new() { Color = "#FF0000" };
            // Add trackers to named collection for easy access
            for (int i = 0; i < (int)LogLevel.None; i++)
            {
                OtherTrackers[$"{(LogLevel)i}"] = Trackers[i];
            }
        }

        /// <summary>Log a verbose trace message, typically used during development.
        /// Provides the most detailed level of logging for granular debugging.</summary>
        /// <param name="message">The message to log</param>
        public static void Verbose(string message)
        {
            LogWithColor(ConsoleColor.DarkGray, ConsoleColor.Gray, "Verbose", ConsoleColor.Black, ConsoleColor.DarkGray, message, LogLevel.Verbose);
        }

        /// <summary>Log a debug message with information useful for troubleshooting.
        /// Provides details about program flow and variable values during execution.</summary>
        /// <param name="message">The message to log</param>
        public static void Debug(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Gray, "Debug", ConsoleColor.Black, ConsoleColor.Gray, message, LogLevel.Debug);
        }

        /// <summary>Log a general informational message about normal operation.
        /// Used for tracking the application's standard activities and state changes.</summary>
        /// <param name="message">The message to log</param>
        public static void Info(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Cyan, "Info", ConsoleColor.Black, ConsoleColor.White, message, LogLevel.Info);
        }

        /// <summary>Log an initialization-related message.
        /// Used specifically for tracking application startup, component initialization, and configuration loading.</summary>
        /// <param name="message">The message to log</param>
        public static void Init(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Green, "Init", ConsoleColor.Black, ConsoleColor.Gray, message, LogLevel.Init);
        }

        /// <summary>Log a warning message for potential problems.
        /// Used for issues that don't prevent the application from working but may indicate problems.</summary>
        /// <param name="message">The message to log</param>
        public static void Warning(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Yellow, "Warning", ConsoleColor.Black, ConsoleColor.Yellow, message, LogLevel.Warning);
        }

        /// <summary>Log an error message for operational failures.
        /// Used for problems that prevent some functionality from working correctly.</summary>
        /// <param name="message">The message to log</param>
        public static void Error(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Red, "Error", ConsoleColor.Black, ConsoleColor.Red, message, LogLevel.Error);
        }

        /// <summary>Internal method to log a message with specific colors.
        /// Formats the message with timestamp and prefix, applies colors, and writes to the console and log file.</summary>
        /// <param name="prefixBackground">Background color for the log level prefix</param>
        /// <param name="prefixForeground">Foreground color for the log level prefix</param>
        /// <param name="prefix">The log level prefix text</param>
        /// <param name="messageBackground">Background color for the message</param>
        /// <param name="messageForeground">Foreground color for the message</param>
        /// <param name="message">The message to log</param>
        /// <param name="level">The severity level of this message</param>
        private static void LogWithColor(ConsoleColor prefixBackground, ConsoleColor prefixForeground, string prefix, ConsoleColor messageBackground, ConsoleColor messageForeground, string message, LogLevel level)
        {
            // Track the message in the appropriate tracker
            Trackers[(int)level].Track(message);
            // Skip console output if below minimum level
            if (MinimumLevel > level)
            {
                // Still add to file log even if not shown in console
                LogsToSave?.Enqueue($"{DateTimeOffset.Now:HH:mm:ss.fff} [{prefix}] {message}");
                return;
            }
            lock (ConsoleLock)
            {
                // Reset console colors
                Console.BackgroundColor = ConsoleColor.Black;
                // Get current timestamp
                DateTimeOffset timestamp = DateTimeOffset.Now;
                // Add a timestamp header if it's been a while since the last log
                if (Environment.TickCount64 - LastLogTime > RepeatTimestampAfter.TotalMilliseconds && LastLogTime != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"== PlexBot logs {timestamp:yyyy-MM-dd HH:mm} ==");
                }
                // Update last log time
                LastLogTime = Environment.TickCount64;
                // Format the log time
                string time = $"{timestamp:HH:mm:ss.fff}";
                // Write timestamp
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"{time} [");
                // Write prefix with colors
                Console.BackgroundColor = prefixBackground;
                Console.ForegroundColor = prefixForeground;
                Console.Write(prefix);
                // Reset for bracket
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("] ");
                // Write message with colors
                Console.BackgroundColor = messageBackground;
                Console.ForegroundColor = messageForeground;
                Console.WriteLine(message);
                // Reset console colors
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
                // Add to file log queue
                LogsToSave?.Enqueue($"{time} [{prefix}] {message}");
            }
        }
    }
}