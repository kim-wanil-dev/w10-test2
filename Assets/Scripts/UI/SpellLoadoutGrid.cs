using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>장착 화면의 정사각형 칸 판에 놓인 도안과 들고 있는 도안의 놓기 미리 보기를 1/4 구역 단위로 그리고 클릭을 전달한다.</summary>
public sealed class SpellLoadoutGrid : UnityEngine.UI.MaskableGraphic, IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler
{
    private const float CELL_GAP = 3f;
    private const float OUTLINE_WIDTH = 4f;
    private const float FILL_ALPHA = 0.32f;

    [Header("칸")]
    [SerializeField] private float _cellSize = 120f;

    [Header("색")]
    [SerializeField] private Color _emptyColor = new Color(0.078f, 0.102f, 0.188f);
    [SerializeField] private Color _lineColor = new Color(0.29f, 0.33f, 0.47f);
    [SerializeField] private Color _validColor = new Color(0.482f, 0.878f, 0.627f);
    [SerializeField] private Color _invalidColor = new Color(0.878f, 0.376f, 0.353f);

    private readonly Vector2[] _corners = new Vector2[4];
    private SpellPatternLayout _layout;
    private IReadOnlyList<int> _owned;
    private Func<int, Color> _colorOf;
    private Action<Vector2Int> _onClick;
    private int _held = -1;
    private int _heldRotation;
    private Vector2Int _hover = new Vector2Int(-1, -1);
    // 보상 시험 배치 중인 도안(흰 외곽선으로 구분), 격자에 시험으로 놓은 조각과 끌고 있는 조각이다. 조각은 도안과 상관없이 격자 안 어디든 놓인다.
    private int _trialSpell = -1;
    private SpellGame.Card _piece, _heldPiece;
    private Vector2Int _pieceAnchor;
    private int _pieceRotation, _heldPieceRotation;
    private Color _pieceTint = Color.white;

    /// <summary>표시할 도안 배치, 보유 목록, 도안 색과 칸 클릭 동작을 설정한다.</summary>
    public void Setup(SpellPatternLayout layout, IReadOnlyList<int> owned, Func<int, Color> colorOf, Action<Vector2Int> onClick)
    {
        _layout = layout; _owned = owned; _colorOf = colorOf; _onClick = onClick; SetVerticesDirty();
    }

    /// <summary>칸 한 변 크기를 바꾼다. 판은 영역 중심에 그린다.</summary>
    public void SetCellSize(float size) { _cellSize = size; SetVerticesDirty(); }

    /// <summary>보상 시험 배치 중인 도안을 정한다. 음수면 없다.</summary>
    public void SetTrial(int spell) { _trialSpell = spell; SetVerticesDirty(); }

    /// <summary>시험으로 놓은 조각을 정한다. card가 null이면 지운다.</summary>
    public void SetPiece(SpellGame.Card card, Vector2Int anchor, int rotation, Color tint)
    {
        _piece = card; _pieceAnchor = anchor; _pieceRotation = rotation; _pieceTint = tint; SetVerticesDirty();
    }

    /// <summary>끌고 있는 조각과 방향을 정한다. card가 null이면 들고 있지 않다.</summary>
    public void SetHeldPiece(SpellGame.Card card, int rotation, Color tint)
    {
        _heldPiece = card; _heldPieceRotation = rotation; _pieceTint = tint; SetVerticesDirty();
    }

    /// <summary>끌고 있는 조각을 가리킨 칸에 놓을 때의 앵커(조각 범위 왼쪽 위)를 반환한다.</summary>
    public Vector2Int HeldPieceAnchor(Vector2Int cell)
    {
        Vector2 size = SpellPiece3D.CellBounds(SpellGame.CardCells(_heldPiece, _heldPieceRotation));
        return cell - new Vector2Int(((int)size.x - 1) / 2, ((int)size.y - 1) / 2);
    }

