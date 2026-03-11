using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TargetManager : MonoBehaviour
{
    public RectTransform dotContainer;
    public GameObject targetPrefab;

    public int targetCount = 5;
    public float spawnAreaWidth = 800f;
    public float spawnAreaHeight = 400f;

    public float respawnDelay = 3f;

    public Text scoreText;

    private List<GameObject> targets = new List<GameObject>();
    private int score = 0;

    bool respawning = false;

    void Start()
    {
        SpawnTargets();
        UpdateScoreUI();
    }

    void Update()
    {
        if (!respawning && AllTargetsDestroyed())
        {
            StartCoroutine(RespawnRoutine());
        }
    }

    void SpawnTargets()
    {
        for (int i = 0; i < targetCount; i++)
        {
            Vector2 pos = new Vector2(
                Random.Range(-spawnAreaWidth / 2, spawnAreaWidth / 2),
                Random.Range(-spawnAreaHeight / 2, spawnAreaHeight / 2)
            );

            GameObject t = Instantiate(targetPrefab, dotContainer);
            RectTransform rt = t.GetComponent<RectTransform>();

            rt.anchoredPosition = pos;

            targets.Add(t);
        }
    }

    bool AllTargetsDestroyed()
    {
        foreach (var t in targets)
        {
            if (t.activeSelf)
                return false;
        }

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

    public void HitTarget(GameObject target)
    {
        if (!target.activeSelf)
            return;

        target.SetActive(false);

        score++;

        UpdateScoreUI();
    }

    void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score : " + score;
    }
}