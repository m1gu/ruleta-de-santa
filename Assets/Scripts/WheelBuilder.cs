using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WheelBuilder : MonoBehaviour
{
    [Header("Layout")]
    public RectTransform wheelRoot;
    public Sprite circleSprite;
    public Vector2 wheelSize = new Vector2(900, 900);
    [Range(0f, 1f)] public float labelRadius = 0.70f;
    public int segments = 28;

    [Header("Colors")]
    public Color colorA = new Color(0.85f, 0.05f, 0.07f);
    public Color colorB = Color.white;
    public Color labelColor = Color.black;

    [Header("Labels")]
    public TMP_FontAsset font;
    public int fontSize = 24;
    public float labelOffsetAngle = 0f;

    // La lista sigue siendo pública para otros scripts,
    // pero no hace falta editarla a mano en el inspector.
    [HideInInspector]
    public List<string> prizeNames = new List<string>();

    [Header("Debug")]
    public List<float> angleCenters = new List<float>();

    const string SEG_PREFIX = "Seg_";

    [ContextMenu("Rebuild Wheel")]
    public void Rebuild()
    {
        if (wheelRoot == null) wheelRoot = GetComponent<RectTransform>();
        if (wheelRoot == null)
        {
            Debug.LogError("WheelBuilder: Asigna wheelRoot (RectTransform).");
            return;
        }

        if (circleSprite == null)
        {
            Debug.LogError("WheelBuilder: Asigna circleSprite.");
            return;
        }

        if (segments <= 0) segments = 1;

        // Limpiar hijos previos
        for (int i = wheelRoot.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(wheelRoot.GetChild(i).gameObject);
        }

        // Configurar root
        wheelRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, wheelSize.x);
        wheelRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, wheelSize.y);
        wheelRoot.anchorMin = wheelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        wheelRoot.pivot = new Vector2(0.5f, 0.5f);
        wheelRoot.anchoredPosition = Vector2.zero;

        float sector = 360f / segments;
        float halfSectorRad = (sector * 0.5f) * Mathf.Deg2Rad;

        EnsurePrizeNames();

        angleCenters = new List<float>(segments);

        for (int i = 0; i < segments; i++)
        {
            // ---------------------------
            // 1) SEGMENTO
            // ---------------------------
            var segGO = new GameObject($"{SEG_PREFIX}{i:D2}", typeof(RectTransform), typeof(Image));
            var segRT = segGO.GetComponent<RectTransform>();
            segRT.SetParent(wheelRoot, false);

            segRT.anchorMin = segRT.anchorMax = new Vector2(0.5f, 0.5f);
            segRT.pivot = new Vector2(0.5f, 0.5f);
            segRT.sizeDelta = wheelSize;

            segRT.localRotation = Quaternion.Euler(0, 0, i * sector);

            var img = segGO.GetComponent<Image>();
            img.sprite = circleSprite;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillAmount = 1f / segments;
            img.fillClockwise = true;
            img.color = (i % 2 == 0) ? colorA : colorB;

            // ---------------------------
            // 2) ÁNGULO CENTRO
            // ---------------------------
            float angleCenter = (i + 0.5f) * sector;
            angleCenters.Add(Normalize360(angleCenter));

            // ---------------------------
            // 3) LABEL
            // ---------------------------
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lr = labelGO.GetComponent<RectTransform>();
            lr.SetParent(segRT, false);

            lr.sizeDelta = new Vector2(wheelSize.x * 0.6f, fontSize * 3f);

            float r = (wheelSize.x * 0.5f) * labelRadius;

            float localX = Mathf.Sin(halfSectorRad) * r;
            float localY = Mathf.Cos(halfSectorRad) * r;
            lr.anchoredPosition = new Vector2(localX, localY);

            lr.localRotation = Quaternion.Euler(0, 0, 90f + labelOffsetAngle);

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = prizeNames[i];
            tmp.font = font;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.color = labelColor;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.fontStyle = FontStyles.Bold;
        }

        Debug.Log("WheelBuilder: ruleta regenerada con " + segments + " segmentos.");
    }

    void EnsurePrizeNames()
    {
        if (prizeNames == null)
            prizeNames = new List<string>();

        if (prizeNames.Count == 0)
        {
            // Si no hay nombres, generamos genéricos
            prizeNames = new List<string>();
            for (int i = 0; i < segments; i++)
                prizeNames.Add("Premio " + (i + 1));
        }

        // Ajustar tamaño de la lista al número de segmentos
        if (prizeNames.Count < segments)
        {
            while (prizeNames.Count < segments)
                prizeNames.Add("Premio " + (prizeNames.Count + 1));
        }
        else if (prizeNames.Count > segments)
        {
            prizeNames.RemoveRange(segments, prizeNames.Count - segments);
        }
    }

    float Normalize360(float a)
    {
        a %= 360f;
        if (a < 0) a += 360f;
        return a;
    }

    // 🔥 Método público para que GameManager pase la lista de nombres
    public void SetPrizeNames(List<string> names)
    {
        if (names == null || names.Count == 0)
        {
            Debug.LogWarning("WheelBuilder: lista de nombres vacía en SetPrizeNames.");
            return;
        }

        segments = names.Count;
        prizeNames = new List<string>(names);

        Rebuild();
    }
}
