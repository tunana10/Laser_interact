using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LaserDetector : MonoBehaviour
{
    public WebCamTexture sourceWebcam;
    public RenderTexture renderTexture;
    public RawImage cameraRawImage;
    public Slider thresholdSlider;
    public float threshold = 0.8f;
    public GameObject laserDotPrefab; // UI Image prefab，複數光點使用

    private Texture2D tempTex;
    private TargetManager targetManager;

    // 多個紅點的管理
    private List<RectTransform> laserDots = new List<RectTransform>();

    // 分群距離（像素）
    public float minDistanceBetweenDots = 300f;

    void Start()
    {
        tempTex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
        targetManager = GameObject.Find("TargetManager").GetComponent<TargetManager>();
    }

    void Update()
    {
        if (sourceWebcam == null || !sourceWebcam.isPlaying)
            return;

        if (thresholdSlider != null)
            threshold = thresholdSlider.value;

        Graphics.Blit(sourceWebcam, renderTexture);

        RenderTexture.active = renderTexture;
        tempTex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;

        int width = tempTex.width;
        int height = tempTex.height;
        Color32[] pixels = tempTex.GetPixels32();

        List<Vector2> detectedPoints = new List<Vector2>();

        // 尋找亮點
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = (pixels[i].r + pixels[i].g + pixels[i].b) / (3f * 255f);

            if (v > threshold)
            {
                int x = i % width;
                int y = i / width;
                Vector2 newPoint = new Vector2(x, y);

                // 如果這個點離現有 detectedPoints 太近，視為同一 cluster，跳過
                bool tooClose = false;
                foreach (var p in detectedPoints)
                {
                    if (Vector2.Distance(p, newPoint) < minDistanceBetweenDots)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose)
                    detectedPoints.Add(newPoint);
            }
        }

        // 先隱藏多餘紅點
        for (int i = detectedPoints.Count; i < laserDots.Count; i++)
            laserDots[i].gameObject.SetActive(false);

        // 顯示/更新紅點
        for (int i = 0; i < detectedPoints.Count; i++)
        {
            Vector2 px = detectedPoints[i];

            // 轉成 normalized
            float nx = px.x / width;
            float ny = px.y / height;

            Vector2 localPos = new Vector2(
                (nx - 0.5f) * cameraRawImage.rectTransform.rect.width,
                (ny - 0.5f) * cameraRawImage.rectTransform.rect.height
            );

            Vector2 finalPos = cameraRawImage.rectTransform.anchoredPosition + localPos;

            RectTransform dot;
            if (i < laserDots.Count)
            {
                dot = laserDots[i];
                dot.gameObject.SetActive(true);
            }
            else
            {
                GameObject obj = Instantiate(laserDotPrefab, cameraRawImage.transform.parent);
                dot = obj.GetComponent<RectTransform>();
                laserDots.Add(dot);
            }

            dot.anchoredPosition = finalPos;

            // 傳給 TargetManager
            if (targetManager != null)
                targetManager.HitCheck(finalPos);
        }
    }
}
