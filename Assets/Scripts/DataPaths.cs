using System;
using System.IO;
using UnityEngine;

public static class DataPaths
{
    // Carpeta Data junto al ejecutable (o junto a Assets en el editor)
    public static string DataDir
    {
        get
        {
            // Application.dataPath:
            //  - Editor: .../RuletaSanta/Assets
            //  - Build:  .../RuletaSanta_Data
            string baseDir = Application.dataPath;
            string rootDir = Path.GetFullPath(Path.Combine(baseDir, ".."));

            string dataDir = Path.Combine(rootDir, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
#if UNITY_EDITOR
                Debug.Log("[DataPaths] Creada carpeta Data en: " + dataDir);
#endif
            }
            return dataDir;
        }
    }

    public static string ModeFilePath =>
        Path.Combine(DataDir, "modo.txt");

    public static string PrizesFilePath =>
        Path.Combine(DataDir, "premios.json");

    public static string InventoryFilePath =>
        Path.Combine(DataDir, "inventario.csv");

    public static string StateFilePath =>
        GetStateFilePath(DateTime.Today.ToString("yyyy-MM-dd"));

    public static string ScheduleFilePath =>
        Path.Combine(DataDir, "horarios.json");

    public static string GetStateFilePath(string date)
    {
        string safeDate = string.IsNullOrEmpty(date)
            ? DateTime.Today.ToString("yyyy-MM-dd")
            : date;
        return Path.Combine(DataDir, $"state_{safeDate}.json");
    }

    public static string GetReportFilePath(string date)
    {
        string safeDate = string.IsNullOrEmpty(date)
            ? DateTime.Today.ToString("yyyy-MM-dd")
            : date;
        return Path.Combine(DataDir, $"report_{safeDate}.csv");
    }
}
