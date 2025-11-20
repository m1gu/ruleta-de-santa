using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class DailyReportService
{
    class PrizeSummary
    {
        public string Id;
        public string Name;
        public int Delivered;
    }

    static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
    static Dictionary<string, PrizeSummary> summaries = new Dictionary<string, PrizeSummary>(IdComparer);
    static List<GameManager.PrizeConfig> cachedPrizes = new List<GameManager.PrizeConfig>();
    static string activeDate = null;
    static bool initialized = false;
    static bool autoRotateWithSystemDate = false;
    static int totalSpins = 0;
    static int totalSuerte = 0;
    static bool dirty = false;

    public static void Initialize(string date, List<GameManager.PrizeConfig> prizes, bool autoRotate)
    {
        if (initialized)
        {
            WriteReport(true);
        }

        activeDate = string.IsNullOrEmpty(date)
            ? DateTime.Today.ToString("yyyy-MM-dd")
            : date;

        autoRotateWithSystemDate = autoRotate;
        cachedPrizes = prizes != null ? new List<GameManager.PrizeConfig>(prizes) : new List<GameManager.PrizeConfig>();

        ResetCounters();
        initialized = true;
        dirty = true;
        WriteReport(true); // crea archivo vacio para el dia
    }

    public static void RegisterSpin(GameManager.PrizeConfig prize, bool isSuerte)
    {
        if (!initialized)
        {
            Debug.LogWarning("[DailyReportService] RegisterSpin llamado sin inicializar.");
            return;
        }

        MaybeRotateIfNeeded();

        totalSpins++;
        if (isSuerte)
            totalSuerte++;

        if (prize != null && !string.IsNullOrEmpty(prize.id))
        {
            if (!summaries.TryGetValue(prize.id, out var summary))
            {
                summary = new PrizeSummary
                {
                    Id = prize.id,
                    Name = prize.name ?? prize.id,
                    Delivered = 0
                };
                summaries[prize.id] = summary;
            }

            summary.Name = prize.name ?? prize.id;
            summary.Delivered++;
        }

        dirty = true;
        WriteReport();
    }

    public static void ForceWriteReport()
    {
        WriteReport(true);
    }

    static void ResetCounters()
    {
        summaries = new Dictionary<string, PrizeSummary>(IdComparer);
        foreach (var prize in cachedPrizes)
        {
            if (prize == null || string.IsNullOrEmpty(prize.id)) continue;
            if (summaries.ContainsKey(prize.id)) continue;
            summaries[prize.id] = new PrizeSummary
            {
                Id = prize.id,
                Name = prize.name ?? prize.id,
                Delivered = 0
            };
        }

        totalSpins = 0;
        totalSuerte = 0;
    }

    static void MaybeRotateIfNeeded()
    {
        if (!autoRotateWithSystemDate) return;

        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (today == activeDate) return;

        WriteReport(true);
        activeDate = today;
        ResetCounters();
        dirty = true;
        WriteReport(true);
    }

    static void WriteReport(bool force = false)
    {
        if (!initialized) return;
        if (!dirty && !force) return;

        try
        {
            string path = DataPaths.GetReportFilePath(activeDate);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (StreamWriter sw = new StreamWriter(path, false))
            {
                sw.WriteLine("Metric,Value");
                sw.WriteLine($"Date,{activeDate}");
                sw.WriteLine($"TotalSpins,{totalSpins}");
                sw.WriteLine($"TotalSuerteProxima,{totalSuerte}");
                sw.WriteLine();
                sw.WriteLine("PrizeID,PrizeName,Delivered");

                foreach (var entry in summaries.Values)
                {
                    if (entry.Delivered <= 0) continue;
                    sw.WriteLine($"{EscapeCsv(entry.Id)},{EscapeCsv(entry.Name)},{entry.Delivered}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[DailyReportService] Error escribiendo reporte diario: " + ex.Message);
        }
        finally
        {
            dirty = false;
        }
    }

    static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        bool needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        string sanitized = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{sanitized}\"" : sanitized;
    }
}
