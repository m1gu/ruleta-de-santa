using System;
using System.Collections.Generic;
using UnityEngine;

public class PrizeSelector
{
    private List<GameManager.PrizeConfig> prizes;
    private int[] remaining;
    private int indexSuerte;

    private int initialRealStock;
    private int spinsDone = 0;
    private bool hasManualDayProgress = false;
    private float manualDayProgress = 0f;
    private int consecutiveRealResults = 0;
    private int consecutiveSuerteResults = 0;

    // Parámetros de pacing
    public float minRealProb = 0.10f;
    public float maxRealProb = 0.90f;
    public int maxRealStreak = 0;
    public int maxSuerteStreak = 0;
    public AnimationCurve realPrizeDistributionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Range(0f, 1f)]
    public float distributionAdjustmentStrength = 0.5f;
    public int dailyRealPrizeGoal = 0;

    // Total de corridas planificadas para el día (lo fija el simulador)
    public int expectedSpinsPerDay = 500;
    public int totalPlannedSpins = 500;

    public PrizeSelector(List<GameManager.PrizeConfig> p, int[] baseStock, int idxSuerte)
    {
        prizes = p;
        indexSuerte = idxSuerte;

        remaining = new int[baseStock.Length];
        Array.Copy(baseStock, remaining, baseStock.Length);

        // Stock real inicial (sin SUERTEPROXIMA)
        initialRealStock = 0;
        for (int i = 0; i < remaining.Length; i++)
        {
            if (i == indexSuerte) continue;
            initialRealStock += remaining[i];
        }

        dailyRealPrizeGoal = initialRealStock;
    }

    public void SetManualDayProgress(float progress01)
    {
        manualDayProgress = Mathf.Clamp01(progress01);
        hasManualDayProgress = true;
    }

    /// <summary>
    /// Ejecuta una corrida (un giro) sin animación.
    /// Devuelve el índice del premio en la lista de prizes.
    /// </summary>
    public int RunOneSpin(int mode)
    {
        spinsDone++;

        // 1) Stock REAL restante (sin SUERTEPROXIMA)
        int totalRealStock = 0;
        for (int i = 0; i < remaining.Length; i++)
        {
            if (i == indexSuerte) continue;
            totalRealStock += remaining[i];
        }

        bool hasSuerteAvailable = indexSuerte >= 0;

        // 2) Si NO hay premios reales pero si SUERTEPROXIMA - solo suerte (infinito)
        if (totalRealStock <= 0)
        {
            if (hasSuerteAvailable)
            {
                UpdateStreak(false);
                return indexSuerte;
            }

            return -1; // caso extremo
        }

        // 3) Cuantos spins faltan en el dia (para el simulador)
        int remainingSpins = Mathf.Max(1, totalPlannedSpins - spinsDone + 1);

        // Si hay MAS premios reales que spins restantes,
        // debemos dar SIEMPRE premio real para no dejar stock sin usar.
        bool forceReal = totalRealStock >= remainingSpins;
        bool forceRealByStreak = maxSuerteStreak > 0 && consecutiveSuerteResults >= maxSuerteStreak;
        bool forceSuerteByStreak = maxRealStreak > 0 && consecutiveRealResults >= maxRealStreak && hasSuerteAvailable;

        if (forceRealByStreak)
        {
            int forcedIdx = ChoosePrize(mode);
            if (forcedIdx >= 0 && forcedIdx != indexSuerte)
            {
                remaining[forcedIdx] = Mathf.Max(0, remaining[forcedIdx] - 1);
                UpdateStreak(true);
                return forcedIdx;
            }
        }

        if (forceSuerteByStreak)
        {
            UpdateStreak(false);
            return indexSuerte;
        }

        // 4) Si no estamos en modo "forzar premio real", usar pacing normal
        if (!forceReal)
        {
            float pReal = ComputePacing(totalRealStock);
            float r = UnityEngine.Random.value;

            // Si este giro NO dara premio real - SUERTEPROXIMA (sin gastar stock)
            if (r > pReal && hasSuerteAvailable)
            {
                UpdateStreak(false);
                return indexSuerte;
            }
        }

        // 5) Elegir premio real segun modo (1,2,3)
        int idx = ChoosePrize(mode);

        if (idx >= 0 && idx != indexSuerte)
        {
            // Descontar stock SOLO de premios reales
            remaining[idx] = Mathf.Max(0, remaining[idx] - 1);
            UpdateStreak(true);
            return idx;
        }

        // 6) Fallback: si algo falla al elegir real, devolvemos SUERTEPROXIMA si existe
        if (hasSuerteAvailable)
        {
            UpdateStreak(false);
            return indexSuerte;
        }

        return -1;
    }

    /// <summary>
    /// Probabilidad de premio real en función del progreso del día.
    /// </summary>
    float ComputePacing(int remainingReal)
    {
        if (dailyRealPrizeGoal <= 0 || expectedSpinsPerDay <= 0)
            return maxRealProb;

        float dayProgress = GetCurrentDayProgress();
        float expectedRatio = Mathf.Clamp01(realPrizeDistributionCurve != null
            ? realPrizeDistributionCurve.Evaluate(dayProgress)
            : dayProgress);

        int deliveredReal = Mathf.Clamp(dailyRealPrizeGoal - remainingReal, 0, dailyRealPrizeGoal);
        float expectedDelivered = expectedRatio * dailyRealPrizeGoal;
        float diff = expectedDelivered - deliveredReal;
        float normalizedDiff = diff / Mathf.Max(1f, dailyRealPrizeGoal);

        float baseProb = Mathf.Clamp01((float)dailyRealPrizeGoal / Mathf.Max(1f, expectedSpinsPerDay));
        float p = baseProb * (1f + distributionAdjustmentStrength * normalizedDiff);

        return Mathf.Clamp(p, minRealProb, maxRealProb);
    }

    /// <summary>
    /// Calcula el progreso del día utilizado para el pacing.
    /// </summary>
    float GetCurrentDayProgress()
    {
        if (hasManualDayProgress)
        {
            hasManualDayProgress = false;
            return manualDayProgress;
        }

        float progress = (float)spinsDone / Mathf.Max(1, expectedSpinsPerDay);
        return Mathf.Clamp01(progress);
    }

    /// Elige índice de premio real (NO incluye SUERTEPROXIMA) según el modo.
    /// Modo 1: Small
    /// Modo 2: Small + Medium
    /// Modo 3: Small + Medium + Large
    /// </summary>
    int ChoosePrize(int mode)
    {
        List<int> S = new List<int>();
        List<int> M = new List<int>();
        List<int> L = new List<int>();

        for (int i = 0; i < prizes.Count; i++)
        {
            if (i == indexSuerte) continue;
            if (remaining[i] <= 0) continue;

            switch (prizes[i].category)
            {
                case GameManager.PrizeCategory.Small:
                    S.Add(i);
                    break;
                case GameManager.PrizeCategory.Medium:
                    M.Add(i);
                    break;
                case GameManager.PrizeCategory.Large:
                    L.Add(i);
                    break;
            }
        }

        if (S.Count == 0 && M.Count == 0 && L.Count == 0)
            return -1;

        switch (mode)
        {
            case 1: // SOLO Small
                return WeightedPick(S);

            case 2: // Small + Medium
                {
                    List<int> SM = new List<int>();
                    SM.AddRange(S);
                    SM.AddRange(M);
                    return WeightedPick(SM);
                }

            case 3: // Small + Medium + Large
            default:
                {
                    List<int> ALL = new List<int>();
                    ALL.AddRange(S);
                    ALL.AddRange(M);
                    ALL.AddRange(L);
                    return WeightedPick(ALL);
                }
        }
    }

    /// <summary>
    /// Elige un índice de la lista según el weight configurado en prizes.
    /// </summary>
    int WeightedPick(List<int> list)
    {
        if (list == null || list.Count == 0) return -1;

        float total = 0f;
        foreach (int idx in list)
            total += Mathf.Max(0f, prizes[idx].weight);

        if (total <= 0f)
            return list[UnityEngine.Random.Range(0, list.Count)];

        float r = UnityEngine.Random.Range(0f, total);
        float acc = 0f;

        foreach (int idx in list)
        {
            acc += Mathf.Max(0f, prizes[idx].weight);
            if (r <= acc) return idx;
        }

        return list[list.Count - 1];
    }

    void UpdateStreak(bool realResult)
    {
        if (realResult)
        {
            consecutiveRealResults++;
            consecutiveSuerteResults = 0;
        }
        else
        {
            consecutiveSuerteResults++;
            consecutiveRealResults = 0;
        }
    }
}
