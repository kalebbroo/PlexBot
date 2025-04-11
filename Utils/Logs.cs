using System.Collections.Concurrent;

namespace PlexBot.Utils
{
    /// <summary>Central logging system that provides thread-safe logging capabilities with configurable output destinations, formatting, and persistence</summary>
    public static class Logs
    {
        /// <summary>Thread-safe lock object that prevents console output from overlapping when multiple threads write logs simultaneously</summary>
        private static readonly object ConsoleLock = new();

        /// <summary>Complete path to the current log file based on the configured template and runtime date/time substitutions</summary>
        public static string LogFilePath = string.Empty;

        /// <summary>Thread-safe queue of log messages waiting to be written to the log file by the background save thread</summary>
        private static ConcurrentQueue<string> LogsToSave = new();

        /// <summary>Background thread responsible for periodically flushing queued log messages to disk without blocking application execution</summary>
        private static Thread? LogSaveThread = null;

        /// <summary>Synchronization primitive that signals when the log save thread has completed its final flush during shutdown</summary>
        private static ManualResetEvent LogSaveCompletion = new(false);

        /// <summary>Timestamp of the last logged message used to determine when to insert timestamp headers for improved readability</summary>
        private static long LastLogTime = 0;

        /// <summary>Time interval threshold after which a full timestamp header will be inserted between log entries to provide temporal context</summary>
        public static TimeSpan RepeatTimestampAfter = TimeSpan.FromMinutes(5);

        /// <summary>Available log severity levels arranged in ascending order of importance from detailed debugging to critical errors</summary>
        public enum LogLevel : int
        {
            /// <summary>Extremely detailed execution tracing for deep debugging of complex issues and execution flow analysis</summary>
            Verbose,

            /// <summary>Development-time information useful for diagnosing issues but too verbose for regular operation</summary>
            Debug,

            /// <summary>General operational information about application state and key activities useful in production environments</summary>
            Info,

            /// <summary>Special markers for application startup, initialization phases, and major system configuration events</summary>
            Init,

            /// <summary>Potential issues that don't prevent functionality but might indicate future problems or degraded performance</summary>
            Warning,

            /// <summary>Serious issues that have caused an operation to fail and require immediate attention or investigation</summary>
            Error,

            /// <summary>Special level used to completely disable all logging when set as the minimum threshold</summary>
            None
        }

        /// <summary>Configurable threshold that determines which severity levels are displayed in output based on operational needs</summary>
        public static LogLevel MinimumLevel = LogLevel.Info;

        /// <summary>Called during program initialization to set up log file saving based on provided configuration settings</summary>
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

        /// <summary>Internal thread loop for periodically saving logs to file without blocking application execution</summary>
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

        /// <summary>Stops the log saving thread and ensures all pending logs are saved during application shutdown</summary>
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

        /// <summary>Immediately saves all pending logs to the log file without blocking application execution</summary>
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

        /// <summary>Helper class to track recent log messages for a specific log level with customizable history size</summary>
        public class LogTracker
        {
            /// <summary>Maximum number of log messages to keep in the history for each tracker</summary>
            public static int MaxTracked = 1024;

            /// <summary>Thread-safe queue of recent log messages, limited to MaxTracked entries</summary>
            public Queue<LogMessage> Messages = new(MaxTracked);

            /// <summary>HTML color code for this log type when displaying in web UIs, matching the console color scheme</summary>
            public string Color = "#707070";

            /// <summary>Global static sequence ID used to track message order across all trackers</summary>
            public static long LastSequenceID = 0;

            /// <summary>Lock object to synchronize access to this tracker's state and prevent data corruption</summary>
            public readonly object Lock = new();

            /// <summary>Last sequence ID in this tracker, used to determine if there are new messages since last check</summary>
            public long LastSeq = 0;

            /// <summary>Additional identifying information for this log tracker, useful when multiple trackers are used</summary>
            public string Identifier = string.Empty;

            /// <summary>Adds a new log message to this tracker, updating the message queue and sequence numbers</summary>
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

        /// <summary>Represents a single log message with its timestamp and content for storage in history trackers</summary>
        public record struct LogMessage(DateTimeOffset Time, string Message, long Sequence);

        /// <summary>Array of log trackers for each log level, providing a historical record of recent logs by severity</summary>
        public static readonly LogTracker[] Trackers = new LogTracker[(int)LogLevel.None];

        /// <summary>Named collection of log trackers for specialized logging needs, allowing separate tracking of logs from different application parts</summary>
        public static readonly Dictionary<string, LogTracker> OtherTrackers = new();

        /// <summary>Static constructor to initialize the log trackers with appropriate colors and settings</summary>
        static Logs()
        {
            // Create trackers for each log level with corresponding colors
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

        /// <summary>Log a verbose trace message, typically used during development for detailed execution flow analysis</summary>
        /// <param name="message">The message to log</param>
        public static void Verbose(string message)
        {
            LogWithColor(ConsoleColor.DarkGray, ConsoleColor.Gray, "Verbose", ConsoleColor.Black, ConsoleColor.DarkGray, message, LogLevel.Verbose);
        }

        /// <summary>Log a debug message with information useful for troubleshooting during development</summary>
        /// <param name="message">The message to log</param>
        public static void Debug(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Gray, "Debug", ConsoleColor.Black, ConsoleColor.Gray, message, LogLevel.Debug);
        }

        /// <summary>Log a general informational message about normal application operation</summary>
        /// <param name="message">The message to log</param>
        public static void Info(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Cyan, "Info", ConsoleColor.Black, ConsoleColor.White, message, LogLevel.Info);
        }

        /// <summary>Log an initialization-related message for application startup and major system events</summary>
        /// <param name="message">The message to log</param>
        public static void Init(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Green, "Init", ConsoleColor.Black, ConsoleColor.Gray, message, LogLevel.Init);
        }

        /// <summary>Log a warning message for potential problems that don't prevent functionality but may indicate issues</summary>
        /// <param name="message">The message to log</param>
        public static void Warning(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Yellow, "Warning", ConsoleColor.Black, ConsoleColor.Yellow, message, LogLevel.Warning);
        }

        /// <summary>Log an error message for operational failures that require immediate attention or investigation</summary>
        /// <param name="message">The message to log</param>
        public static void Error(string message)
        {
            LogWithColor(ConsoleColor.Black, ConsoleColor.Red, "Error", ConsoleColor.Black, ConsoleColor.Red, message, LogLevel.Error);
        }

        /// <summary>Internal method to log a message with specific colors, formatting, and timestamp for console and file output</summary>
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