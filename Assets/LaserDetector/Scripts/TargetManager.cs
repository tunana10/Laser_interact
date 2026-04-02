using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TargetManager : MonoBehaviour
{
    public RectTransform dotContainer;
    public GameObject targetPrefab;
    public GameObject obstaclePrefab;         // 障礙物 Prefab（可為空，會使用 targetPrefab 並改色）
    public int targetCount = 5;
    public int obstacleCount = 3;            // 隨機生成的障礙物數量
    public float respawnDelay = 3f;

    public Text scoreText;

    private LaserDetector detector;
    private List<GameObject> targets = new List<GameObject>();
    private List<GameObject> obstacles = new List<GameObject>(); // 障礙物清單
    private int score = 0;

    // Mode toggles (Inspector)
    public Toggle randomModeToggle;
    public Toggle editModeToggle;

    // 編輯模式用：點擊格子時的選取半徑比例（相對於 cell 的寬/高）
    public float clickRadiusFraction = 1f;

    // 新：cell -> 物件（target / obstacle）映射
    private enum CellItemType { Target, Obstacle }
    private class CellItem
    {
        public GameObject go;
        public CellItemType type;
        public float spawnAt;
        public CellItem(GameObject g, CellItemType t) { go = g; type = t; spawnAt = Time.time; }
    }
    private Dictionary<Vector2Int, CellItem> cellItems = new Dictionary<Vector2Int, CellItem>();

    // 可選：生成後短暫無敵（避免剛生成就被偵測）
    public float spawnInvulnerability = 0f;

    // 新增：程式內的 C# 事件，供其他程式用程式碼訂閱（id, gx, gy, type, isHighPower）
    public event Action<int, int, int, string, bool> OnItemHit;

    bool respawning = false;
    bool isEditMode = false;

    void Awake()
    {
        // 設定 toggle listener（若有）
        if (randomModeToggle != null)
        {
            randomModeToggle.onValueChanged.RemoveListener(OnRandomModeToggleChanged);
            randomModeToggle.onValueChanged.AddListener(OnRandomModeToggleChanged);
        }
        if (editModeToggle != null)
        {
            editModeToggle.onValueChanged.RemoveListener(OnEditModeToggleChanged);
            editModeToggle.onValueChanged.AddListener(OnEditModeToggleChanged);
        }

        // 初始互斥處理
        if (randomModeToggle == null && editModeToggle == null)
        {
            isEditMode = false;
        }
        else if (randomModeToggle != null && editModeToggle != null)
        {
            if (!randomModeToggle.isOn && !editModeToggle.isOn)
            {
                randomModeToggle.SetIsOnWithoutNotify(true);
                editModeToggle.SetIsOnWithoutNotify(false);
                isEditMode = false;
            }
            else if (randomModeToggle.isOn && editModeToggle.isOn)
            {
                editModeToggle.SetIsOnWithoutNotify(false);
                isEditMode = false;
            }
            else
            {
                isEditMode = editModeToggle.isOn;
            }
        }
        else if (editModeToggle != null)
        {
            isEditMode = editModeToggle.isOn;
        }
        else if (randomModeToggle != null)
        {
            isEditMode = !randomModeToggle.isOn;
        }
    }

    void OnDestroy()
    {
        if (randomModeToggle != null)
            randomModeToggle.onValueChanged.RemoveListener(OnRandomModeToggleChanged);
        if (editModeToggle != null)
            editModeToggle.onValueChanged.RemoveListener(OnEditModeToggleChanged);
    }

    void Start()
    {
        StartCoroutine(WaitForLaserDetector());
    }

    IEnumerator WaitForLaserDetector()
    {
        while (detector == null)
        {
            detector = FindObjectOfType<LaserDetector>();
            if (detector == null)
                yield return new WaitForSeconds(0.2f);
        }

        while (!detector.IsGridReady)
            yield return null;

        // 根據模式決定是否生成隨機 target / obstacle
        bool startRandom = true;
        if (editModeToggle != null) startRandom = !editModeToggle.isOn;
        if (randomModeToggle != null) startRandom = randomModeToggle.isOn;
        isEditMode = !startRandom;

        if (startRandom)
            SpawnTargetsAndObstacles();

        UpdateScoreUI();
    }

    void Update()
    {
        if (detector == null) return;

        if (isEditMode)
        {
            HandleEditModeInput();
            // 編輯模式不做自動重生檢查
            return;
        }

        if (!respawning && AllTargetsDestroyed())
            StartCoroutine(RespawnRoutine());
    }

    void HandleEditModeInput()
    {
        if (detector == null || !detector.IsGridReady) return;

        bool leftClicked = false;
        bool rightClicked = false;
        Vector2 clickScreenPos = Vector2.zero;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            leftClicked = true;
            clickScreenPos = Input.mousePosition;
        }
        if (Input.GetMouseButtonDown(1))
        {
            rightClicked = true;
            clickScreenPos = Input.mousePosition;
        }
#endif

#if ENABLE_INPUT_SYSTEM
        if (!leftClicked)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                leftClicked = true;
                clickScreenPos = mouse.position.ReadValue();
            }
        }
        if (!rightClicked)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton != null && mouse.rightButton.wasPressedThisFrame)
            {
                rightClicked = true;
                clickScreenPos = mouse.position.ReadValue();
            }
        }
        // 支援觸控（視需求）
        if (!leftClicked && Touchscreen.current != null)
        {
            var primary = Touchscreen.current.primaryTouch;
            if (primary != null && primary.press.wasPressedThisFrame)
            {
                leftClicked = true;
                clickScreenPos = primary.position.ReadValue();
            }
        }
