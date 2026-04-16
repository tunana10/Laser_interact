using UnityEngine;
using UnityEngine.UI;
using System.Collections;

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
        // 1) 如果 Inspector 有指定 detector，就直接用
        if (detector != null)
        {
            BindUI();
            yield break;
        }

        // 2) 找 cameraManager
        if (cameraManager == null)
            cameraManager = FindObjectOfType<MultiIRCameraManager>();

        // 3) 等待 cameraManager 產生 detectors
        while (cameraManager == null || cameraManager.detectors.Count == 0)
            yield return null;

        detectorIndex = Mathf.Clamp(detectorIndex, 0, cameraManager.detectors.Count - 1);
        detector = cameraManager.detectors[detectorIndex];

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
            if (detectorB != null) gridXInput_B.text = detectorB.gridX.ToString();
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
        }

        if (gridYInput_B)
        {
            gridYInput_B.contentType = InputField.ContentType.IntegerNumber;
            if (detectorB != null) gridYInput_B.text = detectorB.gridY.ToString();
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
        }

        if (resetGridButton) resetGridButton.onClick.AddListener(detector.ResetGrid);

        UpdateValueLabels();
    }

    void UpdateValueLabels()
    {
        if (gridXValue) gridXValue.text = detector.gridX.ToString();
        if (gridYValue) gridYValue.text = detector.gridY.ToString();
    }
}