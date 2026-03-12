using UnityEngine;
using UnityEngine.EventSystems;

public class GridDraggablePoint : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    public LaserDetector detector;
    public int gridX;
    public int gridY;

    private RectTransform rectTransform;
    private Vector2 offset;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform.parent as RectTransform,
            eventData.position, eventData.pressEventCamera, out offset);
        offset = rectTransform.anchoredPosition - offset;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform.parent as RectTransform,
            eventData.position, eventData.pressEventCamera, out localPoint))
        {
            rectTransform.anchoredPosition = localPoint + offset;
            detector.UpdateGridVisual();
            detector.UpdateTargetsPosition();

            // 即時存檔
            detector.SaveGridState();
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 拖動結束也存一次，確保最後位置儲存
        detector.SaveGridState();
    }
}