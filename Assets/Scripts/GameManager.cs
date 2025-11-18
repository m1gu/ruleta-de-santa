using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.IO;

public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Idle,
        Spinning,
        ShowingResult
    }

    public enum PrizeCategory
    {
        Small,
        Medium,
        Large
    }

    [System.Serializable]
    public class PrizeConfig
    {
        public string id;                 // identificador único (ej: "TAZAS")
        public string name;              // texto visible
        public PrizeCategory category;
        public float weight = 1f;
        public int initialStock = 10;
    }

    [Header("Referencias")]
    public RectTransform wheelRoot;
    public Transform pointer;
    public WheelBuilder wheelBuilder;

    [Header("Popup Premio")]
    public GameObject popupResultado;
    public TMP_Text popupText;

    [Header("Input")]
    public bool useMouseClick = true;
    public KeyCode startKey = KeyCode.Keypad8;

    [Header("Spin Settings")]
    public float spinDuration = 4f;
    public int extraFullSpinsMin = 3;
    public int extraFullSpinsMax = 5;

    [Header("Alignment")]
    public float pointerOffsetAngle = 0f;   // en grados, ajustable en el inspector

    [Header("Config externa")]
    public bool useExternalMode = true;    // ¿leer modo desde Data/modo.txt?
    public float modePollInterval = 10f;   // segundos entre lecturas
    [Tooltip("Modo actual efectivo (se actualiza desde modo.txt si useExternalMode=true)")]
    public int currentMode = 3;            // 1=Pequeños, 2=Grandes, 3=Normal

    [Header("Modo de Juego")]
    [Range(1, 3)]
    public int mode = 3;

    [Header("Modo 2 - Probabilidades por categoría")]
    [Range(0f, 1f)]
    public float mode2SmallProbability = 0.8f;   // 80% pequeños
    [Range(0f, 1f)]
    public float mode2MediumProbability = 0.1f;  // 10% medianos
    [Range(0f, 1f)]
    public float mode2LargeProbability = 0.1f;   // 10% grandes

    [Header("Premios (en el mismo orden que los segmentos)")]
    public List<PrizeConfig> prizes = new List<PrizeConfig>();

    // AUDIO >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip spinClip;    // spinner.mp3
    public AudioClip prizeClip;   // premio.mp3
    // AUDIO <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

    private int[] remainingStock;
    private GameState state = GameState.Idle;
    private int lastResultIndex = -1;
    private int indexSuerteProxima = -1;

    void Start()
    {
        // Mostrar fecha del sistema
        string today = System.DateTime.Today.ToString("yyyy-MM-dd");
        Debug.Log("[GameManager] Fecha del sistema detectada: " + today);

        if (popupResultado != null)
            popupResultado.SetActive(false);

        if (wheelRoot == null)
            Debug.LogError("GameManager: Asigna wheelRoot (WheelRoot RectTransform).");

        if (wheelBuilder == null)
            Debug.LogError("GameManager: Asigna wheelBuilder (en WheelRoot).");

        if (prizes == null)
            prizes = new List<PrizeConfig>();

        // AUDIO (opcional: aviso si falta algo)
        if (audioSource == null)
        {
            Debug.LogWarning("[GameManager] audioSource no asignado. No se reproducirá sonido.");
        }

        // 1) Cargar premios desde JSON (si existe)
        InventoryService.LoadPrizesFromJson(ref prizes);

        if (prizes.Count == 0)
        {
            Debug.LogError("GameManager: No hay premios configurados ni en el Inspector ni en premios.json.");
            return;
        }

        // 2) Stock base desde inventario.csv (si no existe, usa initialStock)
        int[] baseStock = InventoryService.LoadInventoryForToday(prizes);

        // 3) Aplicar estado guardado (state.json), si existe y es del día
        remainingStock = InventoryService.ApplySavedState(prizes, baseStock);

        // 4) Sincronizar ruleta con la lista de premios
        SyncWheelWithPrizes();

        // 5) Modo
        currentMode = Mathf.Clamp(mode, 1, 3);

        if (useExternalMode)
        {
            LoadModeFromFile();
            StartCoroutine(ModeWatcherCoroutine());
        }

        // Buscar el índice del premio "suerte para la próxima"
        indexSuerteProxima = -1;
        for (int i = 0; i < prizes.Count; i++)
        {
            if (!string.IsNullOrEmpty(prizes[i].id) &&
                string.Equals(prizes[i].id, "SUERTEPROXIMA", System.StringComparison.OrdinalIgnoreCase))
            {
                indexSuerteProxima = i;
                break;
            }
        }

        if (indexSuerteProxima >= 0)
        {
            Debug.Log("[GameManager] Premio SUERTEPROXIMA encontrado en índice: " + indexSuerteProxima);
        }
        else
        {
            Debug.LogWarning("[GameManager] No se encontró premio con id = SUERTEPROXIMA. El modo 'solo suerte' no podrá activarse.");
        }

        state = GameState.Idle;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (wheelBuilder == null) return;
        if (prizes == null || prizes.Count == 0) return;

        SyncWheelWithPrizes();
    }
