using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Events;

public class LaserDetectorUIPanel : MonoBehaviour
{
    [Header("Target")]
    public LaserDetector detector;
    public MultiIRCameraManager cameraManager;
    public int detectorIndex = 0;  // 想控制第幾個攝影機的 Detector

    [Header("Grid Inputs")]
    public InputField gridXInput;
    public InputField gridYInput;

    public InputField gridXInput_B;//第二台攝影機的Grid Inputs
    public InputField gridYInput_B;

    [Header("Threshold Inputs (auto-find by Tag)")]
    public string lowPowerInputTag = "LowPowerThresholdInput";
    public string highPowerInputTag = "HighPowerThresholdInput";

    [Header("Text")]
    public Text gridXValue;
    public Text gridYValue;

    [Header("Buttons")]
    public Button resetGridButton;

    [Header("Visibility")]
    public Button panelToggleButton;
    public string visibleLabel = "Hide UI";
    public string hiddenLabel = "Show UI";

    private CanvasGroup panelCanvasGroup;

    // 存放自動找到的 InputField 與 listener（方便移除）
    private InputField lowPowerInputField;
    private InputField highPowerInputField;
    private UnityAction<string> lowOnValueChanged;
    private UnityAction<string> lowOnEndEdit;
    private UnityAction<string> highOnValueChanged;
    private UnityAction<string> highOnEndEdit;

    void Awake()
    {
        // 確保有 CanvasGroup 用來顯示/隱藏（不會停用 GameObject）
        panelCanvasGroup = GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        // 初始為可見
        SetPanelVisible(true, true);

        if (panelToggleButton != null)
        {
            if (panelToggleButton.transform.IsChildOf(transform))
                Debug.LogWarning("LaserDetectorUIPanel: 指定的 toggle button 是 panel 的子物件，隱藏面板後按鈕也會一起消失，請將按鈕放到 panel 之外以便切換。");

            panelToggleButton.onClick.RemoveListener(TogglePanelVisibility);
            panelToggleButton.onClick.AddListener(TogglePanelVisibility);
            UpdateToggleButtonLabel(true);
        }
    }

    void OnDestroy()
    {
        if (panelToggleButton != null)
            panelToggleButton.onClick.RemoveListener(TogglePanelVisibility);

        // 清除自動綁定的 listeners（若有）
        if (lowPowerInputField != null)
        {
            if (lowOnValueChanged != null) lowPowerInputField.onValueChanged.RemoveListener(lowOnValueChanged);
            if (lowOnEndEdit != null) lowPowerInputField.onEndEdit.RemoveListener(lowOnEndEdit);
        }
        if (highPowerInputField != null)
        {
            if (highOnValueChanged != null) highPowerInputField.onValueChanged.RemoveListener(highOnValueChanged);
            if (highOnEndEdit != null) highPowerInputField.onEndEdit.RemoveListener(highOnEndEdit);
        }
    }

    // Public: 可由外部按鈕直接呼叫（Inspector -> Button OnClick）
    public void TogglePanelVisibility()
    {
        bool currentlyVisible = panelCanvasGroup != null ? (panelCanvasGroup.alpha > 0.5f && panelCanvasGroup.interactable) : gameObject.activeSelf;
        SetPanelVisible(!currentlyVisible);
    }

    // 顯示/隱藏面板（使用 CanvasGroup，不會停用 GameObject）
    public void SetPanelVisible(bool visible, bool instant = false)
    {
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = visible ? 1f : 0f;
            panelCanvasGroup.interactable = visible;
            panelCanvasGroup.blocksRaycasts = visible;
        }
        else
        {
            gameObject.SetActive(visible);
        }

