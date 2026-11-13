using System.Collections.Generic;

using UnityEngine;

/// <summary>공유 격자 위 도안의 위치·회전과 1/4 구역 점유를 관리하고, 격자 좌표와 도안 기준 좌표를 서로 변환한다.</summary>
/// <remarks>격자는 화면에 똑바로 놓인 정사각형 칸이고, 두 대각선이 칸을 위·오른쪽·아래·왼쪽 1/4 구역으로 나눈다. 격자 +x는 화면 오른쪽, +y는 아래를 향한다.</remarks>
public sealed class SpellPatternLayout
{
    public const int COLUMNS = 4;
    public const int ROWS = 4;
    public static Vector2Int PieceAnchor(Vector2 pointer, Vector2 pivot, Vector2 offset)
    {
        Vector2 origin = pointer + offset - pivot;
        return new Vector2Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.y));
    }

    public static Vector2 RotatePiecePivot(Vector2 pivot, float previousHeight)
    {
        return new Vector2(previousHeight - pivot.y, pivot.x);
    }
    // 화면 기준 판 한 변의 길이를 격자 칸 한 변 단위로 나타낸 값이다.
    public const float BOARD_SIZE = COLUMNS;
    private const int LOCAL_SIZE = SpellGame.LOCAL_SIZE;
    private const int FULL_MASK = 15;

    private static readonly Vector2 BOARD_CENTER = new Vector2(COLUMNS / 2f, ROWS / 2f);
    private static readonly Vector2Int[] SIDE_OFFSETS = { new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0) };

    private readonly Vector2Int[] _origins;
    private readonly int[] _rotations;
    private readonly bool[] _placed;

    /// <summary>도안 수만큼 배치 상태를 만들고 모든 도안을 기본 위치·회전의 미배치 상태로 만든다.</summary>
    public SpellPatternLayout()
    {
        _origins = new Vector2Int[SpellGame.PatternCount];
        _rotations = new int[SpellGame.PatternCount];
        _placed = new bool[SpellGame.PatternCount];
        Reset();
    }

    /// <summary>모든 도안을 도안 데이터의 기본 위치·회전, 미배치 상태로 되돌린다.</summary>
    public void Reset()
    {
        for (int i = 0; i < _origins.Length; i++)
        {
            SpellGame.PatternLayout(i, out int x, out int y, out int rotation);
            _origins[i] = new Vector2Int(x, y); _rotations[i] = rotation; _placed[i] = false;
        }
    }

    /// <summary>격자 칸 (x, y)에서 판에 속한 구역 마스크를 반환한다. 판 범위 밖이면 0이다.</summary>
    public static int BoardMask(int x, int y)
    {
        return x < 0 || x >= COLUMNS || y < 0 || y >= ROWS ? 0 : FULL_MASK;
    }

    /// <summary>소수 격자 위치가 판에 속한 구역 위에 있는지 반환한다.</summary>
    public static bool Contains(Vector2 grid)
    {
        int x = Mathf.FloorToInt(grid.x), y = Mathf.FloorToInt(grid.y);
        float dx = grid.x - x - 0.5f, dy = grid.y - y - 0.5f;
        int sector = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        return (BoardMask(x, y) & (1 << sector)) != 0;
    }

    /// <summary>격자 좌표를 판 중심 기준 화면 방향 좌표(x 오른쪽, y 아래, 칸 한 변 단위)로 바꾼다.</summary>
    public static Vector2 GridToBoard(Vector2 grid)
    {
        return grid - BOARD_CENTER;
    }

    /// <summary>판 중심 기준 화면 방향 좌표(x 오른쪽, y 아래, 칸 한 변 단위)를 격자 좌표로 바꾼다.</summary>
    public static Vector2 BoardToGrid(Vector2 board)
    {
        return BOARD_CENTER + board;
    }

    /// <summary>도안이 격자에 놓여 있는지 반환한다.</summary>
    public bool IsPlaced(int spell) { return _placed[spell]; }

    /// <summary>회전된 도안 범위의 좌상단 격자 좌표를 반환한다.</summary>
    public Vector2Int Origin(int spell) { return _origins[spell]; }

    /// <summary>도안의 시계 방향 90도 회전 횟수(0~3)를 반환한다.</summary>
    public int Rotation(int spell) { return _rotations[spell]; }

    /// <summary>회전하지 않은 도안이 차지하는 칸 범위의 가로·세로 크기를 반환한다.</summary>
    public static Vector2Int BaseSize(int spell)
    {
        int width = 0, height = 0;
        for (int y = 0; y < LOCAL_SIZE; y++) for (int x = 0; x < LOCAL_SIZE; x++)
            if (SpellGame.PatternMask(spell, x, y) != 0) { width = Mathf.Max(width, x + 1); height = Mathf.Max(height, y + 1); }
        return new Vector2Int(width, height);
    }

    /// <summary>지정 회전을 적용한 도안 범위의 가로·세로 크기를 반환한다.</summary>
    public static Vector2Int RotatedSize(int spell, int rotation)
    {
        Vector2Int size = BaseSize(spell);
        return rotation % 2 == 0 ? size : new Vector2Int(size.y, size.x);
    }

    /// <summary>크기 size인 범위 안의 칸을 시계 방향으로 rotation번 돌린 좌표를 반환한다.</summary>
    private static Vector2Int RotateCell(Vector2Int cell, Vector2Int size, int rotation)
    {
        for (int i = 0; i < Wrap(rotation); i++) { cell = new Vector2Int(size.y - 1 - cell.y, cell.x); size = new Vector2Int(size.y, size.x); }
        return cell;
    }

    /// <summary>회전된 도안 범위 기준 칸을 회전하지 않은 도안 기준 칸으로 되돌린다.</summary>
    private static Vector2Int UnrotateCell(int spell, int rotation, Vector2Int rotated)
    {
        return RotateCell(rotated, RotatedSize(spell, rotation), 4 - Wrap(rotation));
    }

    /// <summary>회전하지 않은 도안 기준 칸을 지정 회전을 적용한 도안 범위 기준 칸으로 바꾼다.</summary>
    public static Vector2Int RotateLocal(int spell, int rotation, Vector2Int local)
    {
        return RotateCell(local, BaseSize(spell), rotation);
    }

    /// <summary>지정 회전을 적용한 도안 범위 기준 칸을 회전하지 않은 도안 기준 칸으로 바꾼다.</summary>
    public static Vector2Int UnrotateLocal(int spell, int rotation, Vector2Int rotated)
    {
        return UnrotateCell(spell, rotation, rotated);
    }

    /// <summary>위·오른쪽·아래·왼쪽 구역 비트를 시계 방향으로 rotation번 돌린 마스크를 반환한다.</summary>
    private static int RotateMask(int mask, int rotation)
    {
        int r = Wrap(rotation);
        return ((mask << r) | (mask >> (4 - r))) & FULL_MASK;
    }

    /// <summary>회전 값을 0~3 범위로 맞춘다.</summary>
    private static int Wrap(int rotation) { return (rotation % 4 + 4) % 4; }

    /// <summary>도안을 origin과 rotation으로 놓았을 때 격자 칸 (x, y)에서 차지하는 구역 마스크를 반환한다.</summary>
    public static int GridMask(int spell, Vector2Int origin, int rotation, int x, int y)
    {
        Vector2Int rotated = new Vector2Int(x, y) - origin, size = RotatedSize(spell, rotation);
        if (rotated.x < 0 || rotated.y < 0 || rotated.x >= size.x || rotated.y >= size.y) return 0;
        Vector2Int local = UnrotateCell(spell, rotation, rotated);
        return RotateMask(SpellGame.PatternMask(spell, local.x, local.y), rotation);
    }

    /// <summary>현재 배치된 도안이 격자 칸 (x, y)에서 차지하는 구역 마스크를 반환한다.</summary>
    public int PlacedMask(int spell, int x, int y)
    {
        return _placed[spell] ? GridMask(spell, _origins[spell], _rotations[spell], x, y) : 0;
    }

    /// <summary>도안을 지정 위치·회전으로 옮길 수 없는 이유를 반환하고, 가능하면 null을 반환한다.</summary>
    public string MoveIssue(int spell, Vector2Int origin, int rotation, IReadOnlyList<int> owned)
    {
        Vector2Int size = RotatedSize(spell, rotation);
        for (int y = 0; y < size.y; y++) for (int x = 0; x < size.x; x++)
        {
            int gx = origin.x + x, gy = origin.y + y;
            int mask = GridMask(spell, origin, rotation, gx, gy);
            if (mask == 0) continue;
            if ((mask & ~BoardMask(gx, gy)) != 0) return "도안이 격자 밖으로 나갑니다";
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] != spell && (PlacedMask(owned[i], gx, gy) & mask) != 0) return "다른 도안과 겹칩니다";
        }
        return null;
    }

    /// <summary>도안을 지정 위치·회전으로 확정한다. 호출 전에 MoveIssue로 가능 여부를 확인한다.</summary>
    public void Place(int spell, Vector2Int origin, int rotation)
    {
        _origins[spell] = origin; _rotations[spell] = Wrap(rotation); _placed[spell] = true;
    }

    /// <summary>도안을 격자에서 내려 보관 상태로 만든다.</summary>
    public void Unplace(int spell) { _placed[spell] = false; }

    /// <summary>도안 하나를 기본 위치에, 막히면 비어 있는 첫 위치·회전에 놓는다. 자리가 없으면 false를 반환한다.</summary>
    public bool TryAutoPlace(int spell, IReadOnlyList<int> owned)
    {
        if (_placed[spell]) return true;
        if (MoveIssue(spell, _origins[spell], _rotations[spell], owned) == null) { Place(spell, _origins[spell], _rotations[spell]); return true; }
        for (int rotation = 0; rotation < 4; rotation++)
            for (int y = 0; y < ROWS; y++)
                for (int x = 0; x < COLUMNS; x++)
                    if (MoveIssue(spell, new Vector2Int(x, y), rotation, owned) == null) { Place(spell, new Vector2Int(x, y), rotation); return true; }
        return false;
    }

    /// <summary>아직 놓이지 않은 보유 도안을 기본 위치에, 막히면 비어 있는 첫 위치·회전에 놓는다.</summary>
    public void EnsurePlaced(IReadOnlyList<int> owned)
    {
        for (int i = 0; i < owned.Count; i++)
        {
            int spell = owned[i];
            if (_placed[spell]) continue;
            if (MoveIssue(spell, _origins[spell], _rotations[spell], owned) == null) { Place(spell, _origins[spell], _rotations[spell]); continue; }
            for (int rotation = 0; rotation < 4 && !_placed[spell]; rotation++)
                for (int y = 0; y < ROWS && !_placed[spell]; y++)
                    for (int x = 0; x < COLUMNS && !_placed[spell]; x++)
                        if (MoveIssue(spell, new Vector2Int(x, y), rotation, owned) == null) Place(spell, new Vector2Int(x, y), rotation);
        }
    }

    /// <summary>격자 칸의 한 구역을 차지한 보유 도안을 찾는다. 없으면 false를 반환한다.</summary>
    public bool TryFindOwner(int x, int y, int sector, IReadOnlyList<int> owned, out int spell)
    {
        for (int i = 0; i < owned.Count; i++)
            if ((PlacedMask(owned[i], x, y) & (1 << sector)) != 0) { spell = owned[i]; return true; }
        spell = -1; return false;
    }

    /// <summary>조각을 놓을 대상 도안을 찾는다. 가리킨 구역의 주인을 우선하고, 없으면 그 칸의 구역을 가진 도안을 고른다.</summary>
    public bool TryFindTarget(int x, int y, int sector, IReadOnlyList<int> owned, out int spell)
    {
        if (TryFindOwner(x, y, sector, owned, out spell)) return true;
        for (int i = 0; i < owned.Count; i++)
            if (PlacedMask(owned[i], x, y) != 0) { spell = owned[i]; return true; }
        spell = -1; return false;
    }

    /// <summary>격자 칸과 구역을 도안 기준 칸과 구역으로 바꾼다. 도안 범위 밖이어도 변환 결과를 돌려준다.</summary>
    public void GridToLocal(int spell, int x, int y, int sector, out Vector2Int local, out int localSector)
    {
        local = UnrotateCell(spell, _rotations[spell], new Vector2Int(x, y) - _origins[spell]);
        localSector = Wrap(sector - _rotations[spell]);
    }

    /// <summary>도안 기준 칸을 현재 배치의 격자 칸으로 바꾼다.</summary>
    public Vector2Int LocalToGrid(int spell, Vector2Int local)
    {
        return _origins[spell] + RotateCell(local, BaseSize(spell), _rotations[spell]);
    }

    /// <summary>화면에서 보이는 조각 회전을 도안 기준 회전으로 바꾼다.</summary>
    public int ToLocalRotation(int spell, int gridRotation) { return Wrap(gridRotation - _rotations[spell]); }

    /// <summary>격자 기준 앵커 칸(조각 범위의 왼쪽 위)과 방향을 SpellGame이 쓰는 도안 기준 앵커와 방향으로 바꾼다.</summary>
    public void GridToLocalPlacement(int spell, SpellGame.Shape shape, Vector2Int anchor, int gridRotation, out Vector2Int local, out int localRotation)
    {
        int patternRotation = _rotations[spell];
        if (shape == SpellGame.Shape.Clay)
        {
            local = UnrotateCell(spell, patternRotation, anchor - _origins[spell]); localRotation = 0; return;
        }
        List<SpellGame.ShapeCell> cells = new List<SpellGame.ShapeCell>();
        foreach (SpellGame.ShapeCell cell in SpellGame.ShapeCells(shape, gridRotation))
        {
            Vector2Int l = UnrotateCell(spell, patternRotation, anchor + new Vector2Int(cell.X, cell.Y) - _origins[spell]);
            cells.Add(new SpellGame.ShapeCell(l.x, l.y, RotateMask(cell.Mask, -patternRotation)));
        }
        local = Normalize(cells);
        localRotation = MatchOrientation(shape, cells, Wrap(gridRotation - patternRotation));
    }

    /// <summary>배치된 조각의 격자 기준 앵커 칸(범위 왼쪽 위)과 화면 방향을 반환한다.</summary>
    public void PieceToGrid(int spell, SpellGame.Placement piece, out Vector2Int anchor, out int gridRotation)
    {
        List<SpellGame.ShapeCell> cells = PlacementGridCells(spell, piece);
        anchor = Normalize(cells);
        gridRotation = piece.Card.Shape == SpellGame.Shape.Clay ? 0 : MatchOrientation(piece.Card, cells, Wrap(piece.Rotation + _rotations[spell]));
    }

    /// <summary>배치된 조각이 격자에서 차지하는 칸과 구역 마스크를 격자 좌표로 반환한다.</summary>
    public List<SpellGame.ShapeCell> PlacementGridCells(int spell, SpellGame.Placement piece)
    {
        return LocalMaskGridCells(spell, piece.Mask);
    }

    /// <summary>도안 기준 칸별 마스크를 격자 칸 목록으로 바꾸고, 범위 왼쪽 위가 (0, 0)이 되게 옮긴 뒤 원래 왼쪽 위 격자 좌표를 anchor로 반환한다.</summary>
    public List<SpellGame.ShapeCell> MaskGridCells(int spell, int[] localMask, out Vector2Int anchor)
    {
        List<SpellGame.ShapeCell> cells = LocalMaskGridCells(spell, localMask);
        anchor = Normalize(cells);
        return cells;
    }

    /// <summary>도안 기준 칸별 마스크(LOCAL_SIZE×LOCAL_SIZE)를 격자 좌표 칸 목록으로 바꾼다.</summary>
    private List<SpellGame.ShapeCell> LocalMaskGridCells(int spell, int[] localMask)
    {
        List<SpellGame.ShapeCell> cells = new List<SpellGame.ShapeCell>();
        for (int y = 0; y < LOCAL_SIZE; y++) for (int x = 0; x < LOCAL_SIZE; x++)
        {
            int mask = localMask[y * LOCAL_SIZE + x];
            if (mask == 0) continue;
            Vector2Int grid = LocalToGrid(spell, new Vector2Int(x, y));
            cells.Add(new SpellGame.ShapeCell(grid.x, grid.y, RotateMask(mask, _rotations[spell])));
        }
        return cells;
    }

    /// <summary>배치된 조각의 씰링 위치를 PlacementGridCells와 같은 격자 좌표로 반환한다.</summary>
    public List<SpellGame.SealSpot> PlacementGridSeals(int spell, SpellGame.Placement piece)
    {
        List<SpellGame.SealSpot> seals = new List<SpellGame.SealSpot>();
        foreach (SpellGame.SealSpot seal in SpellGame.SealSpots(piece.Card, piece.Rotation))
        {
            Vector2Int grid = LocalToGrid(spell, new Vector2Int(piece.X + seal.X, piece.Y + seal.Y));
            seals.Add(new SpellGame.SealSpot(grid.x, grid.y, Wrap(seal.Sector + _rotations[spell]), seal.Seal));
        }
        return seals;
    }

    /// <summary>칸 목록을 범위 왼쪽 위가 (0, 0)이 되게 옮기고 원래 왼쪽 위 좌표를 반환한다.</summary>
    private static Vector2Int Normalize(List<SpellGame.ShapeCell> cells)
    {
        Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
        foreach (SpellGame.ShapeCell cell in cells) min = Vector2Int.Min(min, new Vector2Int(cell.X, cell.Y));
        for (int i = 0; i < cells.Count; i++) cells[i] = new SpellGame.ShapeCell(cells[i].X - min.x, cells[i].Y - min.y, cells[i].Mask);
        return min;
    }

    /// <summary>정규화된 칸 목록과 같은 모양인 도형 방향을 찾는다. 대칭 도형에서도 씰링 위치가 유지되도록 fallback을 먼저 확인하고, 없으면 fallback을 반환한다.</summary>
    private static int MatchOrientation(SpellGame.Shape shape, List<SpellGame.ShapeCell> cells, int fallback)
    {
        for (int i = 0; i < 4; i++)
        {
            int rotation = (fallback + i) % 4;
            IReadOnlyList<SpellGame.ShapeCell> candidate = SpellGame.ShapeCells(shape, rotation);
            if (candidate.Count != cells.Count) continue;
            bool same = true;
            foreach (SpellGame.ShapeCell cell in cells)
            {
                bool found = false;
                foreach (SpellGame.ShapeCell other in candidate) if (other.X == cell.X && other.Y == cell.Y && other.Mask == cell.Mask) found = true;
                if (!found) { same = false; break; }
            }
            if (same) return rotation;
        }
        return fallback;
    }

    /// <summary>격자 구역의 이웃 구역(같은 칸의 양옆 구역 또는 옆 칸의 맞닿은 구역)을 반환한다.</summary>
    public static void Neighbor(int x, int y, int sector, int direction, out int nx, out int ny, out int nSector)
    {
        // direction 0은 같은 칸의 다음 구역, 1은 이전 구역, 2는 바깥쪽 옆 칸의 맞닿은 구역이다.
        if (direction == 0) { nx = x; ny = y; nSector = (sector + 1) % 4; return; }
        if (direction == 1) { nx = x; ny = y; nSector = (sector + 3) % 4; return; }
        nx = x + SIDE_OFFSETS[sector].x; ny = y + SIDE_OFFSETS[sector].y; nSector = (sector + 2) % 4;
    }
    /// <summary>복합 카드를 포함한 격자 앵커·회전을 도안 기준으로 변환한다.</summary>
    public void GridToLocalPlacement(int spell, SpellGame.Card card, Vector2Int anchor, int gridRotation, out Vector2Int local, out int localRotation)
    {
        int patternRotation = _rotations[spell];
        if (card.Glued == null && card.Shape == SpellGame.Shape.Clay)
        {
            local = UnrotateCell(spell, patternRotation, anchor - _origins[spell]); localRotation = 0; return;
        }
        List<SpellGame.ShapeCell> cells = new List<SpellGame.ShapeCell>();
        foreach (SpellGame.ShapeCell cell in SpellGame.CardCells(card, gridRotation))
        {
            Vector2Int l = UnrotateCell(spell, patternRotation, anchor + new Vector2Int(cell.X, cell.Y) - _origins[spell]);
            cells.Add(new SpellGame.ShapeCell(l.x, l.y, RotateMask(cell.Mask, -patternRotation)));
        }
        local = Normalize(cells);
        localRotation = MatchOrientation(card, cells, Wrap(gridRotation - patternRotation));
    }
    /// <summary>카드의 전체 점유 칸과 일치하는 회전을 반환한다. 대칭 도형에서도 씰링 위치가 유지되도록 회전 합으로 구한 fallback을 먼저 확인한다.</summary>
    private static int MatchOrientation(SpellGame.Card card, List<SpellGame.ShapeCell> cells, int fallback)
    {
        for (int i = 0; i < 4; i++)
        {
            int rotation = (fallback + i) % 4;
            IReadOnlyList<SpellGame.ShapeCell> candidate = SpellGame.CardCells(card, rotation);
            if (candidate.Count != cells.Count) continue;
            bool same = true;
            foreach (SpellGame.ShapeCell cell in cells)
            {
                bool found = false;
                foreach (SpellGame.ShapeCell other in candidate) if (other.X == cell.X && other.Y == cell.Y && other.Mask == cell.Mask) found = true;
                if (!found) { same = false; break; }
            }
            if (same) return rotation;
        }
        return fallback;
    }
}