#endif

    [ContextMenu("Sync Wheel From Prizes")]
    public void SyncWheelWithPrizes()
    {
        if (wheelBuilder == null) return;
        if (prizes == null || prizes.Count == 0) return;

        List<string> names = new List<string>();
        for (int i = 0; i < prizes.Count; i++)
        {
            names.Add(prizes[i].name);
        }

        wheelBuilder.SetPrizeNames(names);
    }

    void Update()
    {
        if (!Pressed()) return;

        switch (state)
        {
            case GameState.Idle:
                OnStartSpinRequested();
                break;
            case GameState.Spinning:
                break;
            case GameState.ShowingResult:
                HidePopup();
                state = GameState.Idle;
                break;
        }
    }

    bool Pressed()
    {
        return (useMouseClick && Input.GetMouseButtonDown(0)) || Input.GetKeyDown(startKey);
    }

    void OnStartSpinRequested()
    {
        if (remainingStock == null || remainingStock.Length == 0)
        {
            Debug.LogWarning("GameManager: remainingStock no inicializado.");
            return;
        }

        // 1) Calcular stock REAL (todos menos SUERTEPROXIMA)
        int totalRealStock = 0;
        for (int i = 0; i < remainingStock.Length; i++)
        {
            if (i == indexSuerteProxima) continue; // ignoramos "suerte para la próxima"
            totalRealStock += remainingStock[i];
        }

        // 2) Caso: ya no hay premios reales, pero sí tenemos SUERTEPROXIMA
        if (totalRealStock <= 0 && indexSuerteProxima >= 0)
        {
            if (remainingStock[indexSuerteProxima] <= 0)
            {
                Debug.LogWarning("GameManager: Solo queda SUERTEPROXIMA pero su stock es 0. No se puede continuar.");
                return;
            }

            Debug.Log("[GameManager] Sin stock de premios reales. Solo se entregará SUERTEPROXIMA.");
            StartCoroutine(SpinAndShow(indexSuerteProxima));
            return;
        }

        // 3) Caso: no hay NADA de stock (ni reales ni SUERTEPROXIMA)
        int totalStock = 0;
        for (int i = 0; i < remainingStock.Length; i++)
            totalStock += remainingStock[i];

        if (totalStock <= 0)
        {
            Debug.LogWarning("GameManager: Sin stock disponible de ningún premio (incluyendo SUERTEPROXIMA).");
            return;
        }

        // 4) Caso normal: hay premios reales → usar lógica normal de selección
        int chosenIndex = ChoosePrizeIndex();
        if (chosenIndex < 0)
        {
            Debug.LogWarning("GameManager: No se pudo elegir premio.");
            return;
        }

        StartCoroutine(SpinAndShow(chosenIndex));
    }

    int ChoosePrizeIndex()
    {
        List<int> small = new List<int>();
        List<int> medium = new List<int>();
        List<int> large = new List<int>();

        for (int i = 0; i < prizes.Count; i++)
        {
            if (remainingStock[i] <= 0) continue;

            switch (prizes[i].category)
            {
                case PrizeCategory.Small:
                    small.Add(i);
                    break;
                case PrizeCategory.Medium:
                    medium.Add(i);
                    break;
                case PrizeCategory.Large:
                    large.Add(i);
                    break;
            }
        }

        if (small.Count == 0 && medium.Count == 0 && large.Count == 0)
            return -1;

        int ChooseWeightedFrom(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return -1;

            float totalWeight = 0f;
            foreach (int idx in indices)
                totalWeight += Mathf.Max(0f, prizes[idx].weight);

            if (totalWeight <= 0f)
            {
                return indices[Random.Range(0, indices.Count)];
            }

            float r = Random.Range(0f, totalWeight);
            float accum = 0f;

            foreach (int idx in indices)
            {
                float w = Mathf.Max(0f, prizes[idx].weight);
                accum += w;
                if (r <= accum)
                    return idx;
            }

            return indices[indices.Count - 1];
        }

        switch (currentMode)
        {
            case 1:
                {
                    int idx = ChooseWeightedFrom(small);
                    if (idx >= 0) return idx;

                    List<int> all = new List<int>();
                    all.AddRange(small);
                    all.AddRange(medium);
                    all.AddRange(large);
                    return ChooseWeightedFrom(all);
                }

            case 2:
                {
                    bool hasSmall = small.Count > 0;
                    bool hasMedium = medium.Count > 0;
                    bool hasLarge = large.Count > 0;

                    float pSmall = hasSmall ? Mathf.Max(0f, mode2SmallProbability) : 0f;
                    float pMedium = hasMedium ? Mathf.Max(0f, mode2MediumProbability) : 0f;
                    float pLarge = hasLarge ? Mathf.Max(0f, mode2LargeProbability) : 0f;

                    float sum = pSmall + pMedium + pLarge;
                    if (sum <= 0f)
                    {
                        List<int> all = new List<int>();
                        all.AddRange(small);
                        all.AddRange(medium);
                        all.AddRange(large);
                        return ChooseWeightedFrom(all);
                    }

                    pSmall /= sum;
                    pMedium /= sum;
                    pLarge /= sum;

                    float r = Random.value;
                    List<int> chosenList = null;

                    if (r < pSmall)
                    {
                        chosenList = small;
                    }
                    else if (r < pSmall + pMedium)
                    {
                        chosenList = medium;
                    }
                    else
                    {
                        chosenList = large;
                    }

                    int idxCat = ChooseWeightedFrom(chosenList);
                    if (idxCat >= 0) return idxCat;

                    List<int> all2 = new List<int>();
                    all2.AddRange(small);
                    all2.AddRange(medium);
                    all2.AddRange(large);
                    return ChooseWeightedFrom(all2);
                }

            case 3:
            default:
                {
                    List<int> all = new List<int>();
                    all.AddRange(small);
                    all.AddRange(medium);
                    all.AddRange(large);
                    return ChooseWeightedFrom(all);
                }
        }
    }

    IEnumerator SpinAndShow(int prizeIndex)
    {
        state = GameState.Spinning;

        if (popupResultado != null)
            popupResultado.SetActive(false);

        // AUDIO: iniciar sonido de giro
        if (audioSource != null && spinClip != null)
        {
            audioSource.loop = true;
            audioSource.clip = spinClip;
            audioSource.Play();
        }

        if (wheelBuilder.angleCenters == null || wheelBuilder.angleCenters.Count == 0)
        {
            Debug.LogError("GameManager: wheelBuilder.angleCenters vacío. Haz 'Rebuild Wheel'.");
            yield break;
        }

        if (prizeIndex < 0 || prizeIndex >= wheelBuilder.angleCenters.Count)
        {
            Debug.LogError("GameManager: prizeIndex fuera de rango.");
            yield break;
        }

        float angleCenter = wheelBuilder.angleCenters[prizeIndex];
        float adjustedAngle = angleCenter + pointerOffsetAngle;
        float targetBaseAngle = -adjustedAngle;

        int extraSpins = Random.Range(extraFullSpinsMin, extraFullSpinsMax + 1);

        float startAngle = wheelRoot.localEulerAngles.z;
        float finalAngle = targetBaseAngle - 360f * extraSpins;

        float elapsed = 0f;
        float duration = Mathf.Max(0.1f, spinDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float eased = 1f - Mathf.Pow(1f - t, 3f);

            float current = Mathf.Lerp(startAngle, finalAngle, eased);
            wheelRoot.localRotation = Quaternion.Euler(0f, 0f, current);

            yield return null;
        }

        wheelRoot.localRotation = Quaternion.Euler(0f, 0f, targetBaseAngle);

        // AUDIO: terminar sonido de giro y reproducir sonido de premio
        if (audioSource != null)
        {
            audioSource.loop = false;
            if (audioSource.isPlaying)
                audioSource.Stop();

            if (prizeClip != null)
            {
                audioSource.PlayOneShot(prizeClip);
            }
        }

        if (prizeIndex != indexSuerteProxima)
        {
            remainingStock[prizeIndex] = Mathf.Max(0, remainingStock[prizeIndex] - 1);
        }

        lastResultIndex = prizeIndex;

        InventoryService.SaveState(prizes, remainingStock);

        ShowPopup(prizes[prizeIndex].name);

        state = GameState.ShowingResult;
    }

    void ShowPopup(string premioTexto)
    {
        if (popupText != null)
            popupText.text = premioTexto;

        if (popupResultado != null)
            popupResultado.SetActive(true);

        if (popupResultado != null)
            popupResultado.transform.SetAsLastSibling();
    }

    void HidePopup()
    {
        if (popupResultado != null)
            popupResultado.SetActive(false);
    }

    void LoadModeFromFile()
    {
        try
        {
            string path = DataPaths.ModeFilePath;

            if (!File.Exists(path))
            {
#if UNITY_EDITOR
                Debug.LogWarning("[GameManager] modo.txt no encontrado en " + path + ". Se mantiene currentMode=" + currentMode);
#endif
                return;
            }

            string txt = File.ReadAllText(path).Trim();

            if (int.TryParse(txt, out int newMode))
            {
                if (newMode >= 1 && newMode <= 3)
                {
                    if (newMode != currentMode)
                    {
                        int previous = currentMode;
                        currentMode = newMode;

#if UNITY_EDITOR
                        Debug.Log($"[GameManager] 🔄 Cambio de modo detectado: {previous} → {currentMode}");
#endif
                    }
                    else
                    {
#if UNITY_EDITOR
                        Debug.Log($"[GameManager] Modo leído ({newMode}) pero no cambió.");
#endif
                    }
                }
                else
                {
#if UNITY_EDITOR
                    Debug.LogWarning("[GameManager] Valor fuera de rango en modo.txt: " + txt + ". Debe ser 1, 2 o 3.");
#endif
                }
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("[GameManager] No se pudo parsear modo.txt: " + txt);
#endif
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[GameManager] Error leyendo modo.txt: " + ex.Message);
        }
    }

    IEnumerator ModeWatcherCoroutine()
    {
        while (useExternalMode)
        {
            LoadModeFromFile();
            yield return new WaitForSeconds(modePollInterval);
        }
    }
}
