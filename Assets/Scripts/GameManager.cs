using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

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
        public string name;
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


    [Header("Modo de Juego")]
    [Range(1, 3)]
    public int mode = 3;

    [Header("Premios (en el mismo orden que los segmentos)")]
    public List<PrizeConfig> prizes = new List<PrizeConfig>();

    private int[] remainingStock;
    private GameState state = GameState.Idle;
    private int lastResultIndex = -1;

    void Start()
    {
        if (popupResultado != null)
            popupResultado.SetActive(false);

        if (wheelRoot == null)
            Debug.LogError("GameManager: Asigna wheelRoot (WheelRoot RectTransform).");

        if (wheelBuilder == null)
            Debug.LogError("GameManager: Asigna wheelBuilder (en WheelRoot).");

        if (prizes == null || prizes.Count == 0)
            Debug.LogWarning("GameManager: La lista de prizes está vacía. Configúrala en el Inspector.");

        remainingStock = new int[prizes.Count];
        for (int i = 0; i < prizes.Count; i++)
        {
            remainingStock[i] = Mathf.Max(0, prizes[i].initialStock);
        }

        // Sincronizar ruleta con la lista de premios
        SyncWheelWithPrizes();

        state = GameState.Idle;
    }

#if UNITY_EDITOR
    // Se llama cada vez que cambias algo en el inspector (en modo editor)
    void OnValidate()
    {
        // Evitar errores si aún no se asigna wheelBuilder
        if (wheelBuilder == null) return;
        if (prizes == null || prizes.Count == 0) return;

        // Sincronizar automáticamente cuando cambies nombres en el inspector
        SyncWheelWithPrizes();
    }
#endif

    // Context menu para forzar la sincronización manualmente si lo necesitas
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
        int totalStock = 0;
        if (remainingStock == null || remainingStock.Length == 0)
        {
            Debug.LogWarning("GameManager: remainingStock no inicializado.");
            return;
        }

        for (int i = 0; i < remainingStock.Length; i++)
            totalStock += remainingStock[i];

        if (totalStock <= 0)
        {
            Debug.LogWarning("GameManager: Sin stock disponible de premios.");
            return;
        }

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
        List<int> candidates = new List<int>();

        for (int i = 0; i < prizes.Count; i++)
        {
            if (remainingStock[i] <= 0) continue;

            switch (mode)
            {
                case 1:
                    if (prizes[i].category != PrizeCategory.Small) continue;
                    break;
                case 2:
                    if (prizes[i].category != PrizeCategory.Large) continue;
                    break;
                case 3:
                    break;
            }

            candidates.Add(i);
        }

        if (candidates.Count == 0)
        {
            for (int i = 0; i < prizes.Count; i++)
            {
                if (remainingStock[i] > 0)
                    candidates.Add(i);
            }
        }

        if (candidates.Count == 0) return -1;

        float totalWeight = 0f;
        foreach (int idx in candidates)
            totalWeight += Mathf.Max(0f, prizes[idx].weight);

        if (totalWeight <= 0f)
        {
            return candidates[Random.Range(0, candidates.Count)];
        }

        float r = Random.Range(0f, totalWeight);
        float accum = 0f;

        foreach (int idx in candidates)
        {
            float w = Mathf.Max(0f, prizes[idx].weight);
            accum += w;
            if (r <= accum)
                return idx;
        }

        return candidates[candidates.Count - 1];
    }

    IEnumerator SpinAndShow(int prizeIndex)
    {
        state = GameState.Spinning;

        if (popupResultado != null)
            popupResultado.SetActive(false);

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

        // Ajuste por si el puntero no está exactamente en 0° arriba
        float adjustedAngle = angleCenter + pointerOffsetAngle;

        // Queremos que el centro del premio quede bajo el puntero (arriba),
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

        remainingStock[prizeIndex] = Mathf.Max(0, remainingStock[prizeIndex] - 1);
        lastResultIndex = prizeIndex;

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
}
