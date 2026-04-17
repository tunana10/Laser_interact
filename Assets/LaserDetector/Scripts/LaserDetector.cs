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
    public float threshold = 0.2f;

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
    // 修改：加入 isHighPower(bool) 作為第四個參數
    public UnityEvent<int, int, float, bool> onGridHit;

    TargetManager targetManager;
    RectTransform gridContainer;
    Color32[] pixels;
    int[,] persistenceCounter;
    RectTransform[,] controlPoints;
    List<Image> gridLines = new List<Image>();
    List<RectTransform> laserDots = new List<RectTransform>();

    // PlayerPrefs key prefix（以 GameObject.name 為前綴，避免多個 Detector 互相覆寫）
    private string prefsPrefix;

    // threshold slider listener 存放以便移除
    private UnityAction<float> thresholdListener;

    public bool TryScreenPointToGridLocalPoint(Vector2 screenPoint, Camera uiCamera, out Vector2 localPoint)
    {
        if (gridContainer == null)
        {
            localPoint = Vector2.zero;
            return false;
        }
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(gridContainer, screenPoint, uiCamera, out localPoint);
    }

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
        // 載入上次儲存的 threshold 與 grid 大小（如有）
        LoadGridSize();
        LoadThreshold();

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

        // 使用 Slider 的 onValueChanged 更新 threshold 並儲存（避免每幀覆寫）
        if (thresholdSlider != null)
        {
            // 將 slider 初始化為目前 threshold
            thresholdSlider.value = threshold;
            thresholdListener = v =>
            {
                threshold = v;
                SaveThreshold();
            };
            thresholdSlider.onValueChanged.AddListener(thresholdListener);
        }
    }

    void OnDestroy()
    {
        if (thresholdSlider != null && thresholdListener != null)
            thresholdSlider.onValueChanged.RemoveListener(thresholdListener);
    }

    void Update()
    {
        if (sourceWebcam == null || !sourceWebcam.isPlaying) return;
        if (Time.frameCount % processEveryNFrames != 0) return;

        // threshold 不再每幀從 Slider 覆寫，改由 listener 更新並儲存。

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
        int texW = sourceWebcam.width;
        int texH = sourceWebcam.height;
        if (texW <= 0 || texH <= 0) return;

        // 確保 pixels 陣列尺寸正確（防止 webcam 在不同時間點回傳不同解析度）
        if (pixels == null || pixels.Length != texW * texH)
            pixels = new Color32[texW * texH];

        sourceWebcam.GetPixels32(pixels);

        // fallback：若 controlPoints 尚未建立或尺寸不符，維持原本行為（安全）
        if (controlPoints == null || controlPoints.GetLength(0) != gridX + 1 || controlPoints.GetLength(1) != gridY + 1)
        {
            int cellW = texW / gridX;
            int cellH = texH / gridY;
            List<GridCell> brightCells = new List<GridCell>();

            for (int gy = 0; gy < gridY; gy++)
            {
                for (int gx = 0; gx < gridX; gx++)
                {
                    float brightnessSum = 0f;
                    int sampleCount = 0;
                    int brightPixelCount = 0;
                    int startX = gx * cellW;
                    int startY = gy * cellH;

                    for (int y = 0; y < cellH; y += 2)
                        for (int x = 0; x < cellW; x += 2)
                        {
                            int px = startX + x;
                            int py = startY + y;
                            if (px < 0 || px >= texW || py < 0 || py >= texH) continue;
                            int idx = py * texW + px;
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
            return;
        }

        // 取得 Canvas / UI camera（ScreenSpaceOverlay 時為 null）
        Canvas canvas = cameraRawImage.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        // raw image 的本地 rect（UI 空間）
        Rect rawRect = cameraRawImage.rectTransform.rect;
        float rawW = rawRect.width;
        float rawH = rawRect.height;

        // 使用影像的實際像素尺寸（以 sourceWebcam 為準）
        // 計算顯示在 RawImage 內的影像區域（保持長寬比）
        float scale = Mathf.Min(rawW / (float)texW, rawH / (float)texH);
        float dispW = texW * scale;
        float dispH = texH * scale;
        float offsetX = (rawW - dispW) * 0.5f;
        float offsetY = (rawH - dispH) * 0.5f;

        List<GridCell> brightWarped = new List<GridCell>();

        for (int gy = 0; gy < gridY; gy++)
        {
            for (int gx = 0; gx < gridX; gx++)
            {
                // 四角 control points -> 轉成 texture pixel 座標（0..texW,0..texH）
                Vector2 bl = ControlPointToTexturePixel(controlPoints[gx, gy], uiCam, rawRect, texW, texH, dispW, dispH, offsetX, offsetY);
                Vector2 br = ControlPointToTexturePixel(controlPoints[gx + 1, gy], uiCam, rawRect, texW, texH, dispW, dispH, offsetX, offsetY);
                Vector2 tl = ControlPointToTexturePixel(controlPoints[gx, gy + 1], uiCam, rawRect, texW, texH, dispW, dispH, offsetX, offsetY);
                Vector2 tr = ControlPointToTexturePixel(controlPoints[gx + 1, gy + 1], uiCam, rawRect, texW, texH, dispW, dispH, offsetX, offsetY);

                // pixel-space bounding box (clamped for iteration)
                float minXf = Mathf.Min(Mathf.Min(bl.x, br.x), Mathf.Min(tl.x, tr.x));
                float maxXf = Mathf.Max(Mathf.Max(bl.x, br.x), Mathf.Max(tl.x, tr.x));
                float minYf = Mathf.Min(Mathf.Min(bl.y, br.y), Mathf.Min(tl.y, tr.y));
                float maxYf = Mathf.Max(Mathf.Max(bl.y, br.y), Mathf.Max(tl.y, tr.y));

                int minX = Mathf.Clamp(Mathf.FloorToInt(minXf), 0, texW - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(maxXf), 0, texW - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(minYf), 0, texH - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(maxYf), 0, texH - 1);

                float brightnessSum = 0f;
                int sampleCount = 0;
                int brightPixelCount = 0;

                // sub-sample every 2 pixels (保留原取樣間隔)
                for (int py = minY; py <= maxY; py += 2)
                {
                    for (int px = minX; px <= maxX; px += 2)
                    {
                        Vector2 p = new Vector2(px + 0.5f, py + 0.5f); // sample at pixel center
                        if (!PointInQuad(p, bl, br, tr, tl)) continue;

                        int idx = py * texW + px;
                        float b = pixels[idx].r / 255f;
                        brightnessSum += b;
                        sampleCount++;
                        if (b > threshold) brightPixelCount++;
                    }
                }

                if (sampleCount == 0) { persistenceCounter[gx, gy] = 0; continue; }
                float avgBrightness = brightnessSum / sampleCount;
                if (brightPixelCount < minBrightPixels) { persistenceCounter[gx, gy] = 0; continue; }

                persistenceCounter[gx, gy]++;
                if (persistenceCounter[gx, gy] < persistenceFrames) continue;

                brightWarped.Add(new GridCell(gx, gy, avgBrightness));
            }
        }

        UpdateLaserDotsFromCells(brightWarped);
    }

    // --- helpers (新增方法) ---
    // control point 的世界位置 -> RawImage 內 texture 的像素座標 (0..texW, 0..texH)
    Vector2 ControlPointToTexturePixel(RectTransform cp, Camera uiCam, Rect rawRect, int texW, int texH, float dispW, float dispH, float offsetX, float offsetY)
    {
        if (cp == null) return new Vector2(-9999f, -9999f);

        // world -> screen
        Vector2 screenPt = RectTransformUtility.WorldToScreenPoint(uiCam, cp.position);
        // screen -> rawImage local (pivot-centered)
        RectTransformUtility.ScreenPointToLocalPointInRectangle(cameraRawImage.rectTransform, screenPt, uiCam, out Vector2 localPt);
        // 轉為以 rawRect 左下為原點的 local 座標
        Vector2 localLB = new Vector2(localPt.x - rawRect.xMin, localPt.y - rawRect.yMin);
        // 計算在顯示內容 (dispW x dispH) 中的 normalized 座標（可能超出 0..1）
        float nx = dispW > 0f ? (localLB.x - offsetX) / dispW : -1f;
        float ny = dispH > 0f ? (localLB.y - offsetY) / dispH : -1f;
        // 轉為 texture 像素座標（不 clamp，讓後續 bounding-box 判定是否與 texture 有交集）
        return new Vector2(nx * texW, ny * texH);
    }

    bool PointInQuad(Vector2 p, Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl)
    {
        // triangle bl-br-tr OR bl-tr-tl
        if (PointInTriangle(p, bl, br, tr)) return true;
        if (PointInTriangle(p, bl, tr, tl)) return true;
        return false;
    }

    bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v0 = c - a;
        Vector2 v1 = b - a;
        Vector2 v2 = p - a;

        float dot00 = Vector2.Dot(v0, v0);
        float dot01 = Vector2.Dot(v0, v1);
        float dot02 = Vector2.Dot(v0, v2);
        float dot11 = Vector2.Dot(v1, v1);
        float dot12 = Vector2.Dot(v1, v2);

        float denom = dot00 * dot11 - dot01 * dot01;
        if (Mathf.Abs(denom) < 1e-8f) return false;
        float invDenom = 1f / denom;
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        return (u >= -1e-6f) && (v >= -1e-6f) && (u + v <= 1f + 1e-6f);
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
            bool isHighPower = cell.brightness > highPowerThreshold;
            img.color = isHighPower ? Color.green : Color.white;

            // 修改：onGridHit 現在傳入 isHighPower
            onGridHit?.Invoke(cell.x, cell.y, cell.brightness, isHighPower);

            // 修改：把 isHighPower 傳給 TargetManager
            targetManager.HitTargetByCell(cell.x, cell.y, isHighPower);
        }
    }

    public void UpdateTargetsPosition()
    {
        // 改為找到所有帶 TargetCell 的物件，包含 targets 與 obstacles
        TargetCell[] tcs = FindObjectsOfType<TargetCell>();
        foreach (var tc in tcs)
        {
            if (tc == null) continue;
            Vector2 pos = GetWarpedPosition(tc.gridX, tc.gridY);
            RectTransform rt = tc.GetComponent<RectTransform>();
            if (rt == null) continue;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
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

    // 儲存 / 載入 threshold
    void SaveThreshold()
    {
        PlayerPrefs.SetFloat(prefsPrefix + "threshold", threshold);
        PlayerPrefs.Save();
    }

    void LoadThreshold()
    {
        prefsPrefix = gameObject.name + "_LaserDetector_";
        if (PlayerPrefs.HasKey(prefsPrefix + "threshold"))
        {
            threshold = Mathf.Clamp01(PlayerPrefs.GetFloat(prefsPrefix + "threshold"));
        }
    }
    // ...（檔案其他內容不變）...

    // 新增：當拖曳四個外角之一時，使用雙線性插值（bilinear）計算並更新整個 controlPoints 的位置
    // cornerGX/cornerGY 為被拖動的 control point 座標（整數）， newAnchoredPos 為該 control point 的新的 anchoredPosition（UI local, 以 cameraRawImage 的左下為基準）
    public void DragCornerAndWarp(int cornerGX, int cornerGY, Vector2 newAnchoredPos)
    {
        if (controlPoints == null) return;
        if (cornerGX != 0 && cornerGX != gridX) return;
        if (cornerGY != 0 && cornerGY != gridY) return;

        // 取得目前四個角的原始位置（UI local anchoredPosition）
        Vector2 bl = controlPoints[0, 0].anchoredPosition;            // bottom-left (0,0)
        Vector2 br = controlPoints[gridX, 0].anchoredPosition;        // bottom-right (gridX,0)
        Vector2 tl = controlPoints[0, gridY].anchoredPosition;        // top-left (0,gridY)
        Vector2 tr = controlPoints[gridX, gridY].anchoredPosition;    // top-right (gridX,gridY)

        // 依被拖動的是哪一角，替換對應 corner 為 newAnchoredPos
        if (cornerGX == 0 && cornerGY == 0) bl = newAnchoredPos;       // bottom-left
        else if (cornerGX == gridX && cornerGY == 0) br = newAnchoredPos; // bottom-right
        else if (cornerGX == 0 && cornerGY == gridY) tl = newAnchoredPos; // top-left
        else if (cornerGX == gridX && cornerGY == gridY) tr = newAnchoredPos; // top-right

        // 對每一個 control point 使用雙線性插值（dependent on normalized position nx,ny）
        for (int y = 0; y <= gridY; y++)
        {
            for (int x = 0; x <= gridX; x++)
            {
                float nx = (gridX > 0) ? (x / (float)gridX) : 0f;
                float ny = (gridY > 0) ? (y / (float)gridY) : 0f;

                // bilinear interpolation:
                // P(nx,ny) = (1-nx)*(1-ny)*bl + nx*(1-ny)*br + (1-nx)*ny*tl + nx*ny*tr
                Vector2 p = (1f - nx) * (1f - ny) * bl
                          + nx * (1f - ny) * br
                          + (1f - nx) * ny * tl
                          + nx * ny * tr;

                controlPoints[x, y].anchoredPosition = p;
            }
        }

        // 重新繪製 grid 與 targets，並儲存
        UpdateGridVisual();
        UpdateTargetsPosition();
        SaveGridState();
    }
}