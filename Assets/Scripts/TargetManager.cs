
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

        while (!detector.IsGridReady)
            yield return null;

        SpawnTargets();
        UpdateScoreUI();
    }

    void Update()
    {
        if (detector == null) return;

        if (!respawning && AllTargetsDestroyed())
            StartCoroutine(RespawnRoutine());
    }

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

    void PlaceTargetOnGrid(GameObject target, int gx, int gy)
    {
        RectTransform cam = detector.cameraRawImage.rectTransform;
        RectTransform rt = target.GetComponent<RectTransform>();

        rt.SetParent(cam);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
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
            if (t.activeSelf) return false;

        return true;
    }

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

    // 光點進入格子就算命中，不需要碰到 Target
    public void HitTargetByCell(int gx, int gy)
    {
        foreach (var t in targets)
        {
            if (!t.activeSelf) continue;

            TargetCell tc = t.GetComponent<TargetCell>();
            if (tc != null && tc.gridX == gx && tc.gridY == gy)
            {
                // 重新計算 ID：從左上角開始編號，依行（左->右）再列（上->下）
                int id = (detector.gridY - 1 - tc.gridY) * detector.gridX + tc.gridX;
                Debug.Log($"Hit Target Grid ({tc.gridX},{tc.gridY}) ID:{id}");

                t.SetActive(false);
                score++;
                UpdateScoreUI();
                break;
            }
        }
    }

    void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score : " + score;
    }
}