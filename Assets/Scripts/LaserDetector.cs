using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    public int gridX = 3;
    public int gridY = 3;
    public bool IsGridReady { get; private set; }

    [Header("UI")]
    public GameObject laserDotPrefab;
    public GameObject controlPointPrefab;

    [Header("Grid Debug")]
    public bool showGrid = true;
    public Color gridColor = new Color(1, 1, 1, 0.3f);

    [Header("Performance")]
    public int processEveryNFrames = 2;

    [Header("Reset Button")]
    public GameObject ResetButton;
    private Button resetGridButton;

    [Header("Events")]
    public UnityEvent<int, int, float> onGridHit;

    TargetManager targetManager;
    RectTransform gridContainer;
    Color32[] pixels;
    int[,] persistenceCounter;
    RectTransform[,] controlPoints;
    List<Image> gridLines = new List<Image>();
    List<RectTransform> laserDots = new List<RectTransform>();

    // PlayerPrefs key prefix（以 GameObject.name 為前綴，避免多個 Detector 互相覆寫）
    private string prefsPrefix;

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

    void Awake()
    {
        prefsPrefix = gameObject.name + "_LaserDetector_";
    }

    void Start()
    {
        // 先載入儲存的 grid 大小（如果有）
        LoadGridSize();

        // pixels 會在 DetectLaserGrid 裡檢查尺寸並重分配，這裡盡量避免 null 狀況
        pixels = new Color32[0];

        persistenceCounter = new int[gridX, gridY];
        targetManager = FindObjectOfType<TargetManager>();

        ResetButton = GameObject.FindGameObjectWithTag("Reset");
        if (ResetButton != null)
            resetGridButton = ResetButton.GetComponent<Button>();

        CreateGrid();
        LoadGridState();
        IsGridReady = true;

        if (resetGridButton != null)
            resetGridButton.onClick.AddListener(ResetGrid);
    }

    void Update()
    {
        if (sourceWebcam == null || !sourceWebcam.isPlaying) return;
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

    // 新增：公開方法，用於在執行期安全變更格子數
    public void SetGridSize(int newGridX, int newGridY)
    {
        newGridX = Mathf.Max(1, newGridX);
        newGridY = Mathf.Max(1, newGridY);
        if (newGridX == gridX && newGridY == gridY) return;

        gridX = newGridX;
        gridY = newGridY;

        // 儲存格子尺寸，立即生效並重建 grid
        SaveGridSize();
        RebuildGrid();
    }

    // 新增：重建格子與相關資料結構
    void RebuildGrid()
    {
        // 刪除舊的 grid container（包含 control points 與 grid lines）
        if (gridContainer != null)
        {
            Destroy(gridContainer.gameObject);
            gridContainer = null;
        }

        // 刪除並清空 laser dots（它們不是 gridContainer 的子物件）
        for (int i = laserDots.Count - 1; i >= 0; i--)
        {
            if (laserDots[i] != null)
                Destroy(laserDots[i].gameObject);
        }
        laserDots.Clear();

        // 清空 gridLines 參考（實體可能已被 parent 刪除）
        gridLines.Clear();

        // 清空 controlPoints 參考並重建 persistenceCounter
        controlPoints = null;
        persistenceCounter = new int[gridX, gridY];

        // 重新建立 grid（CreateGrid 會重建 control points）
        CreateGrid();

        // 載入儲存的 control point 位置（若有）
        LoadGridState();

        UpdateGridVisual();
        SaveGridState();

        IsGridReady = true;
    }

    public void UpdateGridVisual()
    {
        if (!showGrid) return;

        foreach (var l in gridLines)
            if (l != null) Destroy(l.gameObject);
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

    public void GetCellCorners(int gx, int gy, out Vector2 bl, out Vector2 br, out Vector2 tr, out Vector2 tl)
    {
        RectTransform p00 = controlPoints[gx, gy];
        RectTransform p10 = controlPoints[gx + 1, gy];
        RectTransform p01 = controlPoints[gx, gy + 1];
        RectTransform p11 = controlPoints[gx + 1, gy + 1];

        bl = p00.anchoredPosition;
        br = p10.anchoredPosition;
        tr = p11.anchoredPosition;
        tl = p01.anchoredPosition;
    }

    public Vector2 GetWarpedPosition(int gx, int gy)
    {
        GetCellCorners(gx, gy, out Vector2 bl, out Vector2 br, out Vector2 tr, out Vector2 tl);
        return (bl + br + tr + tl) / 4f;
    }

    void DetectLaserGrid()
    {
        int width = sourceWebcam.width;
        int height = sourceWebcam.height;
        if (width <= 0 || height <= 0) return;

        // 確保 pixels 陣列尺寸正確（防止 webcam 在不同時間點回傳不同解析度）
        if (pixels == null || pixels.Length != width * height)
            pixels = new Color32[width * height];

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
                        if (px < 0 || px >= width || py < 0 || py >= height) continue;
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
            if (i < laserDots.Count)
            {
                dot = laserDots[i];
                dot.gameObject.SetActive(true);
            }
            else
            {
                dot = Instantiate(laserDotPrefab, cameraRawImage.transform).GetComponent<RectTransform>();
                laserDots.Add(dot);
            }

            dot.anchorMin = Vector2.zero;
            dot.anchorMax = Vector2.zero;
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.anchoredPosition = pos;

            Image img = dot.GetComponent<Image>();
            img.color = cell.brightness > highPowerThreshold ? Color.green : Color.white;

            onGridHit?.Invoke(cell.x, cell.y, cell.brightness);

            // 光點進入格子即算命中，不需要碰到 Target
            targetManager.HitTargetByCell(cell.x, cell.y);
        }
    }

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
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
            }
        }
    }

    public void SaveGridState()
    {
        for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
            {
                string keyX = $"{prefsPrefix}Grid_{x}_{y}_X";
                string keyY = $"{prefsPrefix}Grid_{x}_{y}_Y";
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
                string keyX = $"{prefsPrefix}Grid_{x}_{y}_X";
                string keyY = $"{prefsPrefix}Grid_{x}_{y}_Y";
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

    // 儲存 / 載入 gridX / gridY
    void SaveGridSize()
    {
        PlayerPrefs.SetInt(prefsPrefix + "gridX", gridX);
        PlayerPrefs.SetInt(prefsPrefix + "gridY", gridY);
        PlayerPrefs.Save();
    }

    void LoadGridSize()
    {
        prefsPrefix = gameObject.name + "_LaserDetector_";
        if (PlayerPrefs.HasKey(prefsPrefix + "gridX"))
            gridX = Mathf.Max(1, PlayerPrefs.GetInt(prefsPrefix + "gridX"));
        if (PlayerPrefs.HasKey(prefsPrefix + "gridY"))
            gridY = Mathf.Max(1, PlayerPrefs.GetInt(prefsPrefix + "gridY"));
    }
}