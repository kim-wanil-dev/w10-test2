using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>공유 격자와 도안 외곽선, 조각 및 배치·도안 이동 미리 보기를 1/4 구역 단위로 그리고 입력을 전달한다.</summary>
public sealed class SpellBoardGraphic : UnityEngine.UI.MaskableGraphic, IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private const float CELL_GAP = 3f;
    private const float OUTLINE_WIDTH = 5f;
    private const float PREVIEW_INSET = 5f;
    private const float PREVIEW_WIDTH = 3f;

    [Header("칸")]
    [SerializeField] private float _cellSize = 120f;

    [Header("격자 색")]
    [SerializeField] private Color _emptyColor = new Color(0.078f, 0.102f, 0.188f);
    [SerializeField] private Color _requiredColor = new Color(0.365f, 0.424f, 0.596f);
    [SerializeField] private Color _targetColor = new Color(0.306f, 0.541f, 0.494f);
    [SerializeField] private Color _validColor = new Color(0.482f, 0.878f, 0.627f);
    [SerializeField] private Color _invalidColor = new Color(0.878f, 0.376f, 0.353f);
    [SerializeField] private Color _guideColor = new Color(0.165f, 0.2f, 0.337f, 0.9f);

    [Header("조각 색")]
    [SerializeField] private Color _normalPieceColor = new Color(0.725f, 0.769f, 0.808f);

    private bool[] _targets = new bool[0];
    private readonly Vector2[] _corners = new Vector2[4];
    private SpellGameUI _ui;
    private SpellGame _game;
    private SpellPatternLayout _layout;

    private int _hoverX = -1;
    private int _hoverY = -1;
    private int _hoverSector;

    private bool _dragPiece;
    private Vector2Int _pieceOffset;

    private int _dragPattern = -1;
    private int _dragRotation;
    private Vector2Int _grabLocal;
    private bool _hasPatternPreview;
    private Vector2Int _previewOrigin;
    private Vector2 _ghostTopLeft;
    private PointerEventData _lastPatternData;

    private bool _targetsDirty = true;
    private SpellGame.Card _targetsCard;
    private int _targetsRotation = -1;

    public bool IsDragging { get { return _dragPiece || IsDraggingPattern; } }
    public bool IsDraggingPattern { get { return _dragPattern >= 0; } }
    public float CellSize { get { return _cellSize; } }
    public int PreviewPatternIndex { get { return _hasPatternPreview ? _dragPattern : -1; } }
    public Vector2Int PreviewOrigin { get { return _previewOrigin; } }
    public int PreviewRotation { get { return _dragRotation; } }

    /// <summary>표시할 게임 상태와 도안 배치를 설정한다.</summary>
    public void Setup(SpellGameUI ui, SpellGame game, SpellPatternLayout layout)
    {
        _ui = ui; _game = game; _layout = layout; _targetsDirty = true; SetVerticesDirty();
    }

    /// <summary>도형이 지정 칸에서 차지하는 삼각 구역 마스크를 반환한다. 좌표와 회전은 같은 기준(격자 또는 도안)을 쓴다.</summary>
    private static int CellMask(SpellGame.Card card, int x, int y, int rotation, int cellX, int cellY)
    {
        return SpellGame.ShapeMaskAt(card.Shape, x, y, rotation, cellX, cellY);
    }

    /// <summary>배치 조각의 코팅·씰링·이전 효과에 맞는 표시 색을 반환한다. 아무것도 없으면 일반 조각 색이다.</summary>
    private Color PieceColor(SpellGame.Placement piece)
    {
        return SpellGameUI.HasTraits(piece.Card) && !piece.Card.IsUnique ? _ui.AccentColor(piece.Card) : _normalPieceColor;
    }

    /// <summary>선분에 두께를 주어 격자 경계 또는 도형 경계를 그린다.</summary>
    private static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * thickness / 2;
        int start = vh.currentVertCount; UIVertex v = UIVertex.simpleVert; v.color = color;
        v.position = a + n; vh.AddVert(v); v.position = b + n; vh.AddVert(v); v.position = b - n; vh.AddVert(v); v.position = a - n; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>삼각 구역 하나를 채운다.</summary>
    private static void Sector(UnityEngine.UI.VertexHelper vh, Vector2 center, Vector2 a, Vector2 b, Color color)
    {
        int start = vh.currentVertCount; UIVertex v = UIVertex.simpleVert; v.color = color;
        v.position = center; vh.AddVert(v); v.position = a; vh.AddVert(v); v.position = b; vh.AddVert(v); vh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>격자 왼쪽 위를 기준으로 한 픽셀 위치(x 오른쪽, y 아래)의 칸 모서리와 중심을 계산해 _corners에 채운다.</summary>
    private Vector2 CellGeometry(float left, float down, float inset)
    {
        Rect rect = rectTransform.rect;
        float l = rect.xMin + left + inset, r = rect.xMin + left + _cellSize - CELL_GAP - inset;
        float top = rect.yMax - down - inset, bottom = rect.yMax - down - _cellSize + CELL_GAP + inset;
        _corners[0] = new Vector2(l, top); _corners[1] = new Vector2(r, top); _corners[2] = new Vector2(r, bottom); _corners[3] = new Vector2(l, bottom);
        return new Vector2((l + r) / 2, (top + bottom) / 2);
    }

    /// <summary>구역 경계 한 변을 그린다. direction 0과 1은 중심에서 모서리로, 2는 바깥 변이다.</summary>
    private void EdgeLine(UnityEngine.UI.VertexHelper vh, Vector2 center, int sector, int direction, float thickness, Color color)
    {
        if (direction == 0) Line(vh, center, _corners[(sector + 1) % 4], thickness, color);
        else if (direction == 1) Line(vh, center, _corners[sector], thickness, color);
        else Line(vh, _corners[sector], _corners[(sector + 1) % 4], thickness, color);
    }

    /// <summary>3행 6열의 도안 구역, 배치 조각, 배치 가능 구역, 미리 보기와 도안 경계를 그린다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear(); if (_game == null) return;
        IReadOnlyList<int> owned = _game.Spells;
        UpdateTargets();
        SpellGame.Card selected = _ui.SelectedCard;
        int hoverSpell = -1;
        bool preview = !IsDraggingPattern && selected != null && _hoverX >= 0 && _layout.TryFindTarget(_hoverX, _hoverY, _hoverSector, owned, out hoverSpell);
        bool valid = preview && _ui.CanPreviewGrid(hoverSpell, new Vector2Int(_hoverX, _hoverY));
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            Vector2 center = CellGeometry(x * _cellSize, y * _cellSize, 0);
            for (int sector = 0; sector < 4; sector++)
            {
                Color color = _emptyColor;
                int owner;
                if (_layout.TryFindOwner(x, y, sector, owned, out owner))
                {
                    color = _targets[(y * SpellPatternLayout.COLUMNS + x) * 4 + sector] ? _targetColor : _requiredColor;
                    Vector2Int local; int localSector;
                    _layout.GridToLocal(owner, x, y, sector, out local, out localSector);
                    IReadOnlyList<SpellGame.Placement> pieces = _game.Boards[owner].Pieces;
                    for (int i = 0; i < pieces.Count; i++)
                        if ((pieces[i].CellMask(local.x, local.y) & (1 << localSector)) != 0) color = PieceColor(pieces[i]);
                }
                if (preview && (CellMask(selected, _hoverX, _hoverY, _ui.Rotation, x, y) & (1 << sector)) != 0) color = valid ? _validColor : _invalidColor;
                Sector(vh, center, _corners[sector], _corners[(sector + 1) % 4], color);
            }
            Color gridColor = Color.Lerp(_guideColor, Color.white, 0.35f);
            gridColor.a = 1f;
            for (int side = 0; side < 4; side++)
            {
                Line(vh, center, _corners[side], 1.2f, gridColor);
                Line(vh, _corners[side], _corners[(side + 1) % 4], 1.2f, gridColor);
            }
            for (int sector = 0; sector < 4; sector++)
            {
                int owner;
                if (!_layout.TryFindOwner(x, y, sector, owned, out owner)) continue;
                for (int direction = 0; direction < 3; direction++)
                {
                    int nx, ny, nSector, neighbor;
                    SpellPatternLayout.Neighbor(x, y, sector, direction, out nx, out ny, out nSector);
                    bool same = nx >= 0 && nx < SpellPatternLayout.COLUMNS && ny >= 0 && ny < SpellPatternLayout.ROWS && _layout.TryFindOwner(nx, ny, nSector, owned, out neighbor) && neighbor == owner;
                    if (!same) EdgeLine(vh, center, sector, direction, OUTLINE_WIDTH, _ui.PatternColor(owner));
                }
            }
        }
        if (IsDraggingPattern && _hasPatternPreview)
        {
            bool movable = _layout.MoveIssue(_dragPattern, _previewOrigin, _dragRotation, owned) == null;
            Color ghost = _ui.PatternColor(_dragPattern);
            DrawPatternShape(vh, _dragPattern, _dragRotation, new Vector2(_previewOrigin.x * _cellSize, _previewOrigin.y * _cellSize), PREVIEW_INSET, null, movable ? _validColor : _invalidColor, PREVIEW_WIDTH);
            DrawPatternShape(vh, _dragPattern, _dragRotation, _ghostTopLeft, 0, new Color(ghost.r, ghost.g, ghost.b, 0.28f), new Color(ghost.r, ghost.g, ghost.b, 0.9f), PREVIEW_WIDTH);
        }
    }

    /// <summary>회전된 도안 모양을 지정 픽셀 위치에 채우기와 외곽선으로 그린다.</summary>
    private void DrawPatternShape(UnityEngine.UI.VertexHelper vh, int spell, int rotation, Vector2 topLeft, float inset, Color? fill, Color line, float thickness)
    {
        Vector2Int size = SpellPatternLayout.RotatedSize(spell, rotation);
        for (int y = 0; y < size.y; y++) for (int x = 0; x < size.x; x++)
        {
            int mask = SpellPatternLayout.GridMask(spell, Vector2Int.zero, rotation, x, y);
            if (mask == 0) continue;
            Vector2 center = CellGeometry(topLeft.x + x * _cellSize, topLeft.y + y * _cellSize, inset);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((mask & (1 << sector)) == 0) continue;
                if (fill.HasValue) Sector(vh, center, _corners[sector], _corners[(sector + 1) % 4], fill.Value);
                for (int direction = 0; direction < 3; direction++)
                {
                    int nx, ny, nSector;
                    SpellPatternLayout.Neighbor(x, y, sector, direction, out nx, out ny, out nSector);
                    if ((SpellPatternLayout.GridMask(spell, Vector2Int.zero, rotation, nx, ny) & (1 << nSector)) == 0) EdgeLine(vh, center, sector, direction, thickness, line);
                }
            }
        }
    }

    /// <summary>선택 조각을 현재 회전으로 놓을 수 있는 구역 표시를 필요할 때만 다시 계산한다.</summary>
    private void UpdateTargets()
    {
        SpellGame.Card card = IsDraggingPattern ? null : _ui.SelectedCard;
        int rotation = _ui.Rotation;
        if (!_targetsDirty && card == _targetsCard && rotation == _targetsRotation) return;
        _targetsDirty = false; _targetsCard = card; _targetsRotation = rotation;
        if (_targets.Length != SpellPatternLayout.COLUMNS * SpellPatternLayout.ROWS * 4) _targets = new bool[SpellPatternLayout.COLUMNS * SpellPatternLayout.ROWS * 4];
        System.Array.Clear(_targets, 0, _targets.Length);
        if (card == null) return;
        IReadOnlyList<int> owned = _game.Spells;
        for (int i = 0; i < owned.Count; i++)
        {
            if (!_layout.IsPlaced(owned[i])) continue;
            for (int ay = 0; ay < SpellPatternLayout.ROWS; ay++) for (int ax = 0; ax < SpellPatternLayout.COLUMNS; ax++)
            {
                if (!_ui.CanPreviewGrid(owned[i], new Vector2Int(ax, ay))) continue;
                for (int y = ay; y <= Mathf.Min(ay + 1, SpellPatternLayout.ROWS - 1); y++) for (int x = ax; x <= Mathf.Min(ax + 1, SpellPatternLayout.COLUMNS - 1); x++)
                {
                    int mask = CellMask(card, ax, ay, rotation, x, y);
                    for (int sector = 0; sector < 4; sector++) if ((mask & (1 << sector)) != 0) _targets[(y * SpellPatternLayout.COLUMNS + x) * 4 + sector] = true;
                }
            }
        }
    }

    /// <summary>게임 상태가 바뀌어 배치 가능 구역을 다시 계산해야 함을 표시한다.</summary>
    public void MarkStateDirty() { _targetsDirty = true; SetVerticesDirty(); }

    /// <summary>화면 좌표를 격자 왼쪽 위 기준 픽셀 좌표(x 오른쪽, y 아래)로 바꾼다.</summary>
    private bool ToGridPixels(PointerEventData data, Vector2 position, out Vector2 pixels)
    {
        if (_ui.IsTableActive)
        {
            bool mapped = _ui.TryTableGrid(position, out Vector2 grid);
            pixels = grid * _cellSize; return mapped;
        }
        Vector2 p; pixels = Vector2.zero;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, position, data.pressEventCamera, out p)) return false;
        Rect rect = rectTransform.rect; pixels = new Vector2(p.x - rect.xMin, rect.yMax - p.y); return true;
    }

    /// <summary>화면 좌표를 공유 격자 좌표와 삼각 구역으로 변환한다.</summary>
    private bool GetCell(PointerEventData data, out int x, out int y, out int sector)
    {
        return GetCellAt(data, data.position, out x, out y, out sector);
    }

    /// <summary>지정 화면 좌표를 격자 칸과 삼각 구역으로 변환한다.</summary>
    private bool GetCellAt(PointerEventData data, Vector2 position, out int x, out int y, out int sector)
    {
        x = y = sector = 0; Vector2 pixels;
        if (!ToGridPixels(data, position, out pixels)) return false;
        float u = pixels.x / _cellSize, v = pixels.y / _cellSize;
        if (u < 0 || u >= SpellPatternLayout.COLUMNS || v < 0 || v >= SpellPatternLayout.ROWS) return false;
        x = (int)u; y = (int)v; float dx = u - x - 0.5f, dy = v - y - 0.5f;
        sector = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        return (SpellPatternLayout.BoardMask(x, y) & (1 << sector)) != 0;
    }

    /// <summary>드롭 좌표가 도안에 속하면 그 칸을 앵커로 조각을 배치한다.</summary>
    public bool TryDrop(PointerEventData data)
    {
        int x, y, sector, spell;
        if (!GetCell(data, out x, out y, out sector) || !_layout.TryFindTarget(x, y, sector, _game.Spells, out spell)) return false;
        _ui.ClickGrid(spell, new Vector2Int(x, y)); return true;
    }

    /// <summary>도안 이동을 시작한다. grabRotated는 회전된 도안 범위 안에서 잡은 칸이다.</summary>
    public void BeginPatternDrag(int pattern, Vector2Int grabRotated)
    {
        if (!_ui.CanMovePattern(pattern)) { _ui.SetPatternHint("고정된 조각이 있는 도안은 이동할 수 없습니다"); return; }
        _dragPattern = pattern; _dragRotation = _layout.Rotation(pattern);
        _grabLocal = SpellPatternLayout.UnrotateLocal(pattern, _dragRotation, grabRotated);
        _hasPatternPreview = false; _hoverX = _hoverY = -1;
        SetVerticesDirty();
    }

    /// <summary>드래그 중인 도안의 고스트를 포인터에 붙이고, 잡은 칸 기준 스냅 위치의 가능 여부를 표시한다.</summary>
    public void PreviewPattern(PointerEventData data)
    {
        if (!IsDraggingPattern) return;
        _lastPatternData = data;
        Vector2 pixels;
        if (!ToGridPixels(data, data.position, out pixels)) return;
        Vector2Int grab = SpellPatternLayout.RotateLocal(_dragPattern, _dragRotation, _grabLocal);
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(pixels.x / _cellSize), Mathf.FloorToInt(pixels.y / _cellSize));
        _previewOrigin = cell - grab;
        _ghostTopLeft = pixels - new Vector2((grab.x + 0.5f) * _cellSize, (grab.y + 0.5f) * _cellSize);
        _hasPatternPreview = true;
        _ui.SetPatternHint(_layout.MoveIssue(_dragPattern, _previewOrigin, _dragRotation, _game.Spells));
        SetVerticesDirty();
    }

    /// <summary>들고 있는 도안을 시계 방향으로 90도 돌리고 미리 보기를 갱신한다.</summary>
    public void RotatePatternDrag()
    {
        if (!IsDraggingPattern) return;
        _dragRotation = (_dragRotation + 1) % 4;
        if (_lastPatternData != null) PreviewPattern(_lastPatternData); else SetVerticesDirty();
    }

    /// <summary>도안 이동을 끝내고 격자 위면 스냅 위치·회전으로 옮긴다. 불가하면 원래 위치·방향을 유지한다.</summary>
    public void EndPatternDrag(PointerEventData data)
    {
        if (!IsDraggingPattern) return;
        PreviewPattern(data);
        int pattern = _dragPattern, rotation = _dragRotation; Vector2Int origin = _previewOrigin;
        int x, y, sector; bool onGrid = GetCell(data, out x, out y, out sector);
        _dragPattern = -1; _hasPatternPreview = false; _lastPatternData = null; _targetsDirty = true;
        if (onGrid) _ui.MovePattern(pattern, origin, rotation);
        else { _ui.SetPatternHint(null); SetVerticesDirty(); }
    }

    /// <summary>현재 커서 칸에서 회전된 조각의 배치 안내와 그래픽을 즉시 갱신한다.</summary>
    public void RefreshPreview()
    {
        int spell;
        if (_hoverX >= 0 && _layout.TryFindTarget(_hoverX, _hoverY, _hoverSector, _game.Spells, out spell)) _ui.SetPreviewHint(spell, new Vector2Int(_hoverX, _hoverY));
        else _ui.SetOutsidePreviewHint();
        SetVerticesDirty();
    }

    /// <summary>화면 갱신이나 비활성화 시 조각과 도안의 진행 중 드래그를 취소한다.</summary>
    public void CancelDrag()
    {
        _dragPiece = false; _dragPattern = -1; _hasPatternPreview = false; _lastPatternData = null; _hoverX = _hoverY = -1;
        SetVerticesDirty();
    }

    /// <summary>포인터가 가리키는 칸·구역을 갱신하고, 바뀐 경우에만 안내와 그래픽을 다시 그린다.</summary>
    private void UpdateHover(PointerEventData data)
    {
        int x, y, sector;
        if (!GetCell(data, out x, out y, out sector)) { ClearHover(data); return; }
        if (_dragPiece) { x -= _pieceOffset.x; y -= _pieceOffset.y; }
        if (x == _hoverX && y == _hoverY && sector == _hoverSector) return;
        _hoverX = x; _hoverY = y; _hoverSector = sector;
        SpellGame.Card tooltip = null;
        int spell;
        if (_layout.TryFindTarget(x, y, sector, _game.Spells, out spell))
        {
            _ui.SetPreviewHint(spell, new Vector2Int(x, y));
            if (!_dragPiece && _ui.SelectedCard == null) tooltip = PieceAt(x, y, sector)?.Card;
            if (tooltip != null && !SpellGameUI.HasTraits(tooltip)) tooltip = null;
        }
        else _ui.SetOutsidePreviewHint();
        if (_layout.TryFindTarget(x, y, sector, _game.Spells, out spell) && !_dragPiece && _ui.SelectedCard == null) _ui.ShowPatternTooltip(spell, data.position, tooltip);
        else _ui.ShowPieceTooltip(null, data.position);
        SetVerticesDirty();
    }

    /// <summary>가리킨 칸·구역 표시를 지운다.</summary>
    private void ClearHover(PointerEventData data)
    {
        if (_hoverX < 0) return;
        _hoverX = _hoverY = -1; _ui.ShowPieceTooltip(null, data.position); _ui.SetPreviewHint(-1, Vector2Int.zero); SetVerticesDirty();
    }

    /// <summary>격자 구역을 차지한 배치 조각을 찾는다. 없으면 null을 반환한다.</summary>
    private SpellGame.Placement PieceAt(int x, int y, int sector)
    {
        int owner;
        if (!_layout.TryFindOwner(x, y, sector, _game.Spells, out owner)) return null;
        Vector2Int local; int localSector;
        _layout.GridToLocal(owner, x, y, sector, out local, out localSector);
        IReadOnlyList<SpellGame.Placement> pieces = _game.Boards[owner].Pieces;
        for (int i = 0; i < pieces.Count; i++)
            if ((pieces[i].CellMask(local.x, local.y) & (1 << localSector)) != 0) return pieces[i];
        return null;
    }

    public void OnPointerMove(PointerEventData data)
    {
        if (!IsDraggingPattern) UpdateHover(data);
    }

    /// <summary>카드 드래그의 포인터 위치에 배치 미리 보기를 표시한다.</summary>
    public void PreviewCard(PointerEventData data)
    {
        UpdateHover(data);
    }

    public void OnPointerExit(PointerEventData data) { ClearHover(data); }

    public void OnPointerClick(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || data.dragging) return;
        int x, y, sector, spell;
        if (!GetCell(data, out x, out y, out sector)) return;
        if (!_layout.TryFindTarget(x, y, sector, _game.Spells, out spell)) { _ui.SetOutsidePreviewHint(); return; }
        SpellGame.Placement piece = _ui.SelectedCard == null ? PieceAt(x, y, sector) : null;
        int owner;
        if (piece != null && _layout.TryFindOwner(x, y, sector, _game.Spells, out owner)) { _ui.SelectPiece(owner, piece); return; }
        _ui.ClickGrid(spell, new Vector2Int(x, y));
    }

    public void OnBeginDrag(PointerEventData data)
    {
        if (!_ui.CanInteract || !isActiveAndEnabled || data.button != PointerEventData.InputButton.Left) return;
        int x, y, sector, spell;
        if (!GetCellAt(data, data.pressPosition, out x, out y, out sector)) return;
        SpellGame.Placement piece = PieceAt(x, y, sector);
        int owner;
        if (piece != null && _layout.TryFindOwner(x, y, sector, _game.Spells, out owner))
        {
            if (!_game.CanMovePiece(piece)) { _ui.SelectPiece(owner, piece); return; }
            Vector2Int anchor; int gridRotation;
            _layout.PieceToGrid(owner, piece, out anchor, out gridRotation);
            _dragPiece = true; _pieceOffset = new Vector2Int(x, y) - anchor; _hoverX = _hoverY = -1;
            _ui.BeginPieceDrag(owner, piece, data.position); return;
        }
        if (_ui.SelectedCard == null && _layout.TryFindTarget(x, y, sector, _game.Spells, out spell))
        {
            BeginPatternDrag(spell, new Vector2Int(x, y) - _layout.Origin(spell));
            PreviewPattern(data);
        }
    }

    public void OnDrag(PointerEventData data)
    {
        if (!_ui.CanInteract || !isActiveAndEnabled) return;
        if (_dragPiece) { _ui.MoveCardDrag(data.position); PreviewCard(data); }
        else if (IsDraggingPattern) PreviewPattern(data);
    }

    public void OnEndDrag(PointerEventData data)
    {
        if (!_ui.CanInteract || !isActiveAndEnabled) return;
        if (_dragPiece)
        {
            _dragPiece = false;
            int gx, gy, pieceSector, spell = -1;
            bool mapped = GetCell(data, out gx, out gy, out pieceSector);
            Vector2Int anchor = new Vector2Int(gx, gy) - _pieceOffset;
            mapped = mapped && _layout.TryFindTarget(anchor.x, anchor.y, pieceSector, _game.Spells, out spell);
            _ui.EndPieceDrag(data, mapped, spell, anchor); return;
        }
        if (IsDraggingPattern) EndPatternDrag(data);
    }
}

