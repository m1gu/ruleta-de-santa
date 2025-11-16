using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class InventoryService
{
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
        int[] stock = new int[prizes.Count];
        for (int i = 0; i < prizes.Count; i++)
            stock[i] = Mathf.Max(0, prizes[i].initialStock);

        string path = DataPaths.InventoryFilePath;
        if (!File.Exists(path))
        {
#if UNITY_EDITOR
            Debug.LogWarning("[InventoryService] inventario.csv no encontrado. Uso initialStock.");
#endif
            return stock;
        }

        try
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            string[] lines = File.ReadAllLines(path);

            var todayQuantities = new Dictionary<string, int>();

            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                if (i == 0 && lines[i].StartsWith("date")) continue;

                var parts = lines[i].Split(',');
                if (parts.Length < 3) continue;

                string dateStr = parts[0].Trim();
                string prizeId = parts[1].Trim();
                string qtyStr = parts[2].Trim();

                if (!string.Equals(dateStr, today, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(qtyStr, out int qty)) continue;
                if (qty < 0) qty = 0;

                todayQuantities[prizeId] = qty;
            }

            for (int i = 0; i < prizes.Count; i++)
            {
                if (!string.IsNullOrEmpty(prizes[i].id) &&
                    todayQuantities.TryGetValue(prizes[i].id, out int qtyForToday))
                {
                    stock[i] = qtyForToday;
                }
            }

#if UNITY_EDITOR
            Debug.Log("[InventoryService] inventario.csv aplicado para " + today);
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error leyendo inventario.csv: " + ex.Message);
        }

        return stock;
    }

    // -------- state.json --------
    public static int[] ApplySavedState(List<GameManager.PrizeConfig> prizes, int[] baseStock)
    {
        string path = DataPaths.StateFilePath;
        if (!File.Exists(path))
        {
#if UNITY_EDITOR
            Debug.Log("[InventoryService] state.json no existe. Uso stock base.");
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
                Debug.LogWarning("[InventoryService] state.json vacío. Uso stock base.");
#endif
                return baseStock;
            }

            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (!string.Equals(state.date, today, StringComparison.OrdinalIgnoreCase))
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
            Debug.Log("[InventoryService] state.json aplicado. Inventario restaurado.");
#endif

            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error leyendo state.json: " + ex.Message);
            return baseStock;
        }
    }

    public static void SaveState(List<GameManager.PrizeConfig> prizes, int[] remainingStock)
    {
        try
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");

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
            File.WriteAllText(DataPaths.StateFilePath, json);

#if UNITY_EDITOR
            Debug.Log("[InventoryService] state.json guardado en " + DataPaths.StateFilePath);
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError("[InventoryService] Error guardando state.json: " + ex.Message);
        }
    }
}
