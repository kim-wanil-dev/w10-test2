using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>조각 도형이나 도안 모양을 1/4 구역 삼각형으로 영역 가운데에 맞춰 그리고, 씰링은 해당 구역 위 동그라미로 표시한다.</summary>
public sealed class SpellShapeGraphic : UnityEngine.UI.MaskableGraphic
{
    private const float GAP = 1.5f;
    private const float SEAL_RADIUS = 0.13f;
    private const float SEAL_OUTLINE = 0.035f;
    private const int SEAL_SIDES = 14;
    private static readonly Color SEAL_OUTLINE_COLOR = new Color(0.08f, 0.09f, 0.12f, 0.9f);

    private readonly List<SpellGame.ShapeCell> _cells = new List<SpellGame.ShapeCell>();
    private readonly List<SpellGame.SealSpot> _seals = new List<SpellGame.SealSpot>();
    private readonly List<Color> _sealColors = new List<Color>();
    private SpellGame.Shape _shape;
    private bool _isCursed;

    public SpellGame.Shape Shape
    {
        get { return _shape; }
        set { _shape = value; _cells.Clear(); _cells.AddRange(SpellGame.ShapeCells(value, 0)); _seals.Clear(); _sealColors.Clear(); SetVerticesDirty(); }
    }

    /// <summary>도안의 3×3 구역 마스크를 그릴 모양으로 설정한다.</summary>
    public void SetPattern(int spell)
    {
        _cells.Clear(); _seals.Clear(); _sealColors.Clear();
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
        {
            int mask = SpellGame.PatternMask(spell, x, y);
            if (mask != 0) _cells.Add(new SpellGame.ShapeCell(x, y, mask));
        }
        SetVerticesDirty();
    }

    /// <summary>판과 같은 45도 마름모 방향으로 켜진 구역 삼각형의 범위를 구해 영역 가운데에 맞춰 채운다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        if (_cells.Count == 0) return;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (SpellGame.ShapeCell c in _cells)
            for (int sector = 0; sector < 4; sector++)
            {
                if ((c.Mask & (1 << sector)) == 0) continue;
                foreach (Vector2 point in SectorPoints(c, sector)) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            }
        Rect rect = rectTransform.rect;
        Vector2 size = max - min, middle = (min + max) / 2;
        float cell = Mathf.Min(rect.width / size.x, rect.height / size.y);
        float shrink = Mathf.Max(0f, 1f - 2f * GAP / cell);
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = (_isCursed ? new Color(1f, 0.3f, 0.3f) : new Color(0.88f, 0.91f, 0.92f)) * color;
        foreach (SpellGame.ShapeCell c in _cells)
        {
            Vector2 center = SpellPatternLayout.GridToBoard(new Vector2(c.X + 0.5f, c.Y + 0.5f));
            for (int sector = 0; sector < 4; sector++)
            {
                if ((c.Mask & (1 << sector)) == 0) continue;
                Vector2[] points = SectorPoints(c, sector);
                int start = vh.currentVertCount;
                for (int i = 0; i < 3; i++)
                {
                    // 칸 간격은 칸 중심 쪽으로 꼭짓점을 당겨 만든다.
                    Vector2 board = center + (points[i] - center) * shrink - middle;
                    vertex.position = rect.center + new Vector2(board.x, -board.y) * cell; vh.AddVert(vertex);
                }
                vh.AddTriangle(start, start + 1, start + 2);
            }
        }
        for (int i = 0; i < _seals.Count; i++)
        {
            SpellGame.ShapeCell sealCell = new SpellGame.ShapeCell(_seals[i].X, _seals[i].Y, 0);
            Vector2 center = SpellPatternLayout.GridToBoard(new Vector2(sealCell.X + 0.5f, sealCell.Y + 0.5f));
            Vector2[] points = SectorPoints(sealCell, _seals[i].Sector);
            Vector2 board = center + ((points[0] + points[1] + points[2]) / 3 - center) * shrink - middle;
            Vector2 position = rect.center + new Vector2(board.x, -board.y) * cell;
            Circle(vh, position, (SEAL_RADIUS + SEAL_OUTLINE) * cell, SEAL_OUTLINE_COLOR);
            Circle(vh, position, SEAL_RADIUS * cell, _sealColors[i]);
        }
    }

    /// <summary>지정 중심과 반지름으로 채운 원을 추가한다.</summary>
    private static void Circle(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert; vertex.color = color;
        int start = vh.currentVertCount;
        vertex.position = center; vh.AddVert(vertex);
        for (int i = 0; i <= SEAL_SIDES; i++)
        {
            float angle = i * Mathf.PI * 2 / SEAL_SIDES;
            vertex.position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius; vh.AddVert(vertex);
            if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    /// <summary>카드 전체의 회전된 칸 목록과 저주 강조 상태를 표시한다. sealColor를 주면 씰링 위치에 그 색의 동그라미를 그린다.</summary>
    public void SetCard(SpellGame.Card card, int rotation = 0, bool isCursed = false, Func<SpellGame.Seal, Color> sealColor = null)
    {
        _shape = card.Shape; _isCursed = isCursed;
        _cells.Clear(); _cells.AddRange(SpellGame.CardCells(card, rotation));
        _seals.Clear(); _sealColors.Clear();
        if (sealColor != null)
            foreach (SpellGame.SealSpot seal in SpellGame.SealSpots(card, rotation)) { _seals.Add(seal); _sealColors.Add(sealColor(seal.Seal)); }
        SetVerticesDirty();
    }


    /// <summary>칸의 구역 삼각형 꼭짓점(바깥 두 모서리와 칸 중심)을 마름모 방향 좌표(x 오른쪽, y 아래)로 반환한다.</summary>
    private static Vector2[] SectorPoints(SpellGame.ShapeCell cell, int sector)
    {
        Vector2[] corners = { new Vector2(cell.X, cell.Y), new Vector2(cell.X + 1, cell.Y), new Vector2(cell.X + 1, cell.Y + 1), new Vector2(cell.X, cell.Y + 1) };
        return new[] { SpellPatternLayout.GridToBoard(corners[sector]), SpellPatternLayout.GridToBoard(corners[(sector + 1) % 4]), SpellPatternLayout.GridToBoard(new Vector2(cell.X + 0.5f, cell.Y + 0.5f)) };
    }
}

