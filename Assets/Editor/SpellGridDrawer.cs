using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

/// <summary>도안·조각 에디터가 함께 쓰는 GRID_SIZE 격자 그리기와 1/8 삼각형 구역 단위 클릭 편집이다. 칸은 위·오른쪽·아래·왼쪽 1/8 삼각형 구역 4개로 나뉜다.</summary>
public static class SpellGridDrawer
{
    public const float CELL_SIZE = 64f;
    private const float LINE_WIDTH = 1.5f;

    private static readonly Color EMPTY_COLOR = new Color(0.17f, 0.17f, 0.17f);
    private static readonly Color LINE_COLOR = new Color(0.42f, 0.42f, 0.42f);
    private static readonly Color HOVER_COLOR = new Color(1f, 1f, 1f, 0.25f);

    /// <summary>편집 가능한 GRID_SIZE 격자를 area 왼쪽 위에 그리고 입력을 처리한다. 클릭은 해당 1/8 삼각형 구역 토글, Shift+클릭은 칸 전체 토글, 우클릭은 칸 비우기다. cells를 직접 바꾸며 바뀌면 true를 반환한다.</summary>
    public static bool DrawEditable(Rect area, List<SpellCellData> cells, Color fill, bool isReadOnly)
    {
        Event current = Event.current;
        bool isChanged = false;
        if (!isReadOnly && current.type == EventType.MouseDown && TryHitSector(area, current.mousePosition, out int x, out int y, out int sectorMask))
        {
            int mask = GetMask(cells, x, y);
            if (current.button == 1) mask = 0;
            else if (current.shift) mask = mask == 0 ? SpellDataStore.FULL_MASK : 0;
            else mask ^= sectorMask;
            SetMask(cells, x, y, mask);
            isChanged = true;
            current.Use();
        }
        if (current.type != EventType.Repaint) return isChanged;
        for (int cy = 0; cy < SpellDataStore.GRID_SIZE; cy++)
            for (int cx = 0; cx < SpellDataStore.GRID_SIZE; cx++)
            {
                Rect cell = CellRect(area, cx, cy, CELL_SIZE);
                DrawSectors(cell, SpellDataStore.FULL_MASK, EMPTY_COLOR);
                DrawSectors(cell, GetMask(cells, cx, cy), fill);
                DrawCellLines(cell);
            }
        if (!isReadOnly && TryHitSector(area, current.mousePosition, out int hx, out int hy, out int hoverMask))
            DrawSectors(CellRect(area, hx, hy, CELL_SIZE), current.shift ? SpellDataStore.FULL_MASK : hoverMask, HOVER_COLOR);
        return isChanged;
    }

    /// <summary>칸 목록을 columns×rows 격자에 읽기 전용으로 그린다. 회전 미리보기 등에 쓴다.</summary>
    public static void DrawPreview(Rect area, IReadOnlyList<SpellGame.ShapeCell> cells, int columns, int rows, float cellSize, Color fill)
    {
        if (Event.current.type != EventType.Repaint) return;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                Rect cell = CellRect(area, x, y, cellSize);
                DrawSectors(cell, SpellDataStore.FULL_MASK, EMPTY_COLOR);
                DrawCellLines(cell);
            }
        foreach (SpellGame.ShapeCell cell in cells)
        {
            if (cell.X < 0 || cell.X >= columns || cell.Y < 0 || cell.Y >= rows) continue;
            Rect rect = CellRect(area, cell.X, cell.Y, cellSize);
            DrawSectors(rect, cell.Mask, fill);
            DrawCellLines(rect);
        }
    }

    /// <summary>칸 사각형 안에서 mask에 켜진 삼각 구역을 color로 칠한다. Repaint 이벤트에서만 호출한다.</summary>
    public static void DrawSectors(Rect cell, int mask, Color color)
    {
        if (mask == 0) return;
        Color previous = Handles.color;
        Handles.color = color;
        Vector3 center = cell.center;
        Vector3 topLeft = new Vector3(cell.xMin, cell.yMin), topRight = new Vector3(cell.xMax, cell.yMin);
        Vector3 bottomRight = new Vector3(cell.xMax, cell.yMax), bottomLeft = new Vector3(cell.xMin, cell.yMax);
        if ((mask & 1) != 0) Handles.DrawAAConvexPolygon(topLeft, topRight, center);
        if ((mask & 2) != 0) Handles.DrawAAConvexPolygon(topRight, bottomRight, center);
        if ((mask & 4) != 0) Handles.DrawAAConvexPolygon(bottomRight, bottomLeft, center);
        if ((mask & 8) != 0) Handles.DrawAAConvexPolygon(bottomLeft, topLeft, center);
        Handles.color = previous;
    }

    /// <summary>칸 테두리와 구역 경계인 두 대각선을 그린다. Repaint 이벤트에서만 호출한다.</summary>
    public static void DrawCellLines(Rect cell)
    {
        Color previous = Handles.color;
        Handles.color = LINE_COLOR;
        Vector3 topLeft = new Vector3(cell.xMin, cell.yMin), topRight = new Vector3(cell.xMax, cell.yMin);
        Vector3 bottomRight = new Vector3(cell.xMax, cell.yMax), bottomLeft = new Vector3(cell.xMin, cell.yMax);
        Handles.DrawAAPolyLine(LINE_WIDTH, topLeft, topRight, bottomRight, bottomLeft, topLeft);
        Handles.DrawAAPolyLine(LINE_WIDTH, topLeft, bottomRight);
        Handles.DrawAAPolyLine(LINE_WIDTH, topRight, bottomLeft);
        Handles.color = previous;
    }

    /// <summary>격자 왼쪽 위 area에서 칸 (x, y)의 사각형을 반환한다.</summary>
    public static Rect CellRect(Rect area, int x, int y, float cellSize)
    {
        return new Rect(area.x + x * cellSize, area.y + y * cellSize, cellSize, cellSize);
    }

    /// <summary>칸 목록에서 (x, y) 칸의 구역 마스크를 반환한다. 없으면 0이다.</summary>
    public static int GetMask(List<SpellCellData> cells, int x, int y)
    {
        foreach (SpellCellData cell in cells) if (cell.X == x && cell.Y == y) return cell.Mask;
        return 0;
    }

    /// <summary>(x, y) 칸의 마스크를 설정한다. 0이면 항목을 지우고, 없던 칸이면 새로 추가한다.</summary>
    public static void SetMask(List<SpellCellData> cells, int x, int y, int mask)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].X != x || cells[i].Y != y) continue;
            if (mask == 0) cells.RemoveAt(i);
            else cells[i].Mask = mask;
            return;
        }
        if (mask != 0) cells.Add(new SpellCellData { X = x, Y = y, Mask = mask });
    }

    /// <summary>마우스 위치가 GRID_SIZE 격자의 어느 칸인지 구하고, 칸 안에서 가리킨 1/8 삼각형 구역 마스크를 sectorMask로 반환한다.</summary>
    private static bool TryHitSector(Rect area, Vector2 point, out int x, out int y, out int sectorMask)
    {
        Vector2 local = point - area.position;
        x = Mathf.FloorToInt(local.x / CELL_SIZE); y = Mathf.FloorToInt(local.y / CELL_SIZE); sectorMask = 0;
        if (local.x < 0 || local.y < 0 || x >= SpellDataStore.GRID_SIZE || y >= SpellDataStore.GRID_SIZE) return false;
        float dx = local.x - (x + 0.5f) * CELL_SIZE, dy = local.y - (y + 0.5f) * CELL_SIZE;
        int sector = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        sectorMask = 1 << sector;
        return true;
    }
}
