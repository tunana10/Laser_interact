using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class MultiIRCameraManager : MonoBehaviour
{
    public RectTransform cameraContainer;
    public RectTransform dotContainer;
    public Vector2 cameraDisplaySize = new Vector2(320, 180);
    public GameObject laserDotPrefab;
    public Slider thresholdSlider;

    // 給 UI 面板使用的 Detector 清單
    public List<LaserDetector> detectors = new List<LaserDetector>();

    private List<WebCamTexture> camTextures = new List<WebCamTexture>();

    void Start()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"偵測到 {devices.Length} 個攝影機（包含虛擬攝影機）");

        int realCamCount = 0;

        for (int i = 0; i < devices.Length; i++)
        {
            if (!IsAllowedCamera(devices[i].name))
            {
                Debug.Log($"跳過非 IR 攝影機：{devices[i].name}");
                continue;
            }

            CreateCameraPreview(realCamCount, devices[i]);
            realCamCount++;
        }

        Debug.Log($"成功啟動 {realCamCount} 支真正的 IR 攝影機");
    }

    bool IsAllowedCamera(string camName)
    {
        string[] exactNames = {
            "LRCP V720P",
            "HD USB Camera",
            "GIGABYTE HD CAMERA"
            
            
        };

        foreach (var name in exactNames)
        {
            if (camName.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    void CreateCameraPreview(int index, WebCamDevice device)
    {
        WebCamTexture tex = new WebCamTexture(device.name, 1280, 720, 30);

        try
        {
            tex.Play();
        }
        catch
        {
            Debug.LogError($"無法啟動攝影機：{device.name}");
            return;
        }

        if (!tex.isPlaying)
        {
            Debug.LogError($"攝影機無法啟動或不支援解析度：{device.name}");
            return;
        }

        camTextures.Add(tex);

        //==============================================
        // (1) 建立攝影機 RawImage
        //==============================================
        GameObject imgObj = new GameObject($"Camera_{index}");
        imgObj.transform.SetParent(cameraContainer, false);

        RawImage raw = imgObj.AddComponent<RawImage>();
        raw.texture = tex;

        RectTransform rect = raw.GetComponent<RectTransform>();
        rect.sizeDelta = cameraDisplaySize;
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(0, 0.5f);
        rect.anchoredPosition = new Vector2(index * cameraDisplaySize.x + 50, 0);

        //==============================================
        // (2) LaserDetector
        //==============================================
        LaserDetector detector = imgObj.AddComponent<LaserDetector>();
        detector.sourceWebcam = tex;
        detector.cameraRawImage = raw;
        detector.thresholdSlider = thresholdSlider;
        detector.laserDotPrefab = laserDotPrefab;

        detectors.Add(detector);
    }
}
