using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>씰 작업대의 조각과 영역별 부착 가능 상태, 드래그 위치를 같은 좌표계로 표시한다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpellSealWorkbenchGraphic : MaskableGraphic
{
    private const float MARGIN = 24f;
    private const float GAP = 2f;
    private const int CIRCLE_SIDES = 24;
    private static readonly Color VALID = new Color(0.25f, 0.9f, 0.5f);
    private static readonly Color INVALID = new Color(1f, 0.25f, 0.3f);

    private SpellGame.Card _card;
    private IReadOnlyList<SpellGame.RewardSealTarget> _targets;
    private Func<SpellGame.Seal, Color> _sealColor;
    private Color _pieceColor;
    private bool _isDragging;
    private int _hoverX = -1;
    private int _hoverY = -1;
    private int _hoverSector = -1;

    /// <summary>덱 조각과 빈 영역 목록, 기존 씰 색을 받아 작업대 표시를 초기화한다.</summary>
    public void Setup(SpellGame.Card card, IReadOnlyList<SpellGame.RewardSealTarget> targets, Color pieceColor, Func<SpellGame.Seal, Color> sealColor)
    {
        _card = card; _targets = targets; _pieceColor = pieceColor; _sealColor = sealColor;
        SetVerticesDirty();
    }

    /// <summary>조각 대신 드래그할 씰 원형 아이콘을 지정 색으로 표시한다.</summary>
    public void SetupToken(Color tokenColor)
    {
        _card = null; color = tokenColor; SetVerticesDirty();
    }

    /// <summary>포인터의 1/4 영역을 강조하고 부착 대상 인덱스를 반환한다. 부착 불가 위치는 -1이다.</summary>
    public int Preview(Vector2 screen, Camera eventCamera, bool isDragging)
    {
        _isDragging = isDragging; _hoverX = _hoverY = _hoverSector = -1;
        if (_card != null && isDragging)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, eventCamera, out Vector2 local);
            Vector2 grid = (local - rectTransform.rect.center) / CellSize();
            grid = new Vector2(grid.x, -grid.y) + Bounds() / 2;
            if (grid.x >= 0 && grid.y >= 0 && grid.x < Bounds().x && grid.y < Bounds().y)
            {
                _hoverX = Mathf.FloorToInt(grid.x); _hoverY = Mathf.FloorToInt(grid.y);
                Vector2 delta = grid - new Vector2(_hoverX + 0.5f, _hoverY + 0.5f);
                _hoverSector = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? (delta.x > 0 ? 1 : 3) : (delta.y > 0 ? 2 : 0);
            }
        }
        SetVerticesDirty();
        return TargetAt(_hoverX, _hoverY, _hoverSector);
    }

    /// <summary>표시 칸 좌표와 구역 번호로 부착 가능한 대상 인덱스를 찾는다.</summary>
    private int TargetAt(int x, int y, int sector)
    {
        if (_targets == null) return -1;
        for (int i = 0; i < _targets.Count; i++)
        {
            SpellGame.SealSpot spot = _targets[i].DisplaySpot;
            if (spot.X == x && spot.Y == y && spot.Sector == sector) return i;
        }
        return -1;
    }

    /// <summary>선택 조각의 전체 칸 범위를 반환한다.</summary>
    private Vector2 Bounds() { return new Vector2(SpellGame.ShapeWidth(_card, 0), SpellGame.ShapeHeight(_card, 0)); }

    /// <summary>조각 전체가 작업대 여백 안에 들어가는 칸 크기를 반환한다.</summary>
    private float CellSize() { Vector2 bounds = Bounds(); return Mathf.Min((rectTransform.rect.width - MARGIN * 2) / bounds.x, (rectTransform.rect.height - MARGIN * 2) / bounds.y); }

    /// <summary>조각 칸 좌표를 작업대 중심 기준 로컬 픽셀 좌표로 변환한다.</summary>
    private Vector2 Local(Vector2 grid) { Vector2 delta = grid - Bounds() / 2; return rectTransform.rect.center + new Vector2(delta.x, -delta.y) * CellSize(); }

    /// <summary>조각의 유효·무효 영역과 현재 후보 강조, 기존 씰을 표시한다. 토큰 모드에서는 원 하나를 그린다.</summary>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_card == null) { Circle(vh, rectTransform.rect.center, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.45f, color); return; }
        Vector2 bounds = Bounds();
        for (int y = 0; y < bounds.y; y++) for (int x = 0; x < bounds.x; x++) for (int sector = 0; sector < 4; sector++)
        {
            bool occupied = (SpellGame.CellMask(_card, 0, x, y) & (1 << sector)) != 0;
            bool valid = TargetAt(x, y, sector) >= 0;
            bool hover = _isDragging && x == _hoverX && y == _hoverY && sector == _hoverSector;
            Color tint = occupied ? Color.Lerp(_pieceColor, valid ? VALID : INVALID, _isDragging ? 0.5f : 0.25f) : new Color(0.22f, 0.08f, 0.1f);
            if (hover) tint = valid ? VALID : INVALID;
            Vector2 center = Local(new Vector2(x + 0.5f, y + 0.5f));
            Vector2 a = Local(Corner(x, y, sector)), b = Local(Corner(x, y, (sector + 1) % 4));
            Vector2 centroid = (a + b + center) / 3;
            float shrink = Mathf.Max(0, 1f - GAP / CellSize());
            Triangle(vh, centroid + (a - centroid) * shrink, centroid + (b - centroid) * shrink, centroid + (center - centroid) * shrink, tint);
        }
        foreach (SpellGame.SealSpot spot in SpellGame.SealSpots(_card, 0))
        {
            Vector2 centroid = (Corner(spot.X, spot.Y, spot.Sector) + Corner(spot.X, spot.Y, (spot.Sector + 1) % 4) + new Vector2(spot.X + 0.5f, spot.Y + 0.5f)) / 3;
            Circle(vh, Local(centroid), CellSize() * 0.13f, _sealColor(spot.Seal));
        }
    }

    /// <summary>칸의 네 모서리 중 지정 번호의 좌표를 반환한다.</summary>
    private static Vector2 Corner(int x, int y, int corner) { return new Vector2(x + (corner == 1 || corner == 2 ? 1 : 0), y + (corner >= 2 ? 1 : 0)); }

    /// <summary>지정 꼭짓점과 색의 삼각형을 UI 메시로 추가한다.</summary>
    private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>지정 위치에 씰을 나타내는 원형 메시를 추가한다.</summary>
    private static void Circle(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        for (int i = 0; i < CIRCLE_SIDES; i++)
        {
            float a = i * Mathf.PI * 2 / CIRCLE_SIDES, b = (i + 1) * Mathf.PI * 2 / CIRCLE_SIDES;
            Triangle(vh, center, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, tint);
        }
    }
}
