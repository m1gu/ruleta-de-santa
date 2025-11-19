using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class RuletaSimulator : MonoBehaviour
{
    [Header("Parámetros de simulación")]
    [Tooltip("Fecha a simular en formato yyyy-MM-dd (debe existir en inventario.csv)")]
    public string simulationDate = "2025-12-01";

    [Tooltip("Número de corridas a simular")]
    public int runs = 1000;

    [Tooltip("Modo de juego: 1=Pequeños, 2=Mix con prob por categoría, 3=Normal")]
    [Range(1, 3)]
    public int mode = 3;

    [Header("Modo 2 - Probabilidades por categoría")]
    [Range(0f, 1f)]
    public float mode2SmallProbability = 0.8f;
    [Range(0f, 1f)]
    public float mode2MediumProbability = 0.1f;
    [Range(0f, 1f)]
    public float mode2LargeProbability = 0.1f;

    [Header("Debug")]
    public bool logSummaryToConsole = true;

    // Datos internos
    private List<GameManager.PrizeConfig> prizes = new List<GameManager.PrizeConfig>();
    private int[] remainingStock;
    private int indexSuerteProxima = -1;

    // Conteos
    private Dictionary<string, int> prizeCountById = new Dictionary<string, int>();


    // =========================================================
    //  Ejecutar simulación desde menú del inspector
    // =========================================================
    [ContextMenu("Run Simulation")]
    public void RunSimulationContextMenu()
    {
        RunSimulation();
    }


    // =========================================================
    //  LÓGICA PRINCIPAL
    // =========================================================
    public void RunSimulation()
    {
        try
        {
            Debug.Log($"[RuletaSimulator] Simulación: fecha={simulationDate}, runs={runs}, modo={mode}");

            // 1) Cargar premios
            LoadPrizes();

            if (prizes.Count == 0)
            {
                Debug.LogError("[RuletaSimulator] No hay premios cargados.");
                return;
            }

            // 2) Cargar inventario para la fecha
            remainingStock = InventoryService.LoadInventoryForDate(prizes, simulationDate);

            // 3) Encontrar SUERTEPROXIMA
            indexSuerteProxima = -1;
            for (int i = 0; i < prizes.Count; i++)
            {
                if (string.Equals(prizes[i].id, "SUERTEPROXIMA", StringComparison.OrdinalIgnoreCase))
                {
                    indexSuerteProxima = i;
                    break;
                }
            }

            // 4) Inicializar counts
            prizeCountById.Clear();
            foreach (var p in prizes)
                prizeCountById[p.id] = 0;

            // 5) Ejecutar N corridas
            for (int i = 0; i < runs; i++)
            {
                int prizeIndex = SimulateSingleRun();
                if (prizeIndex < 0)
                    break;

                string pid = prizes[prizeIndex].id;
                prizeCountById[pid]++;

                // Consumir stock si no es SUERTEPROXIMA
                if (prizeIndex != indexSuerteProxima)
                    remainingStock[prizeIndex] = Mathf.Max(0, remainingStock[prizeIndex] - 1);
            }

            // 6) Generar CSV
            string csv = BuildCsvReport();
            string path = SaveCsvReport(csv);

            if (logSummaryToConsole)
            {
                Debug.Log("[RuletaSimulator] CSV guardado en: " + path);
                Debug.Log(csv);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[RuletaSimulator] Error: " + ex.Message);
        }
    }


    // =========================================================
    //  Cargar premios desde JSON
    // =========================================================
    void LoadPrizes()
    {
        prizes = new List<GameManager.PrizeConfig>();
        InventoryService.LoadPrizesFromJson(ref prizes);

        if (prizes == null)
            prizes = new List<GameManager.PrizeConfig>();
    }

    // =========================================================
    //  Simulación de una corrida (igual que GameManager)
    // =========================================================
    int SimulateSingleRun()
    {
        if (remainingStock == null) return -1;

        // Stock real (sin SUERTEPROXIMA)
        int totalReal = 0;
        for (int i = 0; i < remainingStock.Length; i++)
        {
            if (i == indexSuerteProxima) continue;
            totalReal += remainingStock[i];
        }

        if (totalReal <= 0)
        {
            if (indexSuerteProxima >= 0)
                return indexSuerteProxima;
            return -1;
        }

        // Elección normal
        return ChoosePrizeIndexSim();
    }


    // =========================================================
    //  Elección del premio con la misma lógica del GameManager
    // =========================================================
    int ChoosePrizeIndexSim()
    {
        List<int> small = new List<int>();
        List<int> medium = new List<int>();
        List<int> large = new List<int>();

        for (int i = 0; i < prizes.Count; i++)
        {
            if (remainingStock[i] <= 0) continue;

            switch (prizes[i].category)
            {
                case GameManager.PrizeCategory.Small:
                    small.Add(i);
                    break;
                case GameManager.PrizeCategory.Medium:
                    medium.Add(i);
                    break;
                case GameManager.PrizeCategory.Large:
                    large.Add(i);
                    break;
            }
        }

        if (small.Count == 0 && medium.Count == 0 && large.Count == 0)
            return -1;

        int ChooseWeighted(List<int> list)
        {
            if (list.Count == 0) return -1;

            float total = 0;
            foreach (int idx in list) total += Mathf.Max(0f, prizes[idx].weight);

            float r = UnityEngine.Random.Range(0f, total);
            float acc = 0;

            foreach (int idx in list)
            {
                acc += Mathf.Max(0f, prizes[idx].weight);
                if (r <= acc) return idx;
            }

            return list[list.Count - 1];
        }

        switch (mode)
        {
            case 1: // Pequeños
                {
                    int idx = ChooseWeighted(small);
                    if (idx >= 0) return idx;

                    List<int> all = new List<int>();
                    all.AddRange(small); all.AddRange(medium); all.AddRange(large);
                    return ChooseWeighted(all);
                }

            case 2: // Probabilidad por categoría
                {
                    bool hs = small.Count > 0;
                    bool hm = medium.Count > 0;
                    bool hl = large.Count > 0;

                    float ps = hs ? mode2SmallProbability : 0;
                    float pm = hm ? mode2MediumProbability : 0;
                    float pl = hl ? mode2LargeProbability : 0;

                    float sum = ps + pm + pl;
                    if (sum <= 0)
                    {
                        List<int> all = new List<int>();
                        all.AddRange(small); all.AddRange(medium); all.AddRange(large);
                        return ChooseWeighted(all);
                    }

                    ps /= sum;
                    pm /= sum;
                    pl /= sum;

                    float r = UnityEngine.Random.value;

                    if (r < ps) return ChooseWeighted(small);
                    else if (r < ps + pm) return ChooseWeighted(medium);
                    else return ChooseWeighted(large);
                }

            case 3:
            default:
                {
                    List<int> all = new List<int>();
                    all.AddRange(small); all.AddRange(medium); all.AddRange(large);
                    return ChooseWeighted(all);
                }
        }
    }


    // =========================================================
    //  CSV con SOLO LOS PREMIOS ENTREGADOS
    // =========================================================
    string BuildCsvReport()
    {
        StringBuilder sb = new StringBuilder();

        // HEADER
        sb.AppendLine("PrizeID,PrizeName,Delivered,FinalStock");

        int totalSuerte = 0;

        foreach (var p in prizes)
        {
            string id = p.id;
            int delivered = prizeCountById[id];

            if (delivered <= 0) continue; // SOLO premios entregados

            int stockFinal = 0;
            int idx = prizes.IndexOf(p);
            if (idx >= 0) stockFinal = remainingStock[idx];

            if (id == "SUERTEPROXIMA")
                totalSuerte = delivered;

            // CSV row
            sb.AppendLine($"{id},{p.name},{delivered},{stockFinal}");
        }

        // Resumen final
        sb.AppendLine();
        sb.AppendLine("Resumen:");
        sb.AppendLine($"TotalRuns,{runs}");
        sb.AppendLine($"TotalSuerteProxima,{totalSuerte}");

        return sb.ToString();
    }


    // =========================================================
    //  Guardar archivo CSV
    // =========================================================
    string SaveCsvReport(string csv)
    {
        string dir = DataPaths.DataDir;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string fileName = $"Sim_{simulationDate}_mode{mode}_runs{runs}.csv";
        string fullPath = Path.Combine(dir, fileName);

        File.WriteAllText(fullPath, csv, Encoding.UTF8);
        return fullPath;
    }
}
