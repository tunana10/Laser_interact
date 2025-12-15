using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetManager : MonoBehaviour
{
    [Header("UI 放置區域")]
    public RectTransform targetContainer;         // 放置目標物的區域（通常與 CameraContainer 大小一致）

    [Header("Prefabs")]
    public RectTransform targetPrefab;            // 你做好的 Target prefab（UI Image）

    [Header("目標物設定")]
    public int minTargets = 3;
    public int maxTargets = 6;
    public float targetRadius = 40f;              // 點擊判定半徑

    [Header("再生設定")]
    public float respawnDelay = 3f;

    private List<RectTransform> currentTargets = new List<RectTransform>();
    private bool respawning = false;

    public static object Instance { get; internal set; }

    void Start()
    {
        SpawnTargets();
    }

    void Update()
    {
        //不用在這裡做事情，由 LaserDetector 呼叫 HitCheck()
    }

    //============================================================
    // ★ 被 LaserDetector 呼叫，檢查是否有擊中任一目標物
    //============================================================
    public void HitCheck(Vector2 laserPos)
    {

        if (currentTargets.Count == 0)
            return;

        Debug.Log("Hit check!");
        for (int i = currentTargets.Count - 1; i >= 0; i--)
        {
            RectTransform target = currentTargets[i];

            // Target 座標
            Vector2 targetPos = target.anchoredPosition;

            float dist = Vector2.Distance(laserPos, targetPos);

            if (dist <= targetRadius)
            {
                Debug.Log("Hit!");
                Destroy(target.gameObject);
                currentTargets.RemoveAt(i);
            }
        }

        // 若全部被打掉 → 等待再生
        if (currentTargets.Count == 0 && !respawning)
        {
            respawning = true;
            StartCoroutine(Respawn());
        }
    }

    //============================================================
    // ★ 生成目標物
    //============================================================
    void SpawnTargets()
    {
        currentTargets.Clear();

        int targetCount = Random.Range(minTargets, maxTargets + 1);

        for (int i = 0; i < targetCount; i++)
        {
            RectTransform target = Instantiate(targetPrefab, targetContainer);
            target.anchoredPosition = RandomInside(targetContainer);
            currentTargets.Add(target);
        }

        respawning = false;
    }

    //============================================================
    // ★ 隨機位置（container 範圍內）
    //============================================================
    Vector2 RandomInside(RectTransform area)
    {
        float w = area.rect.width;
        float h = area.rect.height;

        float x = Random.Range(-w / 2f, w / 2f);
        float y = Random.Range(-h / 2f, h / 2f);

        return new Vector2(x, y);
    }

    //============================================================
    // ★ 延遲再生
    //============================================================
    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnDelay);
        SpawnTargets();
    }
}
