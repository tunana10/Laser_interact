using UnityEngine;
using UnityEngine.EventSystems;

public class GridDraggablePoint : MonoBehaviour, IDragHandler
{
    public LaserDetector detector;

    public int gridX;
    public int gridY;

    RectTransform rt;

    void Start()
    {
        rt = GetComponent<RectTransform>();
    }

    public void OnDrag(PointerEventData eventData)
    {
        rt.anchoredPosition += eventData.delta;

        detector.UpdateGridVisual();
    }
}