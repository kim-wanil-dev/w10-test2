using UnityEngine;

using UnityEngine.EventSystems;

/// <summary>
/// 접착 작업 격자와 두 원본의 실제 점유 영역을 그리고 스냅 드래그를 전달한다.
/// 격자는 도안 판과 같이 내부 칸 좌표를 화면에서 45도 돌려 그리며, 화면 직사각형 안에 온전히 들어가는 1/4 구역만 작업 공간으로 쓴다.
/// </summary>
public sealed class SpellGlueGraphic : UnityEngine.UI.MaskableGraphic, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const int GRID_SIZE = 10;
    public const float CELL_SIZE = 72;
    // 작업 공간 화면 직사각형의 가로·세로 절반 길이(칸 한 변 단위)다.
    public const float HALF_WIDTH = 4f;
    public const float HALF_HEIGHT = 2f;
    private const float GRID_LINE_WIDTH = 1.2f;
    private const float EDGE_WIDTH = 3f;
    private const float SQRT2 = 1.41421356f;
    private const float EPSILON = 0.001f;

    private static readonly Vector2 GRID_CENTER = new Vector2(GRID_SIZE / 2f, GRID_SIZE / 2f);

    private readonly Vector2[] _points = new Vector2[3];
    private SpellGameUI _ui;
    private float _cellGap;
    private Color _emptyColor;
    private Color _lineColor;
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

    /// <summary>접착 미리 보기의 소유 UI와 도안 격자와 같은 빈 칸 색, 격자선 색, 칸 간격을 연결한다.</summary>
    public void Setup(SpellGameUI ui, Color emptyColor, Color lineColor, float cellGap)
    {
        _ui = ui; _emptyColor = emptyColor; _lineColor = lineColor; _cellGap = cellGap; SetVerticesDirty();
    }

    /// <summary>작업 공간 격자 좌표를 중심 기준 화면 방향 좌표(x 오른쪽, y 아래, 칸 한 변 단위)로 바꾼다. 도안 판과 같은 45도 회전이다.</summary>
    private static Vector2 GridToBoard(Vector2 grid)
    {
        Vector2 d = grid - GRID_CENTER;
        return new Vector2(d.x - d.y, d.x + d.y) / SQRT2;
    }

    /// <summary>중심 기준 화면 방향 좌표를 작업 공간 격자 좌표로 바꾼다.</summary>
    private static Vector2 BoardToGrid(Vector2 board)
    {
        return GRID_CENTER + new Vector2(board.x + board.y, board.y - board.x) / SQRT2;
    }

    /// <summary>칸 (x, y)의 지정 구역 삼각형 꼭짓점 두 개와 칸 중심을 격자 좌표로 _points에 채운다.</summary>
    private void FillSectorPoints(int x, int y, int sector)
    {
        _points[0] = SectorCorner(x, y, sector); _points[1] = SectorCorner(x, y, (sector + 1) % 4); _points[2] = new Vector2(x + 0.5f, y + 0.5f);
    }

    /// <summary>칸 (x, y)의 왼쪽 위부터 시계 방향 모서리 하나를 격자 좌표로 반환한다.</summary>
    private static Vector2 SectorCorner(int x, int y, int corner)
    {
        return new Vector2(x + (corner == 1 || corner == 2 ? 1 : 0), y + (corner >= 2 ? 1 : 0));
    }

    /// <summary>지정 1/4 구역 삼각형이 작업 공간 화면 직사각형 안에 온전히 들어가는지 반환한다.</summary>
    public static bool ContainsSector(int x, int y, int sector)
    {
        if (x < 0 || y < 0 || x >= GRID_SIZE || y >= GRID_SIZE) return false;
        Vector2[] points = { SectorCorner(x, y, sector), SectorCorner(x, y, (sector + 1) % 4), new Vector2(x + 0.5f, y + 0.5f) };
        foreach (Vector2 point in points)
        {
            Vector2 board = GridToBoard(point);
            if (Mathf.Abs(board.x) > HALF_WIDTH + EPSILON || Mathf.Abs(board.y) > HALF_HEIGHT + EPSILON) return false;
        }
        return true;
    }

    /// <summary>카드를 지정 방향과 왼쪽 위 좌표로 놓았을 때 모든 점유 구역이 작업 공간 안에 있는지 반환한다.</summary>
    public static bool Contains(SpellGame.Card card, int rotation, Vector2Int position)
    {
        for (int y = 0; y < SpellGame.ShapeHeight(card, rotation); y++) for (int x = 0; x < SpellGame.ShapeWidth(card, rotation); x++)
        {
            int mask = SpellGame.CellMask(card, rotation, x, y);
            for (int sector = 0; sector < 4; sector++)
                if ((mask & (1 << sector)) != 0 && !ContainsSector(position.x + x, position.y + y, sector)) return false;
        }
        return true;
    }

    /// <summary>격자 좌표를 이 그래픽의 로컬 픽셀 좌표로 바꾼다.</summary>
    private Vector2 ToLocal(Vector2 grid)
    {
        Vector2 board = GridToBoard(grid);
        return rectTransform.rect.center + new Vector2(board.x, -board.y) * CELL_SIZE;
    }

    /// <summary>작업 공간 구역, 45도 격자선, 두 원본의 점유 삼각형과 선택 조각의 접착 상태 테두리를 그린다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        if (_ui == null) return;
        for (int y = 0; y < GRID_SIZE; y++) for (int x = 0; x < GRID_SIZE; x++) for (int sector = 0; sector < 4; sector++)
        {
            if (!ContainsSector(x, y, sector)) continue;
            FillSectorPoints(x, y, sector);
            Triangle(vh, ToLocal(_points[0]), ToLocal(_points[1]), ToLocal(_points[2]), _emptyColor);
        }
        for (int y = 0; y < GRID_SIZE; y++) for (int x = 0; x < GRID_SIZE; x++) for (int sector = 0; sector < 4; sector++)
        {
            if (!ContainsSector(x, y, sector)) continue;
            FillSectorPoints(x, y, sector);
            Vector2 a = ToLocal(_points[0]), b = ToLocal(_points[1]), center = ToLocal(_points[2]);
            Line(vh, a, b, _lineColor, GRID_LINE_WIDTH); Line(vh, center, a, _lineColor, GRID_LINE_WIDTH); Line(vh, center, b, _lineColor, GRID_LINE_WIDTH);
        }
        bool valid = _ui.GluePlacementIssue() == null;
        // 도안 판과 같이 조각 삼각형을 칸 중심 쪽으로 칸 간격만큼 당겨 격자선이 보이게 한다.
        float shrink = Mathf.Max(0f, 1f - 2f * _cellGap / CELL_SIZE);
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
                for (int sector = 0; sector < 4; sector++)
                {
                    if ((mask & (1 << sector)) == 0) continue;
                    FillSectorPoints(position.x + x, position.y + y, sector);
                    Vector2 center = ToLocal(_points[2]);
                    Vector2 a = center + (ToLocal(_points[0]) - center) * shrink, b = center + (ToLocal(_points[1]) - center) * shrink;
                    Triangle(vh, center, a, b, fill);
                    if (_ui.SelectedGluePart != part) continue;
                    Line(vh, center, a, edge, EDGE_WIDTH); Line(vh, center, b, edge, EDGE_WIDTH); Line(vh, a, b, edge, EDGE_WIDTH);
                }
            }
        }
    }

    /// <summary>화면 좌표가 작업 공간 직사각형 안인지와 그 위치의 실수 격자 좌표를 반환한다.</summary>
    private bool TryGridPoint(Vector2 screen, Camera eventCamera, out Vector2 grid)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, eventCamera, out Vector2 local);
        Vector2 board = (local - rectTransform.rect.center) / CELL_SIZE;
        board.y = -board.y;
        grid = BoardToGrid(board);
        return Mathf.Abs(board.x) <= HALF_WIDTH && Mathf.Abs(board.y) <= HALF_HEIGHT;
    }

    /// <summary>현재 또는 누르기 시작한 화면 좌표를 작업 공간의 실수 격자 좌표로 변환한다.</summary>
    private Vector2 GridPoint(PointerEventData data, bool usePressPosition = false)
    {
        TryGridPoint(usePressPosition ? data.pressPosition : data.position, data.pressEventCamera, out Vector2 grid);
        return grid;
    }

    /// <summary>손패 드롭이 작업 공간 안에 있는지 확인하고 스냅할 격자 원점을 반환한다.</summary>
    public bool TryDropPosition(PointerEventData data, out Vector2Int position)
    {
        bool inside = TryGridPoint(data.position, data.pressEventCamera, out Vector2 point);
        position = new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
        return inside;
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

    /// <summary>도형이나 선을 표시할 삼각형을 메시 끝에 추가한다.</summary>
    private static void Triangle(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int start = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert; vertex.color = color;
        vertex.position = a; vh.AddVert(vertex); vertex.position = b; vh.AddVert(vertex); vertex.position = c; vh.AddVert(vertex);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>격자선이나 선택한 조각의 실제 삼각 경계를 지정 두께의 선으로 표시한다.</summary>
    private static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Color color, float thickness)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * (thickness / 2);
        Triangle(vh, a + normal, b + normal, b - normal, color);
        Triangle(vh, a + normal, b - normal, a - normal, color);
    }
}