    /// <summary>조각을 앵커·방향으로 놓았을 때 모든 구역이 격자 안에 들어가는지 반환한다.</summary>
    public static bool PieceFits(SpellGame.Card card, Vector2Int anchor, int rotation)
    {
        foreach (SpellGame.ShapeCell cell in SpellGame.CardCells(card, rotation))
            if ((cell.Mask & ~SpellPatternLayout.BoardMask(anchor.x + cell.X, anchor.y + cell.Y)) != 0) return false;
        return true;
    }

    /// <summary>시험으로 놓은 조각이 칸 구역을 차지하는지 반환한다.</summary>
    public bool PieceCovers(Vector2Int gridCell, int sector)
    {
        if (_piece == null) return false;
        foreach (SpellGame.ShapeCell cell in SpellGame.CardCells(_piece, _pieceRotation))
            if (_pieceAnchor.x + cell.X == gridCell.x && _pieceAnchor.y + cell.Y == gridCell.y && (cell.Mask & (1 << sector)) != 0) return true;
        return false;
    }

    /// <summary>격자 표시 영역을 지정한 화면 좌표만큼 이동한다.</summary>
    public void Pan(Vector2 offset) { rectTransform.anchoredPosition += offset; }

    /// <summary>들고 있는 도안과 방향을 설정한다. spell이 음수면 들고 있지 않다.</summary>
    public void SetHeld(int spell, int rotation) { _held = spell; _heldRotation = rotation; SetVerticesDirty(); }

    /// <summary>들고 있는 도안을 가리킨 칸에 놓을 때의 원점(회전된 범위의 왼쪽 위)을 반환한다.</summary>
    public Vector2Int HeldOrigin(Vector2Int cell)
    {
        Vector2Int size = SpellPatternLayout.RotatedSize(_held, _heldRotation);
        return cell - new Vector2Int((size.x - 1) / 2, (size.y - 1) / 2);
    }

