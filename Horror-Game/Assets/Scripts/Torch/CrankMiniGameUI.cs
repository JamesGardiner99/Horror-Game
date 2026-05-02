using UnityEngine;

public class CrankMiniGameUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RectTransform marker;
    [SerializeField] private RectTransform bar;
    [SerializeField] private RectTransform goodZone;
    [SerializeField] private RectTransform perfectZone;

    [Header("Settings")]
    [SerializeField] private float markerMinSpeed = 300f;
    [SerializeField] private float markerMaxSpeed = 600f;
    private float currentSpeed;
    [SerializeField] private float perfectZoneSize = 0.12f;
    [SerializeField] private float goodZoneSize = 0.35f;



    private bool isActive;
    private float markerPosition = -1f;
    private int direction = 1;

    public bool IsActive => isActive;

    private void Awake()
    {
        Hide();
    }

    private void Update()
    {
        if (!isActive)
            return;

        markerPosition += direction * currentSpeed * Time.deltaTime / GetHalfBarWidth();

        if (markerPosition >= 1f)
        {
            markerPosition = 1f;
            direction = -1;
        }
        else if (markerPosition <= -1f)
        {
            markerPosition = -1f;
            direction = 1;
        }

        UpdateMarkerVisual();
    }

    public void Show()
    {
        isActive = true;

        markerPosition = Random.Range(-1f, 1f);
        direction = Random.value > 0.5f ? 1 : -1;
        currentSpeed = Random.Range(markerMinSpeed, markerMaxSpeed);

        RandomiseZones();

        if (panel != null)
            panel.SetActive(true);

        UpdateMarkerVisual();
    }

    public void Hide()
    {
        isActive = false;

        if (panel != null)
            panel.SetActive(false);
    }

    public CrankResult Submit()
    {
        float markerX = markerPosition * GetHalfBarWidth();

        float perfectCenter = perfectZone.anchoredPosition.x;
        float goodCenter = goodZone.anchoredPosition.x;

        float perfectHalfWidth = perfectZone.rect.width * 0.5f;
        float goodHalfWidth = goodZone.rect.width * 0.5f;

        Hide();

        if (Mathf.Abs(markerX - perfectCenter) <= perfectHalfWidth)
            return CrankResult.Perfect;

        if (Mathf.Abs(markerX - goodCenter) <= goodHalfWidth)
            return CrankResult.Good;

        return CrankResult.Miss;
    }

    private void UpdateMarkerVisual()
    {
        if (marker == null)
            return;

        float x = markerPosition * GetHalfBarWidth();
        marker.anchoredPosition = new Vector2(x, marker.anchoredPosition.y);
    }

    private float GetHalfBarWidth()
    {
        if (bar == null)
            return 150f;

        return bar.rect.width * 0.5f;
    }

    private void RandomiseZones()
    {
        float halfBar = GetHalfBarWidth();

        float safePadding = halfBar * 0.6f;
        float randomCenter = Random.Range(-safePadding, safePadding);

        goodZone.anchoredPosition = new Vector2(randomCenter, 0);
        perfectZone.anchoredPosition = new Vector2(randomCenter, 0);
    }
}

public enum CrankResult
{
    Miss,
    Good,
    Perfect
}