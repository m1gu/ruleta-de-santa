using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[ExecuteInEditMode]
public class DaySimulator : MonoBehaviour
{
    [Header("Configuracion General del Dia")]
    public string simulatedDate = "2025-11-20";
    public int totalRuns = 500;  // total giros del dia

    [Header("Distribucion por Porcentajes del Dia")]
    [Range(0f, 1f)] public float pctMode1 = 0.30f;
    [Range(0f, 1f)] public float pctMode2 = 0.30f;
    [Range(0f, 1f)] public float pctMode3 = 0.40f;

    [Header("Pacing")]
    [Range(0f, 1f)] public float minRealPrizeProbability = 0.10f;
    [Range(0f, 1f)] public float maxRealPrizeProbability = 0.90f;

    [Header("Export")]
    public string outputFilename = "simulacion_resultados.csv";
    public string timelineFilename = "simulacion_timeline.csv";

    [Header("Accion")]
    public bool runSimulation = false;   // marcar en Inspector para ejecutar

    void Update()
    {
        if (!runSimulation) return;
        runSimulation = false;

        SimulateDay();
    }

    void SimulateDay()
    {
        Debug.Log("[Simulator] Iniciando simulacion del dia completo.");

        // 1) Cargar premios desde premios.json exactamente como GameManager
        List<GameManager.PrizeConfig> prizes = new List<GameManager.PrizeConfig>();
        InventoryService.LoadPrizesFromJson(ref prizes);

        if (prizes.Count == 0)
        {
            Debug.LogError("[Simulator] premios.json vacio o no cargado.");
            return;
        }

        // 2) Forzar fecha simulada para que LoadInventoryForToday use esa fecha
        InventoryService.SimulatedDateOverride = simulatedDate;

        // Usar EXACTAMENTE la misma funcion que el juego real
        int[] baseStock = InventoryService.LoadInventoryForToday(prizes);

        // Limpiar override para no afectar nada mas
        InventoryService.SimulatedDateOverride = null;

#if UNITY_EDITOR
        int totalInv = 0;
        foreach (int v in baseStock) totalInv += v;
        Debug.Log($"[Simulator] Inventario leido para {simulatedDate}: total={totalInv}");
#endif

        // 3) Buscar indice de SUERTEPROXIMA
        int indexSuerte = -1;
        for (int i = 0; i < prizes.Count; i++)
        {
            if (!string.IsNullOrEmpty(prizes[i].id) &&
                string.Equals(prizes[i].id, "SUERTEPROXIMA", StringComparison.OrdinalIgnoreCase))
            {
                indexSuerte = i;
                break;
            }
        }

        if (indexSuerte < 0)
        {
            Debug.LogWarning("[Simulator] No se encontro premio con id SUERTEPROXIMA.");
        }

        // 4) Crear selector con la misma logica de premios que el juego (sin animacion)
        PrizeSelector selector = new PrizeSelector(prizes, baseStock, indexSuerte);
        selector.expectedSpinsPerDay = totalRuns;
        selector.totalPlannedSpins = totalRuns;
        selector.minRealProb = minRealPrizeProbability;
        selector.maxRealProb = maxRealPrizeProbability;

        // 5) Calcular cuantos giros por modo segun porcentajes
        int runsM1 = Mathf.RoundToInt(totalRuns * pctMode1);
        int runsM2 = Mathf.RoundToInt(totalRuns * pctMode2);
        int used = runsM1 + runsM2;
        int runsM3 = totalRuns - used;

        // 6) Preparar timeline de horas desde 11:00 a 20:00
        List<string> timeline = new List<string>();
        DateTime start = DateTime.Parse(simulatedDate + " 11:00");
        DateTime end = DateTime.Parse(simulatedDate + " 20:00");
        TimeSpan daySpan = end - start;
        TimeSpan stepSpan = TimeSpan.FromSeconds(
            daySpan.TotalSeconds / Mathf.Max(1, totalRuns)
        );
        DateTime t = start;

        // 7) Contadores de premios entregados
        int[] delivered = new int[prizes.Count];
        int deliveredSuerte = 0;

        void RegisterLocal(int idx, int mode)
        {
            string hour = t.ToString("HH:mm");

            if (idx < 0)
            {
                timeline.Add($"{hour},{mode},ERROR");
                return;
            }

            if (indexSuerte >= 0 && idx == indexSuerte)
            {
                deliveredSuerte++;
            }
            else
            {
                delivered[idx]++;
            }

            timeline.Add($"{hour},{mode},{prizes[idx].id}");
        }

        for (int i = 0; i < runsM1; i++)
        {
            int idx = selector.RunOneSpin(1);
            RegisterLocal(idx, 1);
            t = t.Add(stepSpan);
        }

        for (int i = 0; i < runsM2; i++)
        {
            int idx = selector.RunOneSpin(2);
            RegisterLocal(idx, 2);
            t = t.Add(stepSpan);
        }

        for (int i = 0; i < runsM3; i++)
        {
            int idx = selector.RunOneSpin(3);
            RegisterLocal(idx, 3);
            t = t.Add(stepSpan);
        }

        ExportResults(prizes, delivered, deliveredSuerte);
        ExportTimeline(timeline);

        Debug.Log("[Simulator] Simulacion completada. Archivos exportados en /Data/.");
    }

    void ExportResults(List<GameManager.PrizeConfig> prizes, int[] delivered, int suerteCount)
    {
        string path = Path.Combine(Application.dataPath, "../Data/" + outputFilename);
        using (StreamWriter sw = new StreamWriter(path))
        {
            sw.WriteLine("Premio,Entregados");

            for (int i = 0; i < prizes.Count; i++)
            {
                if (prizes[i].id == "SUERTEPROXIMA") continue;
                if (delivered[i] <= 0) continue;
                sw.WriteLine($"{prizes[i].id},{delivered[i]}");
            }

            sw.WriteLine($"SUERTEPROXIMA,{suerteCount}");
        }

        Debug.Log("[Simulator] Archivo de resultados guardado: " + path);
    }

    void ExportTimeline(List<string> timeline)
    {
        string path = Path.Combine(Application.dataPath, "../Data/" + timelineFilename);
        using (StreamWriter sw = new StreamWriter(path))
        {
            sw.WriteLine("Hora,Mode,PremioID");
            foreach (string line in timeline)
                sw.WriteLine(line);
        }

        Debug.Log("[Simulator] Timeline guardado: " + path);
    }
}
