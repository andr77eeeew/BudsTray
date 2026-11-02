using System;
using System.IO;

namespace BudsTray.Core;

public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BudsTray");

    private static string FilePath => Path.Combine(DataDirectory, "app.log");

    public static void Info(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    file.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
