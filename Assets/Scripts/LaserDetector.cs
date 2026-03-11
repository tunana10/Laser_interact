using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LaserDetector : MonoBehaviour
{
    [Header("Camera")]
    public WebCamTexture sourceWebcam;
    public RenderTexture renderTexture;
    public RawImage cameraRawImage;

    [Header("Detection")]
    public Slider thresholdSlider;
    [Range(0f, 1f)]
    public float threshold = 0.5f;

    public int highPowerPixelThreshold = 200;  // 300mW 判定面積
    public int minClusterPixelCount = 50;      // 過濾雜訊

    [Header("Cluster Distance Filter")]
    public float minDistanceBetweenClusters = 150f; //  新增：cluster 最小距離（像素）

    [Header("UI")]
    public GameObject laserDotPrefab;

    [Header("Performance")]
    public int processEveryNFrames = 2;

    [Header("Target")]
    TargetManager targetManager;
    public float targetHitRadius = 120f;
    private List<RectTransform> targets = new List<RectTransform>();

    private Texture2D tempTex;
    private List<RectTransform> laserDots = new List<RectTransform>();

    // ====== Cluster 結構 ======
    class BrightCluster
    {
        public Vector2 center;
        public float averageBrightness;
        public float maxBrightness;
        public int pixelCount;
    }

    void Start()
    {
        tempTex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
        targetManager = FindObjectOfType<TargetManager>();

        //GameObject[] objs = GameObject.FindGameObjectsWithTag("Target");
        //foreach (var o in objs)
        //{
        //    RectTransform rt = o.GetComponent<RectTransform>();
        //    if (rt != null)
        //        targets.Add(rt);
        //}
    }

    void Update()
    {
        if (sourceWebcam == null || !sourceWebcam.isPlaying)
            return;

        if (Time.frameCount % processEveryNFrames != 0)
            return;

        if (thresholdSlider != null)
            threshold = thresholdSlider.value;

        Graphics.Blit(sourceWebcam, renderTexture);

        RenderTexture.active = renderTexture;
        tempTex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;

        DetectLaserClusters();
    }

    void DetectLaserClusters()
    {
        int width = tempTex.width;
        int height = tempTex.height;

        Color32[] pixels = tempTex.GetPixels32();
        float[] brightness = new float[pixels.Length];
        bool[] visited = new bool[pixels.Length];

        // 建立亮度 map
        for (int i = 0; i < pixels.Length; i++)
        {
            brightness[i] = (pixels[i].r + pixels[i].g + pixels[i].b) / (3f * 255f);
        }

        List<BrightCluster> clusters = new List<BrightCluster>();

        // 搜尋 cluster
        for (int i = 0; i < pixels.Length; i++)
        {
            if (visited[i]) continue;
            if (brightness[i] < threshold) continue;

            BrightCluster cluster = FloodFill(i, width, height, brightness, visited);

            if (cluster.pixelCount >= minClusterPixelCount)
                clusters.Add(cluster);
        }

        //  新增：距離過濾（Non-Maximum Suppression）
        clusters = FilterClustersByDistance(clusters);

        UpdateLaserDots(clusters, width, height);
    }

    BrightCluster FloodFill(int startIndex, int width, int height, float[] brightness, bool[] visited)
    {
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(startIndex);

        float sumBrightness = 0f;
        float maxBrightness = 0f;
        int count = 0;
        float sumX = 0f;
        float sumY = 0f;

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            if (visited[idx]) continue;

            visited[idx] = true;

            if (brightness[idx] < threshold)
                continue;

            int x = idx % width;
            int y = idx / width;

            sumBrightness += brightness[idx];
            maxBrightness = Mathf.Max(maxBrightness, brightness[idx]);
            count++;

            sumX += x;
            sumY += y;

            TryAddNeighbor(queue, idx - 1, x > 0);
            TryAddNeighbor(queue, idx + 1, x < width - 1);
            TryAddNeighbor(queue, idx - width, y > 0);
            TryAddNeighbor(queue, idx + width, y < height - 1);
        }

        BrightCluster c = new BrightCluster();

        if (count > 0)
        {
            c.center = new Vector2(sumX / count, sumY / count);
            c.averageBrightness = sumBrightness / count;
            c.maxBrightness = maxBrightness;
            c.pixelCount = count;
        }

        return c;
    }

    void TryAddNeighbor(Queue<int> queue, int index, bool condition)
    {
        if (condition)
            queue.Enqueue(index);
    }

    //  核心：距離過濾
    List<BrightCluster> FilterClustersByDistance(List<BrightCluster> clusters)
    {
        List<BrightCluster> result = new List<BrightCluster>();

        // 先依 pixelCount 由大到小排序（保留大光斑）
        clusters.Sort((a, b) => b.pixelCount.CompareTo(a.pixelCount));

        foreach (var cluster in clusters)
        {
            bool tooClose = false;

            foreach (var kept in result)
            {
                float dist = Vector2.Distance(cluster.center, kept.center);
                if (dist < minDistanceBetweenClusters)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
                result.Add(cluster);
        }

        return result;
    }

    void UpdateLaserDots(List<BrightCluster> clusters, int width, int height)
    {
        for (int i = clusters.Count; i < laserDots.Count; i++)
            laserDots[i].gameObject.SetActive(false);

        for (int i = 0; i < clusters.Count; i++)
        {
            BrightCluster c = clusters[i];

            Vector2 px = c.center;

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

            Image img = dot.GetComponent<Image>();

            bool isHighPower = c.pixelCount >= highPowerPixelThreshold;

            if (isHighPower)
            {
                img.color = Color.green;

                CheckTargetHit(dot);   // 新增
            }
            else
            {
                img.color = Color.white;
            }


        }
    }
    void CheckTargetHit(RectTransform laserDot)
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag("Target");

        foreach (var t in targets)
        {
            if (!t.activeSelf)
                continue;

            RectTransform rt = t.GetComponent<RectTransform>();

            float dist = Vector2.Distance(
                laserDot.anchoredPosition,
                rt.anchoredPosition
            );

            if (dist < targetHitRadius)
            {
                targetManager.HitTarget(t);
            }
        }
    }

}