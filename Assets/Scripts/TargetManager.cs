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

    public int targetCount = 5;
    public float respawnDelay = 3f;

    public Text scoreText;

    private LaserDetector detector;
    private List<GameObject> targets = new List<GameObject>();
    private int score = 0;

    // Mode toggles (Inspector)
    public Toggle randomModeToggle;
    public Toggle editModeToggle;

    // 編輯模式用：點擊格子時的選取半徑比例（相對於 cell 的寬/高）
    public float clickRadiusFraction = 1f;

    private Dictionary<Vector2Int, GameObject> cellToTarget = new Dictionary<Vector2Int, GameObject>();

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

        // 初始互斥處理：若兩個都沒綁，預設隨機模式；若兩個都打勾，取消 edit
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

        // 根據模式決定是否生成隨機 target
        bool startRandom = true;
        if (editModeToggle != null) startRandom = !editModeToggle.isOn;
        if (randomModeToggle != null) startRandom = randomModeToggle.isOn;
        isEditMode = !startRandom;

        if (startRandom)
            SpawnTargets();

        UpdateScoreUI();
    }

    void Update()
    {
        if (detector == null) return;

        if (isEditMode)
        {
            HandleEditModeInput();
            // 不做自動重生檢查
            return;
        }

        if (!respawning && AllTargetsDestroyed())
            StartCoroutine(RespawnRoutine());
    }

    void HandleEditModeInput()
    {
        if (detector == null || !detector.IsGridReady) return;

        bool clicked = false;
        Vector2 clickScreenPos = Vector2.zero;

#if ENABLE_LEGACY_INPUT_MANAGER
        // 舊輸入 API（優先使用）
        if (Input.GetMouseButtonDown(0))
        {
            clicked = true;
            clickScreenPos = Input.mousePosition;
        }
#endif

#if ENABLE_INPUT_SYSTEM
        // 新 Input System
        if (!clicked)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                clicked = true;
                clickScreenPos = mouse.position.ReadValue();
            }
            else if (Touchscreen.current != null)
            {
                var primary = Touchscreen.current.primaryTouch;
                if (primary != null && primary.press.wasPressedThisFrame)
                {
                    clicked = true;
                    clickScreenPos = primary.position.ReadValue();
                }
            }
        }
#endif

        if (!clicked) return;

        // 取得 Canvas 與 camera（對於 ScreenSpaceOverlay 傳 null）
        Canvas canvas = detector.cameraRawImage.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        // 如果有 Canvas + GraphicRaycaster，raycast 判斷點擊落在哪個 UI 元件上
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

        // 取得點擊在 gridContainer 的 local 座標（LaserDetector 提供的 helper）
        if (!detector.TryScreenPointToGridLocalPoint(clickScreenPos, uiCam, out Vector2 gridLocalPivotPoint))
            return;

        // 重要：ScreenPointToLocalPointInRectangle 回傳的 localPoint origin 在 RectTransform 的 pivot（通常是中心）。
        // controlPoints 的 anchoredPosition 在建立時用了以左下為 (0,0) 的座標（0..width, 0..height）。
        // 需要把 pivot-centered local point 轉成左下角為 origin 的座標，再做比較。
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
            ToggleTargetAtCell(foundGx, foundGy);
        }
    }

    void ToggleTargetAtCell(int gx, int gy)
    {
        Vector2Int key = new Vector2Int(gx, gy);
        if (cellToTarget.TryGetValue(key, out GameObject existing))
        {
            // 移除 target
            if (existing != null)
                Destroy(existing);
            cellToTarget.Remove(key);
            targets.Remove(existing);
        }
        else
        {
            // 新增 target
            GameObject t = Instantiate(targetPrefab, dotContainer);
            PlaceTargetOnGrid(t, gx, gy);
            targets.Add(t);
            cellToTarget[key] = t;
        }
    }

    void SpawnTargets()
    {
        ClearTargets(); // 先清掉現有的，避免重複

        HashSet<Vector2Int> usedCells = new HashSet<Vector2Int>();

        int maxAttempts = Mathf.Max(1, detector.gridX * detector.gridY * 2);
        for (int i = 0; i < targetCount; i++)
        {
            Vector2Int cell;
            int attempts = 0;
            do
            {
                int gx = Random.Range(0, detector.gridX);
                int gy = Random.Range(0, detector.gridY);
                cell = new Vector2Int(gx, gy);
                attempts++;
                if (attempts > maxAttempts) break;
            } while (usedCells.Contains(cell));

            if (usedCells.Contains(cell)) continue;

            usedCells.Add(cell);

            GameObject t = Instantiate(targetPrefab, dotContainer);
            PlaceTargetOnGrid(t, cell.x, cell.y);
            targets.Add(t);
            cellToTarget[cell] = t;
        }
    }

    void PlaceTargetOnGrid(GameObject target, int gx, int gy)
    {
        RectTransform cam = detector.cameraRawImage.rectTransform;
        RectTransform rt = target.GetComponent<RectTransform>();

        // 把 target 以左下為 origin 的 anchoredPosition 放在 cameraRawImage 上：
        rt.SetParent(cam, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero; // <- 修正：不要使用 stretch anchors
        rt.pivot = new Vector2(0.5f, 0.5f);

        TargetCell tc = target.GetComponent<TargetCell>();
        if (tc == null)
            tc = target.AddComponent<TargetCell>();

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

        foreach (var t in targets)
            Destroy(t);
        targets.Clear();
        cellToTarget.Clear();

        SpawnTargets();
        respawning = false;
    }

    // 光點進入格子就算命中，不需要碰到 Target
    public void HitTargetByCell(int gx, int gy)
    {
        Vector2Int key = new Vector2Int(gx, gy);

        if (cellToTarget.TryGetValue(key, out GameObject t) && t != null && t.activeSelf)
        {
            TargetCell tc = t.GetComponent<TargetCell>();
            if (tc != null)
            {
                int id = (detector.gridY - 1 - tc.gridY) * detector.gridX + tc.gridX;
                Debug.Log($"Hit Target Grid ({tc.gridX},{tc.gridY}) ID:{id}");

                t.SetActive(false);
                cellToTarget.Remove(key);
                targets.Remove(t);
                score++;
                UpdateScoreUI();
            }
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
        targets.Clear();
        cellToTarget.Clear();
    }

    // Toggle callbacks（由 UI Toggle 呼叫或由 Awake 設定 listener）
    void OnRandomModeToggleChanged(bool on)
    {
        if (on)
        {
            if (editModeToggle != null) editModeToggle.SetIsOnWithoutNotify(false);
            isEditMode = false;
            ClearTargets();
            SpawnTargets();
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
                SpawnTargets();
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