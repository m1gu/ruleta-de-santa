using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class InventoryService
{
    public static string SimulatedDateOverride = null;
    

    [Serializable]
    public class PrizesFile
    {
        public PrizeEntry[] prizes;
    }

    [Serializable]
    public class PrizeEntry
    {
        public string id;
        public string name;
        public string category;
        public float weight;
        public int initialStock;
    }

    [Serializable]
    public class StateFile
    {
        public string date;
        public StatePrizeEntry[] prizes;
    }

    [Serializable]
    public class StatePrizeEntry
    {
        public string id;
        public int remaining;
    }

    // -------- premios.json --------
    public static void LoadPrizesFromJson(ref List<GameManager.PrizeConfig> prizes)
    {
        string path = DataPaths.PrizesFilePath;

        if (!File.Exists(path))
        {
#if UNITY_EDITOR
            Debug.LogWarning("[InventoryService] premios.json no encontrado. Se mantiene lo del Inspector.");
#endif
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            PrizesFile file = JsonUtility.FromJson<PrizesFile>(json);
            if (file == null || file.prizes == null || file.prizes.Length == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[InventoryService] premios.json vacío o mal formado.");
#endif
                return;
            }

            var list = new List<GameManager.PrizeConfig>();
            foreach (var p in file.prizes)
            {
                var cfg = new GameManager.PrizeConfig();
                cfg.id = p.id;
                cfg.name = p.name;
                cfg.weight = p.weight;
                cfg.initialStock = p.initialStock;

                if (Enum.TryParse<GameManager.PrizeCategory>(p.category, true, out var cat))
                    cfg.category = cat;
                else
                    cfg.category = GameManager.PrizeCategory.Small;

                list.Add(cfg);
            }

            prizes = list;

#if UNITY_EDITOR
            Debug.Log("[InventoryService] premios.json cargado. Total: " + prizes.Count);
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error leyendo premios.json: " + ex.Message);
        }
    }

        // -------- inventario.csv --------
    
    public static int[] LoadInventoryForToday(List<GameManager.PrizeConfig> prizes)
    {
        string today = ResolveCurrentDate();
        return LoadInventoryForDate(prizes, today);
    }

    public static int[] LoadInventoryForDate(List<GameManager.PrizeConfig> prizes, string date)
    {
        string path = DataPaths.InventoryFilePath;
        int[] result = new int[prizes.Count];

        if (!File.Exists(path))
        {
            Debug.LogError("[InventoryService] inventario.csv no encontrado en " + path + ". Todos los stocks serán 0.");
            return result;
        }

        try
        {
            string[] lines = File.ReadAllLines(path);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool foundDate = false;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split(',');
                if (parts.Length < 3) continue;

                string lineDate = parts[0].Trim();
                if (!lineDate.Equals(date, StringComparison.OrdinalIgnoreCase))
                    continue;

                foundDate = true;

                string lineId = parts[1].Trim();
                string qtyStr = parts[2].Trim();

                if (!int.TryParse(qtyStr, out int qty))
                {
                    Debug.LogWarning($"[InventoryService] No se pudo parsear la cantidad '{qtyStr}' para el premio '{lineId}' en la fecha {date}. Se asumirá 0.");
                    qty = 0;
                }

                map[lineId] = qty;
            }

            for (int i = 0; i < prizes.Count; i++)
            {
                string id = prizes[i].id;
                result[i] = map.TryGetValue(id, out int qty) ? qty : 0;
            }

            if (!foundDate)
            {
                Debug.LogError("[InventoryService] No existe inventario registrado en inventario.csv para la fecha " + date + ". Todos los stocks quedarán en 0.");
            }
#if UNITY_EDITOR
            else
            {
                int total = 0;
                foreach (int v in result) total += v;
                Debug.Log($"[InventoryService] Inventario para {date}: total={total}");
            }
#endif

            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error leyendo inventario.csv: " + ex.Message);
            return result;
        }
    }

// -------- state.json --------
    public static int[] ApplySavedState(List<GameManager.PrizeConfig> prizes, int[] baseStock)
    {
        string date = ResolveCurrentDate();
        string path = DataPaths.GetStateFilePath(date);
        if (!File.Exists(path))
        {
#if UNITY_EDITOR
            Debug.Log("[InventoryService] state para " + date + " no existe. Uso stock base.");
#endif
            return baseStock;
        }

        try
        {
            string json = File.ReadAllText(path);
            StateFile state = JsonUtility.FromJson<StateFile>(json);
            if (state == null || state.prizes == null || state.prizes.Length == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[InventoryService] state vacío. Uso stock base.");
#endif
                return baseStock;
            }

            if (!string.Equals(state.date, date, StringComparison.OrdinalIgnoreCase))
            {
#if UNITY_EDITOR
                Debug.Log("[InventoryService] state.json es de otro día (" + state.date + "), se ignora.");
#endif
                return baseStock;
            }

            var remainingById = new Dictionary<string, int>();
            foreach (var sp in state.prizes)
            {
                if (string.IsNullOrEmpty(sp.id)) continue;
                remainingById[sp.id] = Mathf.Max(0, sp.remaining);
            }

            int[] result = new int[baseStock.Length];
            baseStock.CopyTo(result, 0);

            for (int i = 0; i < prizes.Count; i++)
            {
                if (!string.IsNullOrEmpty(prizes[i].id) &&
                    remainingById.TryGetValue(prizes[i].id, out int rem))
                {
                    result[i] = rem;
                }
            }

#if UNITY_EDITOR
            Debug.Log("[InventoryService] state restaurado desde " + Path.GetFileName(path));
#endif

            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error leyendo state.json: " + ex.Message);
            return baseStock;
        }
    }

    public static void SaveState(List<GameManager.PrizeConfig> prizes, int[] remainingStock, string explicitDate = null)
    {
        try
        {
            string today = !string.IsNullOrEmpty(explicitDate) ? explicitDate : ResolveCurrentDate();

            StateFile state = new StateFile();
            state.date = today;
            state.prizes = new StatePrizeEntry[prizes.Count];

            for (int i = 0; i < prizes.Count; i++)
            {
                state.prizes[i] = new StatePrizeEntry
                {
                    id = prizes[i].id,
                    remaining = (i >= 0 && i < remainingStock.Length) ? remainingStock[i] : 0
                };
            }

            string json = JsonUtility.ToJson(state, true);
            string pathFile = DataPaths.GetStateFilePath(today);
            File.WriteAllText(pathFile, json);

#if UNITY_EDITOR
            Debug.Log("[InventoryService] state guardado en " + pathFile);
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error guardando state.json: " + ex.Message);
        }
    }
    static string ResolveCurrentDate()
    {
        return !string.IsNullOrEmpty(SimulatedDateOverride)
            ? SimulatedDateOverride
            : DateTime.Today.ToString("yyyy-MM-dd");
    }

}
