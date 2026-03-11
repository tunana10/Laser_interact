using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MultiCamManager:
/// - 自動建立多個 CameraProcessor (one per webcam)
/// - 建立 UI RawImage 顯示各 camera 影像（方便校正）
///
/// 使用方式:
/// 1. 把此 script 掛在空 GameObject (MultiCamManager)
/// 2. 指定 desiredCameraCount 或 cameraIndices (可留空自動)
/// 3. 指定 prefabRawImage (UI RawImage) 與 parentCanvas
/// 4. Play
/// </summary>
public class MultiCamManager : MonoBehaviour
{
    [Header("Camera selection")]
    public int desiredCameraCount = 1; // 想要啟動幾台（若超過可用 camera，會以可用數量為準）
    public int[] cameraIndices = new int[0]; // 若填，會使用指定 index

    [Header("UI")]
    public Canvas parentCanvas; // 放 RawImage 的 Canvas
    public RawImage rawImagePrefab; // 一個簡單 RawImage Prefab

    [Header("Processor Prefab")]
    public GameObject cameraProcessorPrefab; // optional: 若你有 prefab，或會用內建 dynamically add

    private List<CameraProcessor> processors = new List<CameraProcessor>();

    void Start()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No webcams found on this machine.");
            return;
        }

        List<int> indicesToStart = new List<int>();
        if (cameraIndices != null && cameraIndices.Length > 0)
        {
            foreach (int idx in cameraIndices)
                if (idx >= 0 && idx < devices.Length) indicesToStart.Add(idx);
        }
        else
        {
            // auto-select first N devices
            for (int i = 0; i < Mathf.Min(desiredCameraCount, devices.Length); i++)
                indicesToStart.Add(i);
        }

        // create UI and processors
        float w = parentCanvas.pixelRect.width;
        float h = parentCanvas.pixelRect.height;
        int n = indicesToStart.Count;
        for (int i = 0; i < n; i++)
        {
            int devIndex = indicesToStart[i];

            // create UI RawImage
            RawImage r;
            if (rawImagePrefab != null)
                r = Instantiate(rawImagePrefab, parentCanvas.transform);
            else
            {
                GameObject go = new GameObject($"CamRaw_{i}", typeof(RawImage));
                go.transform.SetParent(parentCanvas.transform, false);
                r = go.GetComponent<RawImage>();
                r.rectTransform.sizeDelta = new Vector2(320, 240);
            }

            // layout: horizontal row
            r.rectTransform.anchoredPosition = new Vector2((i * (r.rectTransform.sizeDelta.x + 10)), 0);

            // create processor object
            GameObject procGO = new GameObject($"CamProcessor_{i}");
            procGO.transform.SetParent(transform, false);
            CameraProcessor cp = procGO.AddComponent<CameraProcessor>();
            cp.webcamDeviceIndex = devIndex;
            cp.previewUI = r;

            processors.Add(cp);
        }
    }

    void Update()
    {
        // for debug: press space to log how many blobs total
        if (Input.GetKeyDown(KeyCode.Space))
        {
            int total = 0;
            foreach (var p in processors)
            {
                total += p.CurrentBlobsCount;
            }
            Debug.Log($"Total blobs across cameras: {total}");
        }
    }
}
