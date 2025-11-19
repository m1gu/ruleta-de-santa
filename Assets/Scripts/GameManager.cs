using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.IO;
using System.Globalization;

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

            [Header("Modo actual")]
    [Tooltip("Modo actual efectivo (controlado por teclado: 1, 2, 3)")]
    public int currentMode = 3;            // 1=Small, 2=Small+Medium, 3=Small+Medium+Large

    [Header("Modo Prueba")]
    [Tooltip("Activa una fecha simulada para leer inventario.csv de un dia diferente (solo pruebas).")]
    public bool useTestMode = false;
    [Tooltip("Fecha simulada en formato yyyy-MM-dd.")]
    public string testModeDate = "2025-11-20";

[Header("Probabilidades por categor�a (Modos 2 y 3)")]
    [Range(0f, 1f)]
    public float mode2SmallProbability = 0.8f;
    [Range(0f, 1f)]
    public float mode2MediumProbability = 0.2f;
    [Range(0f, 1f)]
    public float mode2LargeProbability = 0.0f;

    [Header("Premios (en el mismo orden que los segmentos)")]
    public List<PrizeConfig> prizes = new List<PrizeConfig>();

    // AUDIO >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip spinClip;    // spinner.mp3
    public AudioClip prizeClip;   // premio.mp3
    // AUDIO <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

    [Header("Pacing / Dosificación")]
    public bool useAdaptivePacing = true;
    [Tooltip("Giros esperados por día (para repartir premios)")]
    public int expectedSpinsPerDay = 500;
    [Range(0f, 1f)]
    public float minRealPrizeProbability = 0.10f;   // mínimo 10% de giros con premio real
    [Range(0f, 1f)]
    public float maxRealPrizeProbability = 0.90f;   // máximo 90% de giros con premio real
    [Range(0f, 1f)]
    public float pacingAdjustmentStrength = 0.5f;   // qué tan fuerte corrige según la hora
    [Tooltip("Curva acumulativa deseada de premios reales entregados en el día (0=inicio, 1=fin).")]
    public AnimationCurve realPrizeDistributionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Hora (24h) en la que inicia la jornada.")]
    public float dayStartHour = 11f;
    [Tooltip("Hora (24h) en la que finaliza la jornada.")]
    public float dayEndHour = 20f;

    [Header("Streak Limits")]
    [Tooltip("Maximo de premios reales consecutivos (0 = sin limite).")] public int maxRealPrizesInRow = 0;
    [Tooltip("Maximo de SuerteProxima consecutivas (0 = sin limite).")] public int maxSuerteInRow = 0;

    private int[] remainingStock;
    private GameState state = GameState.Idle;
    private int lastResultIndex = -1;
    private int indexSuerteProxima = -1;

    private int spinsToday = 0;
    private int initialRealStock = 0;   // stock real planificado para el día (sin SUERTEPROXIMA)
    private int dailyRealPrizeGoal = 0;
    private int consecutiveRealPrizes = 0;
    private int consecutiveSuerteResults = 0;
    private Texture2D modeIndicatorTexture;

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

        if (useTestMode)
        {
            if (System.DateTime.TryParseExact(testModeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                InventoryService.SimulatedDateOverride = testModeDate;
                Debug.Log("[GameManager] Modo de prueba activo. Simulando fecha de inventario: " + testModeDate);
            }
            else
            {
                Debug.LogError("[GameManager] testModeDate inválida (" + testModeDate + "). Se usará la fecha real del sistema.");
                InventoryService.SimulatedDateOverride = null;
            }
        }
        else
        {
            InventoryService.SimulatedDateOverride = null;
        }

        // 2) Stock base desde inventario.csv (si no existe o falla, quedará en 0 y se registrará el error)
        int[] baseStock = InventoryService.LoadInventoryForToday(prizes);

        // 2.1) Buscar el índice del premio "suerte para la próxima"
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

        // 2.2) Calcular stock real inicial del día (sin SUERTEPROXIMA)
        initialRealStock = 0;
        for (int i = 0; i < baseStock.Length; i++)
        {
            if (i == indexSuerteProxima) continue;
            initialRealStock += Mathf.Max(0, baseStock[i]);
        }
        Debug.Log("[GameManager] Stock real inicial del día (sin SUERTEPROXIMA): " + initialRealStock);
        dailyRealPrizeGoal = initialRealStock;

        // 3) Aplicar estado guardado (state.json) solo en corridas reales
        if (useTestMode)
        {
            remainingStock = baseStock;
        }
        else
        {
            remainingStock = InventoryService.ApplySavedState(prizes, baseStock);
        }

        // 4) Sincronizar ruleta con la lista de premios
        SyncWheelWithPrizes();

        SetCurrentMode(Mathf.Clamp(currentMode, 1, 3));
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
        HandleModeHotkeys();

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

    void HandleModeHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            SetCurrentMode(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            SetCurrentMode(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            SetCurrentMode(3);
        }
    }

    void SetCurrentMode(int newMode)
    {
        newMode = Mathf.Clamp(newMode, 1, 3);
        if (newMode == currentMode)
            return;

        currentMode = newMode;
#if UNITY_EDITOR
        Debug.Log("[GameManager] Modo cambiado manualmente a: " + currentMode);
#endif
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
            spinsToday++;
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

        // 4) A partir de aquí sí habrá un giro
        spinsToday++;

        bool hasRealStock = totalRealStock > 0;
        bool hasSuerteAvailable = (indexSuerteProxima >= 0 && remainingStock[indexSuerteProxima] > 0);
        bool forceRealByStreak = maxSuerteInRow > 0 && consecutiveSuerteResults >= maxSuerteInRow && hasRealStock;
        bool forceSuerteByStreak = maxRealPrizesInRow > 0 && consecutiveRealPrizes >= maxRealPrizesInRow && hasSuerteAvailable;

        if (forceRealByStreak)
        {
            int forcedIndex = ChoosePrizeIndex();
            if (forcedIndex >= 0)
            {
                StartCoroutine(SpinAndShow(forcedIndex));
                return;
            }
            else if (hasSuerteAvailable)
            {
                StartCoroutine(SpinAndShow(indexSuerteProxima));
                return;
            }
        }

        if (forceSuerteByStreak)
        {
            StartCoroutine(SpinAndShow(indexSuerteProxima));
            return;
        }

        // 4.1) Pacing adaptativo: decidir si este giro da premio real o SUERTEPROXIMA
        if (useAdaptivePacing)
        {
            float pReal = ComputeRealPrizeProbability(totalRealStock);
            float r = Random.value;

            if (r > pReal)
            {
                // Giro sin premio real -> SUERTEPROXIMA, si está disponible
                if (indexSuerteProxima >= 0 && remainingStock[indexSuerteProxima] > 0)
                {
                    Debug.Log($"[GameManager] Giro sin premio real (pReal={pReal:F2}). Se entrega SUERTEPROXIMA.");
                    StartCoroutine(SpinAndShow(indexSuerteProxima));
                    return;
                }
                else
                {
                    Debug.Log("[GameManager] Giro sin premio real, pero SUERTEPROXIMA no disponible. Se intentará premio real.");
                }
            }
        }

        // 5) Caso normal: hay premios reales → usar lógica normal de selección
        int chosenIndex = ChoosePrizeIndex();
        if (chosenIndex < 0)
        {
            Debug.LogWarning("GameManager: No se pudo elegir premio real. Se intentará SUERTEPROXIMA si existe.");

            if (indexSuerteProxima >= 0 && remainingStock[indexSuerteProxima] > 0)
            {
                StartCoroutine(SpinAndShow(indexSuerteProxima));
            }
            return;
        }

        StartCoroutine(SpinAndShow(chosenIndex));
    }

    // Calcula probabilidad de dar premio real en este giro
    float ComputeRealPrizeProbability(int totalRealStock)
    {
        if (!useAdaptivePacing)
            return 1f;

        if (dailyRealPrizeGoal <= 0 || expectedSpinsPerDay <= 0)
            return maxRealPrizeProbability;

        float baseProb = Mathf.Clamp01((float)dailyRealPrizeGoal / Mathf.Max(1f, expectedSpinsPerDay));

        float dayProgress = GetDayProgress01();
        float expectedRatio = Mathf.Clamp01(realPrizeDistributionCurve != null
            ? realPrizeDistributionCurve.Evaluate(dayProgress)
            : dayProgress);

        int deliveredReal = Mathf.Clamp(dailyRealPrizeGoal - totalRealStock, 0, dailyRealPrizeGoal);
        float expectedDelivered = expectedRatio * dailyRealPrizeGoal;
        float diff = expectedDelivered - deliveredReal;
        float normalizedDiff = diff / Mathf.Max(1f, dailyRealPrizeGoal);

        float adjustedProb = baseProb * (1f + pacingAdjustmentStrength * normalizedDiff);
        return Mathf.Clamp(adjustedProb, minRealPrizeProbability, maxRealPrizeProbability);
    }

    float GetDayProgress01()
    {
        if (dayEndHour <= dayStartHour)
            return 1f;

        System.DateTime now = System.DateTime.Now;
        System.DateTime today = System.DateTime.Today;
        System.DateTime start = today.AddHours(dayStartHour);
        System.DateTime end = today.AddHours(dayEndHour);

        double totalSeconds = (end - start).TotalSeconds;
        if (totalSeconds <= 0)
            return 1f;

        if (now <= start) return 0f;
        if (now >= end) return 1f;

        return (float)((now - start).TotalSeconds / totalSeconds);
    }

    int ChoosePrizeIndex()
    {
        List<int> small = new List<int>();
        List<int> medium = new List<int>();
        List<int> large = new List<int>();

        for (int i = 0; i < prizes.Count; i++)
        {
            if (i == indexSuerteProxima) continue;
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

        switch (currentMode)
        {
            // Modo 1: SOLO premios Small
            case 1:
                {
                    int idx = ChooseWeightedFrom(small);
                    if (idx >= 0) return idx;

                    // Si no hay Small, devolvemos -1 y dejaremos que OnStartSpinRequested
                    // haga fallback a SUERTEPROXIMA (o nada).
                    return -1;
                }

            // Modo 2: Small + Medium con probabilidades configurables
            case 2:
                {
                    return ChoosePrizeByCategory(small, medium, large, includeLarge: false);
                }

            // Modo 3: Small + Medium + Large con probabilidades por categor�a
            case 3:
            default:
                {
                    return ChoosePrizeByCategory(small, medium, large, includeLarge: true);
                }
        }
    }

    int ChoosePrizeByCategory(List<int> smallList, List<int> mediumList, List<int> largeList, bool includeLarge)
    {
        bool hasSmall = smallList.Count > 0;
        bool hasMedium = mediumList.Count > 0;
        bool hasLarge = includeLarge && largeList.Count > 0;

        float pSmall = hasSmall ? Mathf.Max(0f, mode2SmallProbability) : 0f;
        float pMedium = hasMedium ? Mathf.Max(0f, mode2MediumProbability) : 0f;
        float pLarge = hasLarge ? Mathf.Max(0f, mode2LargeProbability) : 0f;

        float sum = pSmall + pMedium + pLarge;
        if (sum <= 0f)
        {
            List<int> fallback = new List<int>();
            if (hasSmall) fallback.AddRange(smallList);
            if (hasMedium) fallback.AddRange(mediumList);
            if (hasLarge) fallback.AddRange(largeList);
            return ChooseWeightedFrom(fallback);
        }

        float r = Random.value * sum;
        List<int> chosenList = null;

        if (r < pSmall && hasSmall)
            chosenList = smallList;
        else if (r < pSmall + pMedium && hasMedium)
            chosenList = mediumList;
        else if (hasLarge)
            chosenList = largeList;
        else if (hasSmall)
            chosenList = smallList;
        else if (hasMedium)
            chosenList = mediumList;
        else
            chosenList = largeList;

        int idx = ChooseWeightedFrom(chosenList);
        if (idx >= 0) return idx;

        List<int> fallbackAll = new List<int>();
        if (hasSmall) fallbackAll.AddRange(smallList);
        if (hasMedium) fallbackAll.AddRange(mediumList);
        if (hasLarge) fallbackAll.AddRange(largeList);
        return ChooseWeightedFrom(fallbackAll);
    }

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

        if (!useTestMode)
        {
            InventoryService.SaveState(prizes, remainingStock);
        }

        ShowPopup(prizes[prizeIndex].name);
        UpdateStreakCounters(prizeIndex);

        state = GameState.ShowingResult;
    }

    void UpdateStreakCounters(int prizeIndex)
    {
        if (indexSuerteProxima >= 0 && prizeIndex == indexSuerteProxima)
        {
            consecutiveSuerteResults++;
            consecutiveRealPrizes = 0;
        }
        else
        {
            consecutiveRealPrizes++;
            consecutiveSuerteResults = 0;
        }
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

    void OnGUI()
    {
        if (modeIndicatorTexture == null)
        {
            modeIndicatorTexture = new Texture2D(1, 1);
            modeIndicatorTexture.SetPixel(0, 0, Color.white);
            modeIndicatorTexture.Apply();
        }

        Rect rect = GetIndicatorRect();
        GUI.DrawTexture(rect, modeIndicatorTexture);
    }

    Rect GetIndicatorRect()
    {
        float size = 10f;
        float padding = 5f;

        switch (currentMode)
        {
            case 1:
                return new Rect(padding, padding, size, size);
            case 2:
                return new Rect(Screen.width - size - padding, padding, size, size);
            case 3:
            default:
                return new Rect(padding, Screen.height - size - padding, size, size);
        }
    }
}
