using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// Proje kök dizinindeki AdventureLog.txt dosyasına oyuncu, DM ve sistem diyaloglarını anlık UTF-8 olarak kaydeden
/// ve modelin tutarlı cevap üretmesi için son log satırlarını sağlayan kalıcı log yöneticisi.
/// </summary>
public static class AdventureLogger
{
    private static readonly object _fileLock = new();
    private const string LogFileName = "AdventureLog.txt";
    private const string Header = "=== DUNGEON MASTER MACERA LOGU ===";

    public static string GetLogFilePath()
    {
        try
        {
            string globalPath = ProjectSettings.GlobalizePath($"res://{LogFileName}");
            if (!string.IsNullOrWhiteSpace(globalPath))
                return globalPath;
        }
        catch
        {
        }

        return Path.Combine(Directory.GetCurrentDirectory(), LogFileName);
    }

    /// <summary>
    /// AdventureLog.txt dosyasını sıfırlar ve isteğe bağlı başlangıç DM anlatımını ekler.
    /// </summary>
    public static void ClearAndInit(string initialDmIntro = null)
    {
        lock (_fileLock)
        {
            try
            {
                string path = GetLogFilePath();
                var sb = new StringBuilder();
                sb.AppendLine(Header);
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(initialDmIntro))
                {
                    sb.AppendLine($"[DM]: {CleanLogLine(initialDmIntro)}");
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AdventureLogger] ClearAndInit hatası: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Oyuncu mesajını [OYUNCU]: etiketiyle log dosyasına ekler.
    /// </summary>
    public static void AppendPlayer(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendEntry($"[OYUNCU]: {CleanLogLine(text)}");
    }

    /// <summary>
    /// DM anlatımını [DM]: etiketiyle log dosyasına ekler.
    /// </summary>
    public static void AppendDm(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendEntry($"[DM]: {CleanLogLine(text)}");
    }

    /// <summary>
    /// Sistem ve savaş/zar olaylarını [SİSTEM]: etiketiyle log dosyasına ekler.
    /// </summary>
    public static void AppendSystem(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendEntry($"[SİSTEM]: {CleanLogLine(text)}");
    }

    private static void AppendEntry(string line)
    {
        lock (_fileLock)
        {
            try
            {
                string path = GetLogFilePath();
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, Header + "\n\n", Encoding.UTF8);
                }
                File.AppendAllText(path, line + "\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AdventureLogger] AppendEntry hatası: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// AdventureLog.txt dosyasından [OYUNCU]:, [DM]: veya [SİSTEM]: ile başlayan son maxLines satırı okur.
    /// </summary>
    public static string ReadRecentLogForPrompt(int maxLines = 16)
    {
        lock (_fileLock)
        {
            try
            {
                string path = GetLogFilePath();
                if (!File.Exists(path))
                    return "";

                string[] allLines = File.ReadAllLines(path, Encoding.UTF8);
                var validLines = allLines
                    .Where(l => !string.IsNullOrWhiteSpace(l) &&
                                !l.StartsWith("===") &&
                                (l.StartsWith("[OYUNCU]:") || l.StartsWith("[DM]:") || l.StartsWith("[SİSTEM]:")))
                    .ToList();

                if (validLines.Count == 0)
                    return "";

                int skip = Math.Max(0, validLines.Count - maxLines);
                return string.Join("\n", validLines.Skip(skip));
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AdventureLogger] ReadRecentLogForPrompt hatası: {ex.Message}");
                return "";
            }
        }
    }

    private static string CleanLogLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Çok satırlı cevapları tek satıra sıkıştırarak log formatını temiz tut
        string cleaned = text.Replace("\r\n", " ").Replace("\n", " ").Trim();
        return cleaned;
    }
}
