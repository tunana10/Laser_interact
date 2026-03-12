using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    bool respawning = false;

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

        Debug.Log("LaserDetector 已連接");

        SpawnTargets();
        UpdateScoreUI();
    }

    void Update()
    {
        if (detector == null)
            return;

        if (!respawning && AllTargetsDestroyed())
        {
            StartCoroutine(RespawnRoutine());
        }
    }

    // ================================
    // Grid Spawn
    // ================================
    void SpawnTargets()
    {
        HashSet<Vector2Int> usedCells = new HashSet<Vector2Int>();

        for (int i = 0; i < targetCount; i++)
        {
            Vector2Int cell;

            do
            {
                int gx = Random.Range(0, detector.gridX);
                int gy = Random.Range(0, detector.gridY);

                cell = new Vector2Int(gx, gy);

            } while (usedCells.Contains(cell));

            usedCells.Add(cell);

            GameObject t = Instantiate(targetPrefab, dotContainer);

            PlaceTargetOnGrid(t, cell.x, cell.y);

            targets.Add(t);
        }
    }

    // ================================
    // Grid Position
    // ================================
    void PlaceTargetOnGrid(GameObject target, int gx, int gy)
    {
        RectTransform cam = detector.cameraRawImage.rectTransform;

        float cellW = cam.rect.width / detector.gridX;
        float cellH = cam.rect.height / detector.gridY;

        float px = (gx + 0.5f) * cellW;
        float py = (gy + 0.5f) * cellH;

        RectTransform rt = target.GetComponent<RectTransform>();

        rt.SetParent(cam);

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        rt.anchoredPosition = new Vector2(px, py);

        TargetCell tc = target.GetComponent<TargetCell>();
        if (tc == null)
            tc = target.AddComponent<TargetCell>();

        tc.gridX = gx;
        tc.gridY = gy;
    }

    // ================================
    // 檢查是否全部打掉
    // ================================
    bool AllTargetsDestroyed()
    {
        foreach (var t in targets)
        {
            if (t.activeSelf)
                return false;
        }

        return true;
    }

    // ================================
    // Respawn
    // ================================
    IEnumerator RespawnRoutine()
    {
        respawning = true;

        yield return new WaitForSeconds(respawnDelay);

        foreach (var t in targets)
            Destroy(t);

        targets.Clear();

        SpawnTargets();

        respawning = false;
    }

    // ================================
    // Hit
    // ================================
    public void HitTarget(GameObject target)
    {
        if (!target.activeSelf)
            return;

        target.SetActive(false);

        score++;

        UpdateScoreUI();
    }

    // ================================
    // UI
    // ================================
    void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score : " + score;
    }
}