        UpdateToggleButtonLabel(visible);
    }

    void UpdateToggleButtonLabel(bool panelIsVisible)
    {
        if (panelToggleButton == null) return;
        Text t = panelToggleButton.GetComponentInChildren<Text>();
        if (t != null)
            t.text = panelIsVisible ? visibleLabel : hiddenLabel;
    }

    void Start()
    {
        StartCoroutine(WaitAndBindDetector());
    }

    IEnumerator WaitAndBindDetector()
    {
        // 如果 Inspector 有指定 detector，等到該 detector 完成初始化 (IsGridReady) 再綁定
        if (detector != null)
        {
            while (!detector.IsGridReady)
                yield return null;
            BindUI();
            yield break;
        }

        // 嘗試取得 cameraManager
        if (cameraManager == null)
            cameraManager = FindObjectOfType<MultiIRCameraManager>();

        // 等待 cameraManager 有 detectors，或場景中至少有一個 LaserDetector
        while ((cameraManager == null || cameraManager.detectors == null || cameraManager.detectors.Count == 0)
               && FindObjectsOfType<LaserDetector>().Length == 0)
            yield return null;

        // 若有 cameraManager 且包含 detectors，使用指定 index
        if (cameraManager != null && cameraManager.detectors != null && cameraManager.detectors.Count > 0)
        {
            detectorIndex = Mathf.Clamp(detectorIndex, 0, cameraManager.detectors.Count - 1);
            detector = cameraManager.detectors[detectorIndex];

            // 等待主 detector 完成初始化
            while (detector != null && !detector.IsGridReady)
                yield return null;

            // 若面板包含第二台輸入欄位，且 cameraManager 有第二個 detector，也等待它完成初始化
            if ((gridXInput_B != null || gridYInput_B != null) && cameraManager.detectors.Count > 1)
            {
                var detB = cameraManager.detectors[1];
                while (detB != null && !detB.IsGridReady)
                    yield return null;
            }
        }
        else
        {
            // fallback：從場景中找一個可用的 LaserDetector（並等待初始化）
            var all = FindObjectsOfType<LaserDetector>();
            if (all.Length > 0)
            {
                detectorIndex = Mathf.Clamp(detectorIndex, 0, all.Length - 1);
                detector = all[detectorIndex];
                while (detector != null && !detector.IsGridReady)
                    yield return null;
            }
        }

        BindUI();
    }

    void BindUI()
    {
        if (detector == null)
        {
            Debug.LogWarning("LaserDetectorUIPanel: 找不到可控制的 LaserDetector");
            return;
        }

        // Grid X input
        if (gridXInput)
        {
            gridXInput.contentType = InputField.ContentType.IntegerNumber;
            gridXInput.text = detector.gridX.ToString();
            gridXInput.onEndEdit.AddListener(s =>
            {
                if (!int.TryParse(s, out int v)) v = detector.gridX;
                v = Mathf.Max(1, v);
                detector.SetGridSize(v, detector.gridY);
                gridXInput.text = detector.gridX.ToString();
                UpdateValueLabels();
            });
        }

        // Grid Y input
        if (gridYInput)
        {
            gridYInput.contentType = InputField.ContentType.IntegerNumber;
            gridYInput.text = detector.gridY.ToString();
            gridYInput.onEndEdit.AddListener(s =>
            {
                if (!int.TryParse(s, out int v)) v = detector.gridY;
                v = Mathf.Max(1, v);
                detector.SetGridSize(detector.gridX, v);
                gridYInput.text = detector.gridY.ToString();
                UpdateValueLabels();
            });
        }

        // --- Bind second camera inputs (gridXInput_B / gridYInput_B) ---
        LaserDetector detectorB = null;
        if (cameraManager != null && cameraManager.detectors != null && cameraManager.detectors.Count > 1)
        {
            detectorB = cameraManager.detectors[1];
        }
        else
        {
            // 如果沒有 cameraManager，可嘗試從場景中找到另一個 Detector（不是主 detector）
            var all = FindObjectsOfType<LaserDetector>();
            foreach (var d in all)
            {
                if (d != detector) { detectorB = d; break; }
            }
        }

        if (gridXInput_B)
        {
            gridXInput_B.contentType = InputField.ContentType.IntegerNumber;
            if (detectorB != null && detectorB.IsGridReady) gridXInput_B.text = detectorB.gridX.ToString();
            else gridXInput_B.text = "";
            gridXInput_B.onEndEdit.AddListener(s =>
            {
                if (detectorB == null) return;
                if (!int.TryParse(s, out int v)) v = detectorB.gridX;
                v = Mathf.Max(1, v);
                detectorB.SetGridSize(v, detectorB.gridY);
                gridXInput_B.text = detectorB.gridX.ToString();
            });
            gridXInput_B.interactable = detectorB != null;
            if (detectorB != null && !detectorB.IsGridReady)
                StartCoroutine(WaitAndPopulateDetectorBInputs(detectorB));
        }

        if (gridYInput_B)
        {
            gridYInput_B.contentType = InputField.ContentType.IntegerNumber;
            if (detectorB != null && detectorB.IsGridReady) gridYInput_B.text = detectorB.gridY.ToString();
            else gridYInput_B.text = "";
            gridYInput_B.onEndEdit.AddListener(s =>
            {
                if (detectorB == null) return;
                if (!int.TryParse(s, out int v)) v = detectorB.gridY;
                v = Mathf.Max(1, v);
                detectorB.SetGridSize(detectorB.gridX, v);
                gridYInput_B.text = detectorB.gridY.ToString();
            });
            gridYInput_B.interactable = detectorB != null;
            if (detectorB != null && !detectorB.IsGridReady)
                StartCoroutine(WaitAndPopulateDetectorBInputs(detectorB));
        }

        // --- 自動尋找並綁定 Low/High Power Threshold 的 InputField（以 Tag） ---
        if (!string.IsNullOrEmpty(lowPowerInputTag))
        {
            var lowObj = GameObject.FindWithTag(lowPowerInputTag);
            if (lowObj != null) lowPowerInputField = lowObj.GetComponent<InputField>();
        }
        if (!string.IsNullOrEmpty(highPowerInputTag))
        {
            var highObj = GameObject.FindWithTag(highPowerInputTag);
            if (highObj != null) highPowerInputField = highObj.GetComponent<InputField>();
        }

        // 綁定 low
        if (lowPowerInputField != null)
        {
            lowPowerInputField.contentType = InputField.ContentType.DecimalNumber;
            lowPowerInputField.text = detector.lowPowerThreshold.ToString("F3");

            lowOnValueChanged = s =>
            {
                if (float.TryParse(s, out float v))
                {
                    v = Mathf.Clamp01(v);
                    detector.lowPowerThreshold = v;
                }
            };
            lowOnEndEdit = s =>
            {
                if (!float.TryParse(s, out float v)) v = detector.lowPowerThreshold;
                v = Mathf.Clamp01(v);
                detector.lowPowerThreshold = v;
                lowPowerInputField.text = detector.lowPowerThreshold.ToString("F3");
                // 儲存到 PlayerPrefs
                detector.SaveThreshold();
            };

            lowPowerInputField.onValueChanged.AddListener(lowOnValueChanged);
            lowPowerInputField.onEndEdit.AddListener(lowOnEndEdit);
        }

        // 綁定 high
        if (highPowerInputField != null)
        {
            highPowerInputField.contentType = InputField.ContentType.DecimalNumber;
            highPowerInputField.text = detector.highPowerThreshold.ToString("F3");

            highOnValueChanged = s =>
            {
                if (float.TryParse(s, out float v))
                {
                    v = Mathf.Clamp01(v);
                    detector.highPowerThreshold = v;
                }
            };
            highOnEndEdit = s =>
            {
                if (!float.TryParse(s, out float v)) v = detector.highPowerThreshold;
                v = Mathf.Clamp01(v);
                detector.highPowerThreshold = v;
                highPowerInputField.text = detector.highPowerThreshold.ToString("F3");
                // 儲存到 PlayerPrefs
                detector.SaveThreshold();
            };

            highPowerInputField.onValueChanged.AddListener(highOnValueChanged);
            highPowerInputField.onEndEdit.AddListener(highOnEndEdit);
        }

        if (resetGridButton) resetGridButton.onClick.AddListener(detector.ResetGrid);

        UpdateValueLabels();
    }

    IEnumerator WaitAndPopulateDetectorBInputs(LaserDetector detB)
    {
        while (detB != null && !detB.IsGridReady)
            yield return null;

        if (detB == null) yield break;

        if (gridXInput_B != null)
        {
            gridXInput_B.text = detB.gridX.ToString();
            gridXInput_B.interactable = true;
        }
        if (gridYInput_B != null)
        {
            gridYInput_B.text = detB.gridY.ToString();
            gridYInput_B.interactable = true;
        }
    }

    // 可由外部呼叫以同步 UI 顯示當前 detector 的 threshold 值
    public void RefreshThresholdFields()
    {
        if (detector == null) return;
        if (lowPowerInputField != null) lowPowerInputField.text = detector.lowPowerThreshold.ToString("F3");
        if (highPowerInputField != null) highPowerInputField.text = detector.highPowerThreshold.ToString("F3");
    }

    void UpdateValueLabels()
    {
        if (gridXValue) gridXValue.text = detector.gridX.ToString();
        if (gridYValue) gridYValue.text = detector.gridY.ToString();
    }
}