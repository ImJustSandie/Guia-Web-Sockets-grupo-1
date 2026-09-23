using UnityEngine;
using TMPro;

/// <summary>
/// UI del temporizador. Ubicar en un TMP_Text dentro del Canvas de MainScene.
/// Se auto-vincula a MatchTimerManager.Instance y muestra mm:ss.
/// </summary>
public class MatchTimerUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private string format = "{0:00}:{1:00}";
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color lowTimeColor = Color.red;
    [SerializeField] private float lowTimeThreshold = 30f;

    private void Awake()
    {
        if (timerText == null)
            timerText = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        TrySubscribe();
        InvokeRepeating(nameof(TrySubscribe), 0.5f, 0.5f);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(TrySubscribe));
        if (MatchTimerManager.Instance != null)
            MatchTimerManager.Instance.RemainingTimeChanged -= OnTimeChanged;
    }

    private void TrySubscribe()
    {
        if (MatchTimerManager.Instance == null) return;
        MatchTimerManager.Instance.RemainingTimeChanged -= OnTimeChanged;
        MatchTimerManager.Instance.RemainingTimeChanged += OnTimeChanged;
        CancelInvoke(nameof(TrySubscribe));
        OnTimeChanged(MatchTimerManager.Instance.RemainingTime);
    }

    private void OnTimeChanged(float remaining)
    {
        if (timerText == null) return;
        int t = Mathf.CeilToInt(remaining);
        int m = t / 60;
        int s = t % 60;
        timerText.text = string.Format(format, m, s);
        timerText.color = t <= lowTimeThreshold ? lowTimeColor : normalColor;
    }
}
