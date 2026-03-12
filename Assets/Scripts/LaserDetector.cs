using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LaserDetector : MonoBehaviour
{
    [Header("Camera")]
    public WebCamTexture sourceWebcam;
    public RawImage cameraRawImage;

    [Header("Detection")]
    public Slider thresholdSlider;
    [Range(0f, 1f)]
    public float threshold = 0.25f;

    [Header("Laser Power Levels")]
    public float lowPowerThreshold = 0.15f;
    public float highPowerThreshold = 0.2f;

    [Header("Noise Filter")]
    public int minBrightPixels = 8;
    public int persistenceFrames = 2;

    [Header("Grid")]
    public int gridX = 12;
    public int gridY = 8;

    [Header("UI")]
    public GameObject laserDotPrefab;
    public GameObject controlPointPrefab;

    [Header("Grid Debug")]
    public bool showGrid = true;
    public Color gridColor = new Color(1, 1, 1, 0.3f);

    [Header("Performance")]
    public int processEveryNFrames = 2;
    private GameObject ResetButton;
    public Button resetGridButton;

    TargetManager targetManager;
    RectTransform gridContainer;
    Color32[] pixels;
    int[,] persistenceCounter;
    RectTransform[,] controlPoints;
    List<Image> gridLines = new List<Image>();
    List<RectTransform> laserDots = new List<RectTransform>();

    struct GridCell
    {
        public int x;
        public int y;
        public float brightness;
        public GridCell(int gx, int gy, float b)
        {
            x = gx; y = gy; brightness = b;
        }
    }

    void Start()
    {
        pixels = new Color32[sourceWebcam.width * sourceWebcam.height];
        persistenceCounter = new int[gridX, gridY];
        targetManager = FindObjectOfType<TargetManager>();
        ResetButton = GameObject.FindGameObjectWithTag("Reset");
        resetGridButton = ResetButton.GetComponent<Button>();

        CreateGrid();
        LoadGridState();

        if (resetGridButton != null)
            resetGridButton.onClick.AddListener(ResetGrid);
    }

    void Update()
    {
        if (!sourceWebcam.isPlaying) return;
        if (Time.frameCount % processEveryNFrames != 0) return;
        if (thresholdSlider != null) threshold = thresholdSlider.value;

        DetectLaserGrid();
        UpdateTargetsPosition();
    }

    void CreateGrid()
    {
        if (controlPointPrefab == null)
        {
            controlPointPrefab = Resources.Load<GameObject>("ControlPointPrefab");
            if (controlPointPrefab == null)
            {
                Debug.LogError("找不到 ControlPointPrefab，請放到 Resources/ControlPointPrefab.prefab");
                return;
            }
        }

        GameObject gridObj = new GameObject("GridOverlay");
        gridObj.transform.SetParent(cameraRawImage.transform, false);

        gridContainer = gridObj.AddComponent<RectTransform>();
        gridContainer.anchorMin = Vector2.zero;
        gridContainer.anchorMax = Vector2.one;
        gridContainer.offsetMin = Vector2.zero;
        gridContainer.offsetMax = Vector2.zero;

        controlPoints = new RectTransform[gridX + 1, gridY + 1];

        Rect rect = cameraRawImage.rectTransform.rect;
        float stepX = rect.width / gridX;
        float stepY = rect.height / gridY;

        for (int y = 0; y <= gridY; y++)
        {
            for (int x = 0; x <= gridX; x++)
            {
                GameObject p = Instantiate(controlPointPrefab, gridContainer);
                RectTransform rt = p.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.anchoredPosition = new Vector2(x * stepX, y * stepY);

                GridDraggablePoint drag = p.GetComponent<GridDraggablePoint>();
                drag.detector = this;
                drag.gridX = x;
                drag.gridY = y;

                controlPoints[x, y] = rt;
            }
        }

        UpdateGridVisual();
    }

    public void UpdateGridVisual()
    {
        foreach (var l in gridLines)
            Destroy(l.gameObject);
        gridLines.Clear();

        for (int y = 0; y <= gridY; y++)
            for (int x = 0; x < gridX; x++)
                DrawLine(controlPoints[x, y], controlPoints[x + 1, y]);

        for (int x = 0; x <= gridX; x++)
            for (int y = 0; y < gridY; y++)
                DrawLine(controlPoints[x, y], controlPoints[x, y + 1]);
    }

    void DrawLine(RectTransform a, RectTransform b)
    {
        GameObject line = new GameObject("line");
        line.transform.SetParent(gridContainer, false);
        Image img = line.AddComponent<Image>();
        img.color = gridColor;

        RectTransform rt = img.rectTransform;
        Vector2 dir = b.anchoredPosition - a.anchoredPosition;
        float length = dir.magnitude;

        rt.sizeDelta = new Vector2(length, 2);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = a.anchoredPosition;
        rt.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        gridLines.Add(img);
    }

    Vector2 GetWarpedPosition(int gx, int gy)
    {
        RectTransform p00 = controlPoints[gx, gy];
        RectTransform p10 = controlPoints[gx + 1, gy];
        RectTransform p01 = controlPoints[gx, gy + 1];
        RectTransform p11 = controlPoints[gx + 1, gy + 1];

        return (p00.anchoredPosition + p10.anchoredPosition + p01.anchoredPosition + p11.anchoredPosition) / 4f;
    }

    void DetectLaserGrid()
    {
        int width = sourceWebcam.width;
        int height = sourceWebcam.height;
        sourceWebcam.GetPixels32(pixels);

        int cellW = width / gridX;
        int cellH = height / gridY;
        List<GridCell> brightCells = new List<GridCell>();

        for (int gy = 0; gy < gridY; gy++)
        {
            for (int gx = 0; gx < gridX; gx++)
            {
                float brightnessSum = 0;
                int sampleCount = 0;
                int brightPixelCount = 0;
                int startX = gx * cellW;
                int startY = gy * cellH;

                for (int y = 0; y < cellH; y += 2)
                    for (int x = 0; x < cellW; x += 2)
                    {
                        int px = startX + x;
                        int py = startY + y;
                        int idx = py * width + px;
                        float b = pixels[idx].r / 255f;
                        brightnessSum += b;
                        sampleCount++;
                        if (b > threshold) brightPixelCount++;
                    }

                if (sampleCount == 0) continue;
                float avgBrightness = brightnessSum / sampleCount;
                if (brightPixelCount < minBrightPixels) { persistenceCounter[gx, gy] = 0; continue; }

                persistenceCounter[gx, gy]++;
                if (persistenceCounter[gx, gy] < persistenceFrames) continue;

                brightCells.Add(new GridCell(gx, gy, avgBrightness));
            }
        }

        UpdateLaserDotsFromCells(brightCells);
    }

    void UpdateLaserDotsFromCells(List<GridCell> cells)
    {
        for (int i = cells.Count; i < laserDots.Count; i++)
            laserDots[i].gameObject.SetActive(false);

        for (int i = 0; i < cells.Count; i++)
        {
            GridCell cell = cells[i];
            Vector2 pos = GetWarpedPosition(cell.x, cell.y);

            RectTransform dot;
            if (i < laserDots.Count) { dot = laserDots[i]; dot.gameObject.SetActive(true); }
            else { dot = Instantiate(laserDotPrefab, cameraRawImage.transform).GetComponent<RectTransform>(); laserDots.Add(dot); }

            dot.anchorMin = Vector2.zero;
            dot.anchorMax = Vector2.zero;
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.anchoredPosition = pos;

            Image img = dot.GetComponent<Image>();
            img.color = cell.brightness > highPowerThreshold ? Color.green : Color.white;

            CheckTargetHit(cell);
        }
    }

    void CheckTargetHit(GridCell laserCell)
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag("Target");
        foreach (var t in targets)
        {
            if (!t.activeSelf) continue;
            TargetCell tc = t.GetComponent<TargetCell>();
            if (tc != null && tc.gridX == laserCell.x && tc.gridY == laserCell.y)
                targetManager.HitTarget(t);
        }
    }

    // =================================
    // Target 隨 Grid 動態更新
    // =================================
    public void UpdateTargetsPosition()
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag("Target");
        foreach (var t in targets)
        {
            TargetCell tc = t.GetComponent<TargetCell>();
            if (tc != null)
            {
                Vector2 pos = GetWarpedPosition(tc.gridX, tc.gridY);
                RectTransform rt = t.GetComponent<RectTransform>();
                rt.anchoredPosition = pos;
            }
        }
    }

    // =================================
    // Grid 保存/載入
    // =================================
    public void SaveGridState()
    {
        for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
            {
                string keyX = $"Grid_{x}_{y}_X";
                string keyY = $"Grid_{x}_{y}_Y";
                Vector2 pos = controlPoints[x, y].anchoredPosition;
                PlayerPrefs.SetFloat(keyX, pos.x);
                PlayerPrefs.SetFloat(keyY, pos.y);
            }
        PlayerPrefs.Save();
    }

    public void LoadGridState()
    {
        bool hasSaved = false;
        for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
            {
                string keyX = $"Grid_{x}_{y}_X";
                string keyY = $"Grid_{x}_{y}_Y";
                if (PlayerPrefs.HasKey(keyX) && PlayerPrefs.HasKey(keyY))
                {
                    hasSaved = true;
                    Vector2 pos = new Vector2(PlayerPrefs.GetFloat(keyX), PlayerPrefs.GetFloat(keyY));
                    controlPoints[x, y].anchoredPosition = pos;
                }
            }

        if (hasSaved) UpdateGridVisual();
    }

    public void ResetGrid()
    {
        Rect rect = cameraRawImage.rectTransform.rect;
        float stepX = rect.width / gridX;
        float stepY = rect.height / gridY;

        for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
                controlPoints[x, y].anchoredPosition = new Vector2(x * stepX, y * stepY);

        UpdateGridVisual();
        SaveGridState();
    }
}