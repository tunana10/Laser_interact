using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CameraProcessor:
/// - 使用 WebCamTexture 取得影像
/// - 閾值化 (threshold) 找到亮點
/// - 使用連通元件 labeling 找出每個 blob 的面積與重心
/// - 輸出 normalized blob positions (0..1)
/// - 提供 inspector 中的 srcPoints / dstPoints 用於計算 Homography
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CameraProcessor : MonoBehaviour
{
    [Header("WebCam")]
    public int webcamDeviceIndex = 0;
    public int captureWidth = 640;
    public int captureHeight = 480;
    public int captureFPS = 30;

    [Header("Preview UI")]
    public RawImage previewUI; // 可以在 MultiCamManager 自動指定

    [Header("Detection")]
    [Range(0, 255)] public int threshold = 200; // brightness threshold
    public int minBlobSize = 6; // pixel
    public bool invert = false; // 若需要反向 (dark spot)

    [Header("Calibration (src in image pixel coords, dst in screen pixel coords)")]
    public Vector2[] srcPoints; // e.g. new Vector2[4]
    public Vector2[] dstPoints; // same length as srcPoints
    public bool homographyComputed = false;

    private WebCamTexture wtex;
    private Texture2D frameTex;
    private Color32[] pixels;
    private int texW, texH;

    // homography
    private Homography homography = null;

    // detected blobs (normalized 0..1)
    public List<Vector2> detectedBlobs = new List<Vector2>();
    public int CurrentBlobsCount { get { return detectedBlobs.Count; } }

    void Start()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (webcamDeviceIndex < 0 || webcamDeviceIndex >= devices.Length)
        {
            Debug.LogError($"Invalid webcam index {webcamDeviceIndex}");
            return;
        }

        string devName = devices[webcamDeviceIndex].name;
        wtex = new WebCamTexture(devName, captureWidth, captureHeight, captureFPS);
        wtex.Play();

        texW = captureWidth;
        texH = captureHeight;
        frameTex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        pixels = new Color32[texW * texH];

        if (previewUI != null)
        {
            previewUI.texture = frameTex;
        }

        if (srcPoints != null && dstPoints != null && srcPoints.Length >= 4 && srcPoints.Length == dstPoints.Length)
        {
            ComputeHomography();
        }
    }

    void OnDestroy()
    {
        if (wtex != null) wtex.Stop();
    }

    void Update()
    {
        if (wtex == null || !wtex.isPlaying) return;
        if (wtex.width <= 16) return; // not ready yet

        // get pixels
        frameTex.SetPixels32(wtex.GetPixels32());
        frameTex.Apply();
        previewUI.texture = frameTex;

        // process
        pixels = frameTex.GetPixels32();
        detectedBlobs.Clear();
        var labels = new int[texW * texH]; // 0 = background, >0 = label
        int currentLabel = 0;

        // brightness binary map
        byte[] mask = new byte[texW * texH];
        for (int y = 0; y < texH; y++)
        {
            int row = y * texW;
            for (int x = 0; x < texW; x++)
            {
                Color32 c = pixels[row + x];
                // compute brightness (convert to grayscale)
                int bright = (c.r * 299 + c.g * 587 + c.b * 114) / 1000;
                bool hit = invert ? (bright < threshold) : (bright > threshold);
                mask[row + x] = (byte)(hit ? 1 : 0);
            }
        }

        // connected components (4-neighbor)
        for (int y = 0; y < texH; y++)
        {
            int row = y * texW;
            for (int x = 0; x < texW; x++)
            {
                int idx = row + x;
                if (mask[idx] == 0 || labels[idx] != 0) continue;
                // new component
                currentLabel++;
                int minx = x, maxx = x, miny = y, maxy = y;
                int sumx = 0, sumy = 0, count = 0;
                // BFS stack
                var stack = new System.Collections.Generic.Stack<int>();
                stack.Push(idx);
                labels[idx] = currentLabel;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int px = p % texW;
                    int py = p / texW;
                    count++;
                    sumx += px; sumy += py;
                    if (px < minx) minx = px; if (px > maxx) maxx = px;
                    if (py < miny) miny = py; if (py > maxy) maxy = py;
                    // neighbors
                    int nx, ny, ni;
                    nx = px - 1; ny = py; if (nx >= 0) { ni = ny * texW + nx; if (mask[ni] == 1 && labels[ni] == 0) { labels[ni] = currentLabel; stack.Push(ni); } }
                    nx = px + 1; ny = py; if (nx < texW) { ni = ny * texW + nx; if (mask[ni] == 1 && labels[ni] == 0) { labels[ni] = currentLabel; stack.Push(ni); } }
                    nx = px; ny = py - 1; if (ny >= 0) { ni = ny * texW + nx; if (mask[ni] == 1 && labels[ni] == 0) { labels[ni] = currentLabel; stack.Push(ni); } }
                    nx = px; ny = py + 1; if (ny < texH) { ni = ny * texW + nx; if (mask[ni] == 1 && labels[ni] == 0) { labels[ni] = currentLabel; stack.Push(ni); } }
                }
                // evaluate component
                if (count >= minBlobSize)
                {
                    float cx = (float)sumx / count;
                    float cy = (float)sumy / count;
                    // normalized 0..1 in image coords (x left->right, y top->bottom)
                    float nxv = cx / (float)texW;
                    float nyv = 1f - (cy / (float)texH); // flip y so 0 bottom
                    detectedBlobs.Add(new Vector2(nxv, nyv));
                }
            }
        }

        // we now have detectedBlobs (normalized). Optionally map them via homography:
        if (homographyComputed && homography != null && detectedBlobs.Count > 0)
        {
            // map all and replace with mapped coords (in destination pixel space)
            for (int i = 0; i < detectedBlobs.Count; i++)
            {
                Vector2 imgN = detectedBlobs[i];
                // convert normalized back to pixel coords of source
                Vector2 srcPx = new Vector2(imgN.x * texW, (1f - imgN.y) * texH); // note flip
                Vector2 dstPx = homography.TransformPoint(srcPx);
                detectedBlobs[i] = dstPx; // override with projected pixel coordinates
            }
        }
    }

    // --- Call to compute homography when srcPoints/dstPoints assigned in inspector ---
    public void ComputeHomography()
    {
        if (srcPoints == null || dstPoints == null || srcPoints.Length < 4 || srcPoints.Length != dstPoints.Length)
        {
            homographyComputed = false;
            Debug.LogWarning("Need 4+ matching src/dst points to compute homography.");
            return;
        }
        // srcPoints currently assumed in image pixel coords (if you used normalized, convert accordingly)
        homography = Homography.FromCorrespondences(srcPoints, dstPoints);
        homographyComputed = (homography != null);
        Debug.Log("Homography computed: " + homographyComputed);
    }

    // Helper: get latest mapped blobs
    public List<Vector2> GetMappedBlobs()
    {
        return new List<Vector2>(detectedBlobs);
    }
}
