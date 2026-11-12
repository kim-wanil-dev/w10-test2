using UnityEngine;

using UnityEngine.EventSystems;

/// <summary>접착 작업 격자와 두 원본의 실제 점유 영역을 그리고 스냅 드래그를 전달한다.</summary>
public sealed class SpellGlueGraphic : UnityEngine.UI.MaskableGraphic, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private const float CELL_SIZE = 72;
    private SpellGameUI _ui;
    private int _dragPart = -1;
    private Vector2 _dragOffset;
    private Vector2 _dragPoint;

    public bool IsDragging { get { return _dragPart >= 0; } }
    public int DragPart { get { return _dragPart; } }

    /// <summary>회전 전 높이를 사용해 잡은 지점을 함께 회전하고 마지막 포인터 위치에서 조각을 즉시 갱신한다.</summary>
    public void RotateDragOffset(int previousHeight)
    {
        if (_dragPart < 0) return;
        _dragOffset = new Vector2(previousHeight - _dragOffset.y, _dragOffset.x);
        Vector2 origin = _dragPoint - _dragOffset;
        _ui.MoveGluePart(_dragPart, new Vector2Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.y)));
    }

    /// <summary>접착 미리 보기의 소유 UI를 연결한다.</summary>
    public void Setup(SpellGameUI ui) { _ui = ui; SetVerticesDirty(); }

    /// <summary>두 원본의 점유 삼각형과 격자, 접착 상태 테두리를 그린다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        if (_ui == null) return;
        Rect rect = rectTransform.rect;
        Quad(vh, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.width, rect.height), new Color(0.16f, 0.12f, 0.10f));
        for (int x = 0; x <= 8; x++) Quad(vh, new Vector2(rect.xMin + x * CELL_SIZE, rect.yMax), new Vector2(1, rect.height), new Color(0.35f, 0.30f, 0.25f));
        for (int y = 0; y <= 4; y++) Quad(vh, new Vector2(rect.xMin, rect.yMax - y * CELL_SIZE), new Vector2(rect.width, 1), new Color(0.35f, 0.30f, 0.25f));
        bool valid = _ui.GluePlacementIssue() == null;
        for (int part = 0; part < 2; part++)
        {
            SpellGame.Card card = _ui.GlueCard(part);
            if (card == null) continue;
            int rotation = _ui.GlueRotation(part);
            Vector2Int position = _ui.GluePosition(part);
            Color fill = part == 0 ? new Color(0.35f, 0.68f, 0.87f, 0.85f) : new Color(0.9f, 0.69f, 0.32f, 0.85f);
            if (_ui.IsGluePartCursed(part)) fill = Color.red;
            Color edge = valid ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.35f, 0.32f);
            for (int y = 0; y < SpellGame.ShapeHeight(card, rotation); y++) for (int x = 0; x < SpellGame.ShapeWidth(card, rotation); x++)
            {
                int mask = SpellGame.CellMask(card, rotation, x, y);
                Vector2 origin = new Vector2(rect.xMin + (position.x + x) * CELL_SIZE, rect.yMax - (position.y + y) * CELL_SIZE);
                Vector2 center = origin + new Vector2(CELL_SIZE / 2, -CELL_SIZE / 2);
                Vector2[] corners = { origin, origin + new Vector2(CELL_SIZE, 0), origin + new Vector2(CELL_SIZE, -CELL_SIZE), origin + new Vector2(0, -CELL_SIZE) };
                for (int side = 0; side < 4; side++)
                {
                    if ((mask & (1 << side)) == 0) continue;
                    Triangle(vh, center, corners[side], corners[(side + 1) % 4], fill);
                    if (_ui.SelectedGluePart == part)
                    {
                        Line(vh, center, corners[side], edge);
                        Line(vh, center, corners[(side + 1) % 4], edge);
                        Line(vh, corners[side], corners[(side + 1) % 4], edge);
                    }
                }
            }
        }
    }

    /// <summary>현재 또는 누르기 시작한 화면 좌표를 작업 공간의 실수 격자 좌표로 변환한다.</summary>
    private Vector2 GridPoint(PointerEventData data, bool usePressPosition = false)
    {
        Vector2 point;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, usePressPosition ? data.pressPosition : data.position, data.pressEventCamera, out point);
        return new Vector2((point.x - rectTransform.rect.xMin) / CELL_SIZE, (rectTransform.rect.yMax - point.y) / CELL_SIZE);
    }

    /// <summary>손패 드롭이 작업 공간 안에 있는지 확인하고 스냅할 격자 원점을 반환한다.</summary>
    public bool TryDropPosition(PointerEventData data, out Vector2Int position)
    {
        Vector2 point = GridPoint(data);
        position = new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
        return point.x >= 0 && point.x < 8 && point.y >= 0 && point.y < 4;
    }

    /// <summary>포인터가 실제로 점유된 삼각 구역에 닿는 원본 번호를 반환한다.</summary>
    private int HitPart(Vector2 point)
    {
        int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
        float dx = point.x - x - 0.5f, dy = point.y - y - 0.5f;
        int sector = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        for (int part = 1; part >= 0; part--)
        {
            SpellGame.Card card = _ui.GlueCard(part);
            Vector2Int pos = _ui.GluePosition(part);
            if (card != null && (SpellGame.CellMask(card, _ui.GlueRotation(part), x - pos.x, y - pos.y) & (1 << sector)) != 0) return part;
        }
        return -1;
    }

    public void OnPointerClick(PointerEventData data)
    {
        if (!isActiveAndEnabled || data.button != PointerEventData.InputButton.Left) return;
        Vector2 point = GridPoint(data);
        int part = HitPart(point);
        if (part >= 0) _ui.SelectGluePart(part);
        else _ui.MoveGluePart(_ui.SelectedGluePart, new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y)));
    }

    public void OnBeginDrag(PointerEventData data)
    {
        if (!isActiveAndEnabled || data.button != PointerEventData.InputButton.Left) return;
        Vector2 point = GridPoint(data, true);
        _dragPart = HitPart(point);
        if (_dragPart < 0) return;
        _dragOffset = point - (Vector2)_ui.GluePosition(_dragPart);
        _dragPoint = GridPoint(data);
        _ui.SelectGluePart(_dragPart);
    }

    public void OnDrag(PointerEventData data)
    {
        if (!isActiveAndEnabled || _dragPart < 0 || data.button != PointerEventData.InputButton.Left) return;
        _dragPoint = GridPoint(data);
        Vector2 origin = _dragPoint - _dragOffset;
        _ui.MoveGluePart(_dragPart, new Vector2Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.y)));
    }

    public void OnEndDrag(PointerEventData data)
    {
        if (!isActiveAndEnabled || _dragPart < 0 || data.button != PointerEventData.InputButton.Left) return;
        int part = _dragPart;
        OnDrag(data); _dragPart = -1;
        _ui.SelectGluePart(part);
    }

    /// <summary>작업 공간 배경 또는 격자 선을 사각형으로 추가한다.</summary>
    private static void Quad(UnityEngine.UI.VertexHelper vh, Vector2 origin, Vector2 size, Color color)
    {
        Triangle(vh, origin, origin + new Vector2(size.x, 0), origin - new Vector2(0, size.y), color);
        Triangle(vh, origin + new Vector2(size.x, 0), origin + new Vector2(size.x, -size.y), origin - new Vector2(0, size.y), color);
    }

    /// <summary>도형이나 선을 표시할 삼각형을 메시 끝에 추가한다.</summary>
    private static void Triangle(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int start = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert; vertex.color = color;
        vertex.position = a; vh.AddVert(vertex); vertex.position = b; vh.AddVert(vertex); vertex.position = c; vh.AddVert(vertex);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>선택한 조각의 실제 삼각 경계를 두께 있는 선으로 표시한다.</summary>
    private static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Color color)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * 1.5f;
        Triangle(vh, a + normal, b + normal, b - normal, color);
        Triangle(vh, a + normal, b - normal, a - normal, color);
    }
}