#endif

        if (!leftClicked && !rightClicked) return;

        // 取得 Canvas 與 camera（對於 ScreenSpaceOverlay 傳 null）
        Canvas canvas = detector.cameraRawImage.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        // 判斷點擊是否落在 cameraRawImage（若點到其他 UI 則忽略）
        if (EventSystem.current != null && canvas != null)
        {
            GraphicRaycaster gr = canvas.GetComponent<GraphicRaycaster>();
            if (gr != null)
            {
                PointerEventData ped = new PointerEventData(EventSystem.current) { position = clickScreenPos };
                List<RaycastResult> results = new List<RaycastResult>();
                gr.Raycast(ped, results);
                if (results.Count > 0)
                {
                    bool hitCameraRawImage = false;
                    foreach (var r in results)
                    {
                        if (IsDescendantOf(r.gameObject, detector.cameraRawImage.gameObject) || r.gameObject == detector.cameraRawImage.gameObject)
                        {
                            hitCameraRawImage = true;
                            break;
                        }
                    }
                    if (!hitCameraRawImage)
                        return; // 點到其他 UI，不處理
                }
            }
            else
            {
                // fallback：舊方式（若有指向 UI 且不在 cameraRawImage 區域，忽略）
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(detector.cameraRawImage.rectTransform, clickScreenPos, uiCam, out Vector2 tmp))
                        return;
                    Rect imgRect = detector.cameraRawImage.rectTransform.rect;
                    if (tmp.x < imgRect.xMin || tmp.x > imgRect.xMax || tmp.y < imgRect.yMin || tmp.y > imgRect.yMax)
                        return;
                }
            }
        }

        // 取得點擊在 gridContainer 的 local 座標（pivot-centered）
        if (!detector.TryScreenPointToGridLocalPoint(clickScreenPos, uiCam, out Vector2 gridLocalPivotPoint))
            return;

        // 將 pivot-centered 轉成 左下角為原點的座標（和 controlPoints 的 anchoredPosition 同源）
        Rect rect = detector.cameraRawImage.rectTransform.rect;
        Vector2 gridLocalPoint = gridLocalPivotPoint + new Vector2(rect.width * 0.5f, rect.height * 0.5f);

        // 找 nearest cell
        float rectW = rect.width;
        float rectH = rect.height;
        float cellW = rectW / Mathf.Max(1, detector.gridX);
        float cellH = rectH / Mathf.Max(1, detector.gridY);
        float threshold = Mathf.Min(cellW, cellH) * Mathf.Max(0.05f, clickRadiusFraction);
        float thresholdSqr = threshold * threshold;

        int foundGx = -1, foundGy = -1;
        float bestDist = float.MaxValue;

        for (int gy = 0; gy < detector.gridY; gy++)
        {
            for (int gx = 0; gx < detector.gridX; gx++)
            {
                Vector2 center = detector.GetWarpedPosition(gx, gy);
                float dsq = (gridLocalPoint - center).sqrMagnitude;
                if (dsq < bestDist)
                {
                    bestDist = dsq;
                    foundGx = gx;
                    foundGy = gy;
                }
            }
        }

        if (foundGx >= 0 && bestDist <= thresholdSqr)
        {
            if (leftClicked)
                ToggleTargetAtCell(foundGx, foundGy);
            else if (rightClicked)
                ToggleObstacleAtCell(foundGx, foundGy);
        }
    }

    void ToggleTargetAtCell(int gx, int gy)
    {
        Vector2Int key = new Vector2Int(gx, gy);
        if (cellItems.TryGetValue(key, out CellItem existing))
        {
            if (existing.type == CellItemType.Target)
            {
                // 移除 target
                if (existing.go != null) Destroy(existing.go);
                cellItems.Remove(key);
                targets.Remove(existing.go);
            }
            else
            {
                // cell 有 obstacle：移除 obstacle 並建立 target
                if (existing.go != null) Destroy(existing.go);
                cellItems.Remove(key);
                obstacles.Remove(existing.go);
                CreateTargetAtCell(gx, gy);
            }
        }
        else
        {
            CreateTargetAtCell(gx, gy);
        }
    }

    void ToggleObstacleAtCell(int gx, int gy)
    {
        Vector2Int key = new Vector2Int(gx, gy);
        if (cellItems.TryGetValue(key, out CellItem existing))
        {
            if (existing.type == CellItemType.Obstacle)
            {
                // 移除 obstacle
                if (existing.go != null) Destroy(existing.go);
                cellItems.Remove(key);
                obstacles.Remove(existing.go);
            }
            else
            {
                // cell 有 target：移除 target 並建立 obstacle
                if (existing.go != null) Destroy(existing.go);
                cellItems.Remove(key);
                targets.Remove(existing.go);
                CreateObstacleAtCell(gx, gy);
            }
        }
        else
        {
            CreateObstacleAtCell(gx, gy);
        }
    }

    GameObject CreateTargetAtCell(int gx, int gy)
    {
        GameObject t = Instantiate(targetPrefab, dotContainer);
        PlaceTargetOnGrid(t, gx, gy);
        targets.Add(t);
        Vector2Int key = new Vector2Int(gx, gy);
        cellItems[key] = new CellItem(t, CellItemType.Target);
        return t;
    }

    GameObject CreateObstacleAtCell(int gx, int gy)
    {
        GameObject o;
        if (obstaclePrefab != null)
            o = Instantiate(obstaclePrefab, dotContainer);
        else
            o = Instantiate(targetPrefab, dotContainer); // fallback：用 targetPrefab 並改色

        PlaceObstacleOnGrid(o, gx, gy);

        // 若使用 fallback，嘗試把 Image 設為紅色（若有 Image）
        var img = o.GetComponent<Image>();
        if (img != null)
            img.color = Color.red;

        obstacles.Add(o);
        Vector2Int key = new Vector2Int(gx, gy);
        cellItems[key] = new CellItem(o, CellItemType.Obstacle);
        return o;
    }

    void SpawnTargetsAndObstacles()
    {
        ClearTargets();

        HashSet<Vector2Int> usedCells = new HashSet<Vector2Int>();

        // spawn targets
        int maxAttempts = Mathf.Max(1, detector.gridX * detector.gridY * 2);
        for (int i = 0; i < targetCount; i++)
        {
            Vector2Int cell;
            int attempts = 0;
            do
            {
                int gx = UnityEngine.Random.Range(0, detector.gridX);
                int gy = UnityEngine.Random.Range(0, detector.gridY);
                cell = new Vector2Int(gx, gy);
                attempts++;
                if (attempts > maxAttempts) break;
            } while (usedCells.Contains(cell));

            if (usedCells.Contains(cell)) continue;

            usedCells.Add(cell);

            GameObject t = Instantiate(targetPrefab, dotContainer);
            PlaceTargetOnGrid(t, cell.x, cell.y);
            targets.Add(t);
            cellItems[cell] = new CellItem(t, CellItemType.Target);
        }

        // spawn obstacles (不與 targets 重複)
        maxAttempts = Mathf.Max(1, detector.gridX * detector.gridY * 2);
        for (int i = 0; i < obstacleCount; i++)
        {
            Vector2Int cell;
            int attempts = 0;
            do
            {
                int gx = UnityEngine.Random.Range(0, detector.gridX);
                int gy = UnityEngine.Random.Range(0, detector.gridY);
                cell = new Vector2Int(gx, gy);
                attempts++;
                if (attempts > maxAttempts) break;
            } while (usedCells.Contains(cell));

            if (usedCells.Contains(cell)) continue;

            usedCells.Add(cell);

            GameObject o;
            if (obstaclePrefab != null)
                o = Instantiate(obstaclePrefab, dotContainer);
            else
                o = Instantiate(targetPrefab, dotContainer);

            PlaceObstacleOnGrid(o, cell.x, cell.y);
            var img = o.GetComponent<Image>();
            if (img != null) img.color = Color.red;

            obstacles.Add(o);
            cellItems[cell] = new CellItem(o, CellItemType.Obstacle);
        }
    }

    void PlaceTargetOnGrid(GameObject target, int gx, int gy)
    {
        RectTransform cam = detector.cameraRawImage.rectTransform;
        RectTransform rt = target.GetComponent<RectTransform>();

        // 改為以 cameraRawImage 的左下為座標系
        rt.SetParent(cam, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        TargetCell tc = target.GetComponent<TargetCell>();
        if (tc == null)
            tc = target.AddComponent<TargetCell>();

        tc.gridX = gx;
        tc.gridY = gy;

        Vector2 pos = detector.GetWarpedPosition(gx, gy);
        rt.anchoredPosition = pos;
    }

    void PlaceObstacleOnGrid(GameObject obstacle, int gx, int gy)
    {
        // 與 PlaceTargetOnGrid 一樣的定位邏輯
        RectTransform cam = detector.cameraRawImage.rectTransform;
        RectTransform rt = obstacle.GetComponent<RectTransform>();

        rt.SetParent(cam, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        TargetCell tc = obstacle.GetComponent<TargetCell>();
        if (tc == null)
            tc = obstacle.AddComponent<TargetCell>();

        tc.gridX = gx;
        tc.gridY = gy;

        Vector2 pos = detector.GetWarpedPosition(gx, gy);
        rt.anchoredPosition = pos;
    }

    bool AllTargetsDestroyed()
    {
        foreach (var t in targets)
            if (t != null && t.activeSelf) return false;

        return true;
    }

    IEnumerator RespawnRoutine()
    {
        respawning = true;
        yield return new WaitForSeconds(respawnDelay);

        foreach (var t in new List<GameObject>(targets))
            if (t != null) Destroy(t);
        foreach (var o in new List<GameObject>(obstacles))
            if (o != null) Destroy(o);

        targets.Clear();
        obstacles.Clear();
        cellItems.Clear();

        SpawnTargetsAndObstacles();
        respawning = false;
    }

    // 光點進入格子就算命中，不需要碰到 Target/Obstacle
    // 修改：新增 isHighPower 參數（true = 綠點 -> 射擊；false = 白點 -> 瞄準）
    public void HitTargetByCell(int gx, int gy, bool isHighPower)
    {
        Vector2Int key = new Vector2Int(gx, gy);

        if (cellItems.TryGetValue(key, out CellItem item) && item != null && item.go != null && item.go.activeSelf)
        {
            // spawn invulnerability 檢查
            if (spawnInvulnerability > 0f && Time.time - item.spawnAt < spawnInvulnerability)
                return;

            TargetCell tc = item.go.GetComponent<TargetCell>();
            int id = (detector.gridY - 1 - gy) * detector.gridX + gx;
            string typeStr = item.type == CellItemType.Target ? "Target" : "Obstacle";
            string modeStr = isHighPower ? "Shot(射擊)" : "Aim(瞄準)";
            Debug.Log($"Hit Grid ({gx},{gy}) ID:{id} Type:{typeStr} Mode:{modeStr}");

            // 先發出程式內事件（供程式碼訂閱）
            OnItemHit?.Invoke(id, gx, gy, typeStr, isHighPower);

            // 設為 inactive 並更新分數（原邏輯不變）
            item.go.SetActive(false);
            cellItems.Remove(key);
            if (item.type == CellItemType.Target)
            {
                targets.Remove(item.go);
                score++;
            }
            else
            {
                obstacles.Remove(item.go);
                score--;
            }
            UpdateScoreUI();
        }
    }

    void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score : " + score;
    }

    void ClearTargets()
    {
        foreach (var t in targets)
            if (t != null) Destroy(t);
        foreach (var o in obstacles)
            if (o != null) Destroy(o);

        targets.Clear();
        obstacles.Clear();
        cellItems.Clear();
    }

    void OnRandomModeToggleChanged(bool on)
    {
        if (on)
        {
            if (editModeToggle != null) editModeToggle.SetIsOnWithoutNotify(false);
            isEditMode = false;
            ClearTargets();
            SpawnTargetsAndObstacles();
        }
        else
        {
            isEditMode = editModeToggle != null && editModeToggle.isOn;
        }
    }

    void OnEditModeToggleChanged(bool on)
    {
        if (on)
        {
            if (randomModeToggle != null) randomModeToggle.SetIsOnWithoutNotify(false);
            isEditMode = true;
            ClearTargets();
        }
        else
        {
            isEditMode = false;
            if (randomModeToggle != null && randomModeToggle.isOn)
            {
                ClearTargets();
                SpawnTargetsAndObstacles();
            }
        }
    }

    // helper：判斷一個 GameObject 是否為另一個的子孫（包含自身）
    bool IsDescendantOf(GameObject child, GameObject parent)
    {
        if (child == null || parent == null) return false;
        Transform t = child.transform;
        while (t != null)
        {
            if (t.gameObject == parent) return true;
            t = t.parent;
        }
        return false;
    }
}