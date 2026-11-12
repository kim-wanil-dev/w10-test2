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

    /// <summary>표시할 도안 배치, 보유 목록, 도안 색과 칸 클릭 동작을 설정한다.</summary>
    public void Setup(SpellPatternLayout layout, IReadOnlyList<int> owned, Func<int, Color> colorOf, Action<Vector2Int> onClick)
    {
        _layout = layout; _owned = owned; _colorOf = colorOf; _onClick = onClick; SetVerticesDirty();
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
            DrawPattern(vh, spell, _layout.Origin(spell), _layout.Rotation(spell), new Color(color.r, color.g, color.b, FILL_ALPHA), color);
        }
        if (_held >= 0 && _hover.x >= 0)
        {
            Vector2Int origin = HeldOrigin(_hover);
            Color color = _layout.MoveIssue(_held, origin, _heldRotation, _owned) == null ? _validColor : _invalidColor;
            DrawPattern(vh, _held, origin, _heldRotation, new Color(color.r, color.g, color.b, FILL_ALPHA), color);
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
        cell = new Vector2Int(-1, -1);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, data.position, data.pressEventCamera, out Vector2 local)) return false;
        Vector2 offset = local - rectTransform.rect.center;
        Vector2 grid = SpellPatternLayout.BoardToGrid(new Vector2(offset.x, -offset.y) / _cellSize);
        if (!SpellPatternLayout.Contains(grid)) return false;
        cell = new Vector2Int(Mathf.FloorToInt(grid.x), Mathf.FloorToInt(grid.y)); return true;
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