    /// <summary>격자 칸, 놓인 도안, 들고 있는 도안의 미리 보기를 그린다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear(); if (_layout == null) return;
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            int board = SpellPatternLayout.BoardMask(x, y);
            if (board == 0) continue;
            Vector2 center = CellGeometry(x, y);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((board & (1 << sector)) == 0) continue;
                Sector(vh, center, sector, _emptyColor);
                Line(vh, center, _corners[sector], 1.2f, _lineColor); Line(vh, center, _corners[(sector + 1) % 4], 1.2f, _lineColor);
                Line(vh, _corners[sector], _corners[(sector + 1) % 4], 1.2f, _lineColor);
            }
        }
        foreach (int spell in _owned)
        {
            if (!_layout.IsPlaced(spell)) continue;
            Color color = _colorOf(spell);
            // 시험 배치 중인 보상 도안은 진하게 채우고 흰 외곽선으로 아직 확정 전임을 구분한다.
            bool trial = spell == _trialSpell;
            DrawPattern(vh, spell, _layout.Origin(spell), _layout.Rotation(spell), new Color(color.r, color.g, color.b, trial ? 0.5f : FILL_ALPHA), trial ? Color.white : color);
        }
        if (_piece != null) DrawPiece(vh, _piece, _pieceAnchor, _pieceRotation, new Color(_pieceTint.r, _pieceTint.g, _pieceTint.b, 0.9f), Color.white);
        if (_held >= 0 && _hover.x >= 0)
        {
            Vector2Int origin = HeldOrigin(_hover);
            Color color = _layout.MoveIssue(_held, origin, _heldRotation, _owned) == null ? _validColor : _invalidColor;
            DrawPattern(vh, _held, origin, _heldRotation, new Color(color.r, color.g, color.b, FILL_ALPHA), color);
        }
        if (_heldPiece != null && _hover.x >= 0)
        {
            Vector2Int anchor = HeldPieceAnchor(_hover);
            Color color = PieceFits(_heldPiece, anchor, _heldPieceRotation) ? _validColor : _invalidColor;
            DrawPiece(vh, _heldPiece, anchor, _heldPieceRotation, new Color(_pieceTint.r, _pieceTint.g, _pieceTint.b, 0.75f), color);
        }
    }

    /// <summary>조각을 앵커·방향으로 채우고 바깥 경계에 외곽선을 그린다. 격자 밖 구역은 그리지 않는다.</summary>
    private void DrawPiece(UnityEngine.UI.VertexHelper vh, SpellGame.Card card, Vector2Int anchor, int rotation, Color fill, Color line)
    {
        IReadOnlyList<SpellGame.ShapeCell> cells = SpellGame.CardCells(card, rotation);
        int MaskAt(int x, int y)
        {
            foreach (SpellGame.ShapeCell c in cells) if (anchor.x + c.X == x && anchor.y + c.Y == y) return c.Mask;
            return 0;
        }
        foreach (SpellGame.ShapeCell cell in cells)
        {
            int x = anchor.x + cell.X, y = anchor.y + cell.Y;
            int mask = cell.Mask & SpellPatternLayout.BoardMask(x, y);
            if (mask == 0) continue;
            Vector2 center = CellGeometry(x, y);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((mask & (1 << sector)) == 0) continue;
                Sector(vh, center, sector, fill);
                for (int direction = 0; direction < 3; direction++)
                {
                    SpellPatternLayout.Neighbor(x, y, sector, direction, out int nx, out int ny, out int ns);
                    if ((MaskAt(nx, ny) & (1 << ns)) != 0) continue;
                    if (direction == 0) Line(vh, center, _corners[(sector + 1) % 4], OUTLINE_WIDTH * 0.75f, line);
                    else if (direction == 1) Line(vh, center, _corners[sector], OUTLINE_WIDTH * 0.75f, line);
                    else Line(vh, _corners[sector], _corners[(sector + 1) % 4], OUTLINE_WIDTH * 0.75f, line);
                }
            }
        }
    }

    /// <summary>도안 하나를 원점·회전으로 채우고 바깥 경계에 외곽선을 그린다. 격자 밖 구역은 그리지 않는다.</summary>
    private void DrawPattern(UnityEngine.UI.VertexHelper vh, int spell, Vector2Int origin, int rotation, Color fill, Color line)
    {
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            int mask = SpellPatternLayout.GridMask(spell, origin, rotation, x, y) & SpellPatternLayout.BoardMask(x, y);
            if (mask == 0) continue;
            Vector2 center = CellGeometry(x, y);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((mask & (1 << sector)) == 0) continue;
                Sector(vh, center, sector, fill);
                for (int direction = 0; direction < 3; direction++)
                {
                    SpellPatternLayout.Neighbor(x, y, sector, direction, out int nx, out int ny, out int ns);
                    if ((SpellPatternLayout.GridMask(spell, origin, rotation, nx, ny) & (1 << ns)) != 0) continue;
                    if (direction == 0) Line(vh, center, _corners[(sector + 1) % 4], OUTLINE_WIDTH, line);
                    else if (direction == 1) Line(vh, center, _corners[sector], OUTLINE_WIDTH, line);
                    else Line(vh, _corners[sector], _corners[(sector + 1) % 4], OUTLINE_WIDTH, line);
                }
            }
        }
    }

    /// <summary>칸 (x, y)의 네 모서리를 판 위치로 _corners에 채우고 중심을 반환한다. 모서리는 칸 간격만큼 중심 쪽으로 당긴다.</summary>
    private Vector2 CellGeometry(int x, int y)
    {
        Vector2 center = GridPoint(new Vector2(x + 0.5f, y + 0.5f));
        float shrink = 1f - CELL_GAP / _cellSize;
        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = GridPoint(new Vector2(x + (i == 1 || i == 2 ? 1 : 0), y + (i >= 2 ? 1 : 0)));
            _corners[i] = center + (corner - center) * shrink;
        }
        return center;
    }

    /// <summary>격자 좌표를 영역 중심에 놓인 판의 로컬 UI 좌표로 바꾼다.</summary>
    private Vector2 GridPoint(Vector2 grid)
    {
        Vector2 board = SpellPatternLayout.GridToBoard(grid) * _cellSize;
        return rectTransform.rect.center + new Vector2(board.x, -board.y);
    }

    /// <summary>칸의 구역 삼각형 하나를 채운다.</summary>
    private void Sector(UnityEngine.UI.VertexHelper vh, Vector2 center, int sector, Color color)
    {
        int start = vh.currentVertCount; UIVertex v = UIVertex.simpleVert; v.color = color;
        v.position = center; vh.AddVert(v); v.position = _corners[sector]; vh.AddVert(v); v.position = _corners[(sector + 1) % 4]; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>두 점 사이에 두께가 있는 선을 그린다.</summary>
    private static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * thickness / 2;
        int start = vh.currentVertCount; UIVertex v = UIVertex.simpleVert; v.color = color;
        v.position = a + n; vh.AddVert(v); v.position = b + n; vh.AddVert(v); v.position = b - n; vh.AddVert(v); v.position = a - n; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>화면 좌표를 격자 칸으로 바꾼다. 격자 밖이면 false를 반환한다.</summary>
    private bool TryCell(PointerEventData data, out Vector2Int cell)
    {
        return TryPoint(data.position, data.pressEventCamera, out cell, out _);
    }

    /// <summary>화면 좌표를 격자 칸과 칸 안의 상·우·하·좌 구역으로 바꾼다. 격자 밖이면 false를 반환한다.</summary>
    private bool TryPoint(Vector2 screen, Camera eventCamera, out Vector2Int cell, out int sector)
    {
        cell = new Vector2Int(-1, -1); sector = 0;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, eventCamera, out Vector2 local)) return false;
        Vector2 offset = local - rectTransform.rect.center;
        Vector2 grid = SpellPatternLayout.BoardToGrid(new Vector2(offset.x, -offset.y) / _cellSize);
        if (!SpellPatternLayout.Contains(grid)) return false;
        cell = new Vector2Int(Mathf.FloorToInt(grid.x), Mathf.FloorToInt(grid.y));
        float dx = grid.x - cell.x - 0.5f, dy = grid.y - cell.y - 0.5f;
        sector = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        return true;
    }

    /// <summary>캔버스 렌더 방식에 맞춰 화면 좌표 변환에 쓸 카메라를 반환한다. Overlay면 null이다.</summary>
    private Camera EventCamera()
    {
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    /// <summary>드래그 중 화면 좌표로 격자 칸과 구역을 찾는다. 격자 밖이면 false를 반환한다.</summary>
    public bool TryScreenPoint(Vector2 screen, out Vector2Int cell, out int sector) { return TryPoint(screen, EventCamera(), out cell, out sector); }

    /// <summary>드래그 중 포인터 위치의 칸을 놓기 미리 보기 칸으로 정한다. 격자 밖이면 미리 보기를 숨긴다.</summary>
    public void HoverAt(Vector2 screen)
    {
        TryPoint(screen, EventCamera(), out Vector2Int cell, out _);
        if (cell == _hover) return;
        _hover = cell; SetVerticesDirty();
    }

    public void OnPointerMove(PointerEventData data)
    {
        TryCell(data, out Vector2Int cell);
        if (cell == _hover) return;
        _hover = cell; SetVerticesDirty();
    }

    public void OnPointerExit(PointerEventData data) { _hover = new Vector2Int(-1, -1); SetVerticesDirty(); }

    public void OnPointerClick(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !TryCell(data, out Vector2Int cell)) return;
        _hover = cell; _onClick?.Invoke(cell);
    }
}
