using System;
using System.Diagnostics;
using System.IO;


public static class Logger
{
    public static bool IsDebug = ConfigJson.json["IsDebug"]?.ToObject<bool>() ?? true;
    // Base directory for logs
    private static readonly string BaseLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

    // Method to get the log file path based on the current hour
    private static string GetLogFilePath()
    {
        // Get the current date and hour
        string datePart = DateTime.Now.ToString("yyyy-MM-dd");
        string hourPart = DateTime.Now.ToString("HH");

        // Create the log file name with the date and hour
        string logFileName = $"ServiceLog_{datePart}_{hourPart}.txt";

        // Combine the base directory and log file name
        return Path.Combine(BaseLogDirectory, logFileName);
    }

    public static void i(string message)
    {
        if (!IsDebug) return;
        try
        {
            // Get the log file path for the current hour
            string logFilePath = GetLogFilePath();

            // Ensure the log directory exists
            Directory.CreateDirectory(BaseLogDirectory);

            // Create the log message with timestamp
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";

            // Append the log message to the file
            File.AppendAllText(logFilePath, logMessage);
        }
        catch (Exception ex)
        {
            // If logging fails, output to console (for debugging purposes)
            Console.WriteLine($"日志写入失败: {ex.Message}");
        }
    }
}

public static class Logger2
{
    public static bool IsDebug = ConfigJson.json["IsDebug"]?.ToObject<bool>() ?? true;
    // Base directory for logs
    private static readonly string BaseLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs2");

    // Method to get the log file path based on the current hour
    private static string GetLogFilePath()
    {
        // Get the current date and hour
        string datePart = DateTime.Now.ToString("yyyy-MM-dd");
        string hourPart = DateTime.Now.ToString("HH");

        // Create the log file name with the date and hour
        string logFileName = $"ServiceLog_{datePart}_{hourPart}.txt";

        // Combine the base directory and log file name
        return Path.Combine(BaseLogDirectory, logFileName);
    }

    public static void i(string message)
    {
        if (!IsDebug) return;
        try
        {
            // Get the log file path for the current hour
            string logFilePath = GetLogFilePath();

            // Ensure the log directory exists
            Directory.CreateDirectory(BaseLogDirectory);

            // Create the log message with timestamp
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";

            // Append the log message to the file
            File.AppendAllText(logFilePath, logMessage);
        }
        catch (Exception ex)
        {
            // If logging fails, output to console (for debugging purposes)
            Console.WriteLine($"日志写入失败: {ex.Message}");
        }
    }
}
