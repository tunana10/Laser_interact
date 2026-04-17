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
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out offset
        );
        offset = rectTransform.anchoredPosition - offset;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint
        ))
        {
            Vector2 newAnchored = localPoint + offset;

            // 如果是外角（四個角其中一個），則呼叫 Detector 的方法以整體變形（bilinear warp）
            bool isCorner = (gridX == 0 || (detector != null && gridX == detector.gridX)) &&
                            (gridY == 0 || (detector != null && gridY == detector.gridY));

            if (isCorner && detector != null)
            {
                // 讓 Detector 依新的這個角位置重算所有 control points
                detector.DragCornerAndWarp(gridX, gridY, newAnchored);
            }
            else
            {
                // 一般點拖曳（維持原行為）
                rectTransform.anchoredPosition = newAnchored;

                detector.UpdateGridVisual();
                detector.UpdateTargetsPosition();
                detector.SaveGridState();
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        detector.SaveGridState();
    }
}
