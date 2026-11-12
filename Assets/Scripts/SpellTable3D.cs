using System.Collections.Generic;

using UnityEngine;

using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>공유 격자와 물리 손패를 3D 책상에 표시하고 화면 포인터를 기존 주문 규칙에 연결한다.</summary>
public sealed class SpellTable3D : MonoBehaviour
{
    private const int TABLE_LAYER = 30;
    private const float BOARD_MARGIN = 0.22f;
    // 도안 이름표 너비를 칸 한 변 단위로 나타낸 값이다. 두 글자 이름과 핸들이 한 줄에 들어가는 폭이다.
    private const float TAB_WIDTH_CELLS = 1.5f;
    private static readonly Vector3 WORLD_ORIGIN = new Vector3(1000, 0, 0);
    private static readonly Color NORMAL_COLOR = new Color(0.77f, 0.82f, 0.84f);
    private static readonly Color CLAY_COLOR = new Color(0.72f, 0.56f, 0.44f);

    [Header("테이블 카메라")]
    [SerializeField, Range(55f, 90f)] private float _cameraAngle = 90f;
    [SerializeField, Min(4f)] private float _cameraSize = 5.2f;

    [Header("조각과 낙하")]
    [SerializeField] private GameObject _tablePrefab;
    [SerializeField, Min(0.5f)] private float _dropHeight = 1.6f;
    [SerializeField] private Material _pieceMaterial;
    [SerializeField] private Material _lineMaterial;

    [Header("코팅·씰링 표시")]
    [SerializeField] private Material _guardMaterial;
    [SerializeField] private Material _amplifyMaterial;
    [SerializeField] private Material _cogMaterial;
    [SerializeField] private Material _sealMaterial;

    private readonly Dictionary<int, SpellPiece3D> _pieces = new Dictionary<int, SpellPiece3D>();
    private readonly List<int> _removed = new List<int>();
    private readonly HashSet<int> _activeCards = new HashSet<int>();
    private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
    private readonly List<Material> _materials = new List<Material>();
    private SpellGameUI _ui;
    private SpellGame _game;
    private SpellPatternLayout _layout;
    private SpellCombatScreen _screen;
    private Transform _world;
    private Transform _boardRoot;
    private int _handPage;
    private Transform _handRoot;
    private Transform _patterns;
    private Transform _preview;
    private Camera _camera;
    private Transform _handSurface;
    private float _cellSize;
    private Vector3 _boardPan;
    private PhysicsMaterial _physicsMaterial;
    private PointerEventData _pointer;
    private InputAction _pickAction;
    private InputAction _rotateAction;
    private int _requestedRotations;
    private Vector2 _downPosition;
    private Vector2 _upPosition;
    private bool _hasPress;
    private bool _hasRelease;
    private SpellPiece3D _picked;
    private Vector2Int _anchor;
    private Vector2 _pivot;
    private Vector2 _pivotOffset;
    private Vector2Int _local;
    private int _rotation;
    private int _localRotation;
    private int _destination = -1;
    private bool _isHolding;
    private bool _isClickSelected;
    private Vector2 _pressPosition;
    private float _lastAngle;
    private float _lastAspect;
    private int _previewPattern = -1;
    private Vector2Int _previewOrigin;
    private int _previewRotation;
    private bool _hadTableHover;
    private int _castSource = -1;
    private int _dragTemplate = -1;
    private bool _isCastDragging;
    private bool _isTemplateDragging;
    private readonly int[] _overlapCounts = new int[SpellPatternLayout.COLUMNS * SpellPatternLayout.ROWS * 4];
    private Vector2Int _autoCell;
    private bool _hasAutoCell;
    private bool _isAutoRotateLocked;

    public bool IsDragging { get { return _picked != null || _castSource >= 0 || _dragTemplate >= 0; } }

    /// <summary>기존 전투 모델과 UI를 받아 전용 카메라, 판과 물리 손패 공간을 초기화한다.</summary>
    public void Setup(SpellGameUI ui, SpellGame game, SpellPatternLayout layout, SpellCombatScreen screen)
    {
        _ui = ui; _game = game; _layout = layout; _screen = screen;
        _pointer = new PointerEventData(EventSystem.current);
        _pickAction = new InputAction("Table pick", InputActionType.Button, "<Mouse>/leftButton");
        _pickAction.performed += RecordPress; _pickAction.canceled += RecordRelease; _pickAction.Enable();
        _rotateAction = new InputAction("Rotate held piece", InputActionType.Button);
        _rotateAction.AddBinding("<Keyboard>/r"); _rotateAction.AddBinding("<Mouse>/rightButton");
        _rotateAction.performed += RecordRotation; _rotateAction.Enable();
        _physicsMaterial = new PhysicsMaterial("Piece bounce") { bounciness = 0.32f, dynamicFriction = 0.65f, staticFriction = 0.75f, bounceCombine = PhysicsMaterialCombine.Maximum };
        _world = Instantiate(_tablePrefab).transform;
        _world.position = WORLD_ORIGIN;
        _camera = CreateRoot("Table Camera", _world).gameObject.AddComponent<Camera>();
        _camera.orthographic = true; _camera.orthographicSize = _cameraSize;
        _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(0.035f, 0.045f, 0.075f);
        _camera.cullingMask = 1 << TABLE_LAYER; _camera.depth = 10; _camera.nearClipPlane = 0.1f; _camera.farClipPlane = 50;
        Light light = CreateRoot("Table Key Light", _world).gameObject.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 0.8f; light.color = new Color(1f, 0.91f, 0.77f);
        light.cullingMask = 1 << TABLE_LAYER; light.transform.rotation = Quaternion.Euler(52, -25, 0);
        _boardRoot = _world.Find("Grid 3 x 4");
        _handRoot = _world.Find("Physical Hand");
        Transform boardSurface = _boardRoot.Find("Board slab");
        _handSurface = _handRoot.Find("Hand tray");
        Vector3 boardSize = boardSurface.localScale;
        // 화면 기준 정사각형 판을 기존 받침의 짧은 변 안에 맞춘다.
        float side = Mathf.Min(boardSize.x, boardSize.z);
        _cellSize = Mathf.Max(0.1f, (side - BOARD_MARGIN) / SpellPatternLayout.BOARD_SIZE);
        boardSize.x = boardSize.z = side;
        boardSurface.localScale = boardSize;
        boardSurface.localPosition = Vector3.down * boardSize.y * 0.5f;
        _handSurface.localPosition = Vector3.down * _handSurface.localScale.y * 0.5f;
        boardSurface.GetComponent<BoxCollider>().sharedMaterial = _physicsMaterial;
        _handSurface.GetComponent<BoxCollider>().sharedMaterial = _physicsMaterial;
        _patterns = CreateRoot("Spell Outlines", _boardRoot);
        _preview = CreateRoot("Template Preview", _boardRoot);
        BuildSurfaces();
        ConfigureCamera();
        _screen.Board.enabled = false;
        _screen.HandDropArea.gameObject.SetActive(false);
        foreach (SpellHandCardView card in _screen.Hand) card.gameObject.SetActive(false);
        foreach (SpellCastInput input in _screen.GetComponentsInChildren<SpellCastInput>(true)) input.enabled = false;
        foreach (SpellPatternDrag input in _screen.GetComponentsInChildren<SpellPatternDrag>(true)) input.enabled = false;
    }

    /// <summary>새 오브젝트를 전용 레이어의 지정 부모 아래에 만들고 변환을 반환한다.</summary>
    private static Transform CreateRoot(string name, Transform parent)
    {
        GameObject obj = new GameObject(name); obj.layer = TABLE_LAYER;
        obj.transform.SetParent(parent, false); return obj.transform;
    }

    /// <summary>원본 재질을 복사해 지정 색을 설정하고 종료 시 정리할 목록에 등록한다.</summary>
    private Material Colored(Material source, Color color)
    {
        Material material = new Material(source); material.color = color; _materials.Add(material); return material;
    }

    /// <summary>판에 속한 삼각 구역의 변을 판 위 선으로 그린다. 두 구역이 공유하는 변은 한 번만 그린다.</summary>
    private void BuildSurfaces()
    {
        Material grid = Colored(_lineMaterial, new Color(0.46f, 0.49f, 0.57f));
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            int mask = SpellPatternLayout.BoardMask(x, y);
            Vector2[] corners = { new Vector2(x, y), new Vector2(x + 1, y), new Vector2(x + 1, y + 1), new Vector2(x, y + 1) };
            Vector2 center = new Vector2(x + 0.5f, y + 0.5f);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((mask & (1 << sector)) == 0) continue;
                int key = (y * SpellPatternLayout.COLUMNS + x) * 4 + sector;
                for (int edge = 0; edge < 3; edge++)
                {
                    SpellPatternLayout.Neighbor(x, y, sector, edge, out int nx, out int ny, out int ns);
                    bool shared = (SpellPatternLayout.BoardMask(nx, ny) & (1 << ns)) != 0;
                    if (shared && (ny * SpellPatternLayout.COLUMNS + nx) * 4 + ns < key) continue;
                    Vector2 a = corners[sector], b = corners[(sector + 1) % 4];
                    Vector2 from = edge == 0 ? b : edge == 1 ? center : a;
                    Vector2 to = edge == 0 ? center : edge == 1 ? a : b;
                    Line(_boardRoot, new[] { GridLocal(from), GridLocal(to) }, grid, 0.016f);
                }
            }
        }
    }

    /// <summary>로컬 꼭짓점들을 잇는 굵은 선을 만들고 지정 재질을 공유한다.</summary>
    private static void Line(Transform parent, Vector3[] vertices, Material material, float width)
    {
        LineRenderer line = CreateRoot("Inlaid line", parent).gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.positionCount = vertices.Length; line.SetPositions(vertices);
        line.widthMultiplier = width; line.sharedMaterial = material; line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
    }

    /// <summary>격자 좌표를 판 중심 기준 로컬 좌표로 바꾼다. 격자 +y는 화면 아래(-z)다.</summary>
    private Vector3 GridLocal(Vector2 point, float height = 0.025f)
    {
        Vector2 board = SpellPatternLayout.GridToBoard(point) * _cellSize;
        return new Vector3(board.x, height, -board.y);
    }

    /// <summary>Inspector 각도로 카메라를 조정하고 판과 손패를 목표 화면 위치에 놓는다.</summary>
    private void ConfigureCamera()
    {
        _camera.transform.rotation = Quaternion.Euler(_cameraAngle, 0, 0);
        _camera.transform.position = WORLD_ORIGIN - _camera.transform.forward * 15f;
        _camera.orthographicSize = _cameraSize;
        _boardRoot.position = ViewportOnTable(ViewportCenter(_screen.Board.rectTransform)) + _boardPan;
        _handRoot.position = ViewportOnTable(ViewportCenter(_screen.HandDropArea));
        FitHandTray();
        _lastAngle = _cameraAngle; _lastAspect = _camera.aspect;
    }

    /// <summary>손패 놓기 영역의 화면 범위를 책상면에 투영해 손패 받침의 가로·세로 길이를 맞춘다.</summary>
    private void FitHandTray()
    {
        RectTransform area = _screen.HandDropArea;
        Vector3 left = ViewportOnTable(ViewportAt(area, new Vector2(0, 0.5f))), right = ViewportOnTable(ViewportAt(area, new Vector2(1, 0.5f)));
        Vector3 bottom = ViewportOnTable(ViewportAt(area, new Vector2(0.5f, 0))), top = ViewportOnTable(ViewportAt(area, new Vector2(0.5f, 1)));
        Vector3 size = _handSurface.localScale;
        size.x = Vector3.Distance(left, right); size.z = Vector3.Distance(bottom, top);
        _handSurface.localScale = size;
    }

    /// <summary>뷰포트 위치의 광선과 수평 책상면의 교점을 반환한다.</summary>
    private Vector3 ViewportOnTable(Vector2 point)
    {
        Ray ray = _camera.ViewportPointToRay(point); new Plane(Vector3.up, WORLD_ORIGIN).Raycast(ray, out float distance); return ray.GetPoint(distance);
    }

    /// <summary>전투 화면 프리팹의 기준 영역 중심을 뷰포트 좌표로 반환한다.</summary>
    private static Vector2 ViewportCenter(RectTransform rect)
    {
        return ViewportAt(rect, new Vector2(0.5f, 0.5f));
    }

    /// <summary>영역 안의 정규화 위치(0~1)를 뷰포트 좌표로 반환한다.</summary>
    private static Vector2 ViewportAt(RectTransform rect, Vector2 normalized)
    {
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(Rect.NormalizedToPoint(rect.rect, normalized)));
        return new Vector2(screen.x / Screen.width, screen.y / Screen.height);
    }

    /// <summary>전투 표시 여부를 바꾸고 화면 전환 시 진행 중인 집기를 취소한다.</summary>
    public void SetVisible(bool visible)
    {
        CancelPick(); _castSource = _dragTemplate = -1; _isCastDragging = _isTemplateDragging = false;
        if (_world != null) _world.gameObject.SetActive(visible);
    }

    /// <summary>조각 집기와 도안 이동 및 시전 드래그를 취소한다.</summary>
    public void CancelInteractions()
    {
        CancelPick(); _castSource = _dragTemplate = -1;
        _isCastDragging = _isTemplateDragging = false;
        _hasPress = _hasRelease = false; _requestedRotations = 0;
    }

    /// <summary>모델의 손패·배치 상태와 3D 조각을 동기화하고 새 손패만 낙하시킨다.</summary>
    public void Sync()
    {
        _handPage = Mathf.Clamp(_handPage, 0, Mathf.Max(0, (_game.Hand.Count - 1) / 9));
        CancelPick(); Vector3 previousHandPosition = _handRoot.position;
        Canvas.ForceUpdateCanvases(); ConfigureCamera();
        Vector3 handOffset = _handRoot.position - previousHandPosition;
        if (handOffset.sqrMagnitude > 0.000001f)
            foreach (SpellPiece3D piece in _pieces.Values) if (piece.IsHand) piece.Body.position += handOffset;
        _activeCards.Clear();
        for (int i = _handPage * 9; i < Mathf.Min(_game.Hand.Count, (_handPage + 1) * 9); i++) _activeCards.Add(_game.Hand[i].Id);
        foreach (int spell in _game.Spells) foreach (SpellGame.Placement piece in _game.Boards[spell].Pieces) _activeCards.Add(piece.Card.Id);
        _removed.Clear();
        foreach (KeyValuePair<int, SpellPiece3D> entry in _pieces) if (!_activeCards.Contains(entry.Key)) _removed.Add(entry.Key);
        foreach (int id in _removed) { Destroy(_pieces[id].gameObject); _pieces.Remove(id); }
        for (int i = _handPage * 9; i < Mathf.Min(_game.Hand.Count, (_handPage + 1) * 9); i++)
        {
            SpellGame.Card card = _game.Hand[i];
            SpellPiece3D piece = GetPiece(card);
            if (piece.IsHand) continue;
            int slot = FreeSlot(); piece.DropToHand(slot, SlotPosition(slot), _cellSize, _dropHeight + Random.Range(0f, 0.45f));
        }
        foreach (int spell in _game.Spells) foreach (SpellGame.Placement placement in _game.Boards[spell].Pieces) PlaceOnBoard(GetPiece(placement.Card), spell, placement);
        RebuildPatterns(); ProjectControls(); RefreshCurseTint();
    }

    /// <summary>손패 페이지를 변경하고 해당 페이지의 조각을 표시한다.</summary>
    public void ChangeHandPage(int direction)
    {
        _handPage = Mathf.Clamp(_handPage + direction, 0, Mathf.Max(0, (_game.Hand.Count - 1) / 9));
        Sync();
    }

    /// <summary>배치 조각을 격자 칸 모양과 위치에 고정한다. 다른 조각 위에 겹친 덧칠 조각은 한 층 위에 놓는다.</summary>
    private void PlaceOnBoard(SpellPiece3D piece, int spell, SpellGame.Placement placement)
    {
        List<SpellGame.ShapeCell> cells = _layout.PlacementGridCells(spell, placement);
        List<SpellGame.SealSpot> seals = _layout.PlacementGridSeals(spell, placement);
        Vector2Int anchor = new Vector2Int(int.MaxValue, int.MaxValue);
        foreach (SpellGame.ShapeCell cell in cells) anchor = Vector2Int.Min(anchor, new Vector2Int(cell.X, cell.Y));
        for (int i = 0; i < cells.Count; i++) cells[i] = new SpellGame.ShapeCell(cells[i].X - anchor.x, cells[i].Y - anchor.y, cells[i].Mask);
        for (int i = 0; i < seals.Count; i++) seals[i] = new SpellGame.SealSpot(seals[i].X - anchor.x, seals[i].Y - anchor.y, seals[i].Sector, seals[i].Seal);
        float layer = IsLayered(spell, placement) ? SpellPiece3D.Thickness * _cellSize : 0;
        piece.PlaceOnBoard(spell, placement, cells, seals, PiecePosition(anchor, cells, 0.045f + layer), Quaternion.identity, _cellSize);
    }

    /// <summary>덧칠 씰링이 있는 조각이 같은 판의 다른 조각과 구역을 공유하는지 반환한다.</summary>
    private bool IsLayered(int spell, SpellGame.Placement placement)
    {
        if (SpellGame.SealCount(placement.Card, SpellGame.Seal.Overpaint) == 0) return false;
        foreach (SpellGame.Placement other in _game.Boards[spell].Pieces)
        {
            if (other == placement) continue;
            for (int y = 0; y < SpellGame.LOCAL_SIZE; y++) for (int x = 0; x < SpellGame.LOCAL_SIZE; x++) if ((other.CellMask(x, y) & placement.CellMask(x, y)) != 0) return true;
        }
        return false;
    }

    /// <summary>집은 조각을 현재 방향의 칸 모양으로 반환한다. 점토는 한 칸으로 보여 준다.</summary>
    private IReadOnlyList<SpellGame.ShapeCell> HeldCells()
    {
        return SpellGame.CardCells(_picked.Card, _picked.Card.Shape == SpellGame.Shape.Clay ? 0 : _rotation);
    }

    /// <summary>집은 조각의 씰링을 HeldCells와 같은 방향 좌표로 반환한다.</summary>
    private IReadOnlyList<SpellGame.SealSpot> HeldSeals()
    {
        return SpellGame.SealSpots(_picked.Card, _picked.Card.Shape == SpellGame.Shape.Clay ? 0 : _rotation);
    }

    /// <summary>코팅에 맞는 3D 조각 Material을 반환한다. 코팅이 없으면 기본 조각 Material이다.</summary>
    private Material CoatingMaterial(SpellGame.Coating coating)
    {
        switch (coating)
        {
            case SpellGame.Coating.Guard: return _guardMaterial;
            case SpellGame.Coating.Amplify: return _amplifyMaterial;
            case SpellGame.Coating.Cog: return _cogMaterial;
            default: return _pieceMaterial;
        }
    }

    /// <summary>카드에 대응하는 3D 조각을 재사용하거나 카드 모양과 효과 색으로 생성한다.</summary>
    private SpellPiece3D GetPiece(SpellGame.Card card)
    {
        if (_pieces.TryGetValue(card.Id, out SpellPiece3D piece)) return piece;
        piece = CreateRoot("Piece " + card.Id + " " + card.Shape, _world).gameObject.AddComponent<SpellPiece3D>();
        piece.Setup(card, CoatingMaterial(card.Coating), _physicsMaterial, PieceTint(card), _sealMaterial, _ui.SealColor); _pieces.Add(card.Id, piece); return piece;
    }

    /// <summary>3D 조각의 기본 색을 반환한다. 코팅 색, 이전 효과 색, 점토 색, 일반 색 순으로 고른다.</summary>
    private Color PieceTint(SpellGame.Card card)
    {
        if (card.IsContaminated) return _ui.ShapeTint(card);
        if (card.Coating != SpellGame.Coating.None) return _ui.CoatingColor(card.Coating);
        if (card.Effect != SpellGame.Effect.None) return _ui.EffectColor(card.Effect);
        return card.IsUnique ? CLAY_COLOR : NORMAL_COLOR;
    }

    /// <summary>최대 일곱 조각의 회전 외접원이 겹치지 않는 빈 손패 자리를 무작위로 고른다.</summary>
    private int FreeSlot()
    {
        int first = Random.Range(0, 9);
        for (int i = 0; i < 9; i++)
        {
            int slot = (first + i) % 9; bool used = false;
            foreach (SpellPiece3D piece in _pieces.Values) if (piece.IsHand && piece.Slot == slot) { used = true; break; }
            if (!used) return slot;
        }
        return 0;
    }

    /// <summary>손패의 예약 자리 중심을 반환한다. 물리 yaw 회전 중에도 조각끼리 겹치지 않는다.</summary>
    private Vector3 SlotPosition(int slot)
    {
        Vector3 size = _handSurface.localScale;
        return _handRoot.position + new Vector3((slot % 3 - 1) * size.x * (1.46f / 4.65f), 0.025f, (slot / 3 - 1) * size.z * (1.08f / 3.45f));
    }

    /// <summary>격자 앵커(범위 왼쪽 위)와 칸 범위로 조각 메시 중심의 월드 위치를 계산한다.</summary>
    private Vector3 PiecePosition(Vector2Int anchor, IReadOnlyList<SpellGame.ShapeCell> cells, float height)
    {
        Vector2 center = anchor + SpellPiece3D.CellBounds(cells) / 2;
        return _boardRoot.TransformPoint(GridLocal(center, height));
    }

    /// <summary>화면 좌표를 3D 판 위 소수 격자 좌표로 바꾸고 평면 교차 여부를 반환한다.</summary>
    public bool ScreenToGrid(Vector2 screenPosition, out Vector2 grid)
    {
        Ray ray = _camera.ScreenPointToRay(screenPosition); Plane plane = new Plane(Vector3.up, _boardRoot.position);
        if (!plane.Raycast(ray, out float distance)) { grid = default; return false; }
        Vector3 local = _boardRoot.InverseTransformPoint(ray.GetPoint(distance));
        grid = SpellPatternLayout.BoardToGrid(new Vector2(local.x, -local.z) / _cellSize); return true;
    }

    /// <summary>도안의 삼각 구역을 얇은 입체로 채우고 외부 경계에만 주문 색 외곽선을 그린다.</summary>
    private void RebuildPatterns()
    {
        ClearGeometry(_patterns);
        foreach (int spell in _game.Spells)
        {
            if (!_layout.IsPlaced(spell)) continue;
            Material fill = new Material(_lineMaterial) { color = _ui.PatternColor(spell) * 0.28f };
            Material border = new Material(_lineMaterial) { color = _ui.PatternColor(spell) };
            DrawPattern(_patterns, spell, _layout.Origin(spell), _layout.Rotation(spell), fill, border);
        }
    }

    /// <summary>지정 도안 표시의 자식과 해당 표시가 소유한 임시 메시·재질을 정리한다.</summary>
    private void ClearGeometry(Transform root)
    {
        HashSet<Material> materials = new HashSet<Material>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>()) materials.Add(renderer.sharedMaterial);
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>()) Destroy(filter.sharedMesh);
        foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (Material material in materials) Destroy(material);
    }

    /// <summary>지정 원점과 회전의 구역 마스크를 표면 메시와 경계선으로 표시한다.</summary>
    private void DrawPattern(Transform parent, int spell, Vector2Int origin, int rotation, Material fill, Material border)
    {
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            int mask = SpellPatternLayout.GridMask(spell, origin, rotation, x, y);
            Vector2[] corners = { new Vector2(x, y), new Vector2(x + 1, y), new Vector2(x + 1, y + 1), new Vector2(x, y + 1) };
            Vector2 center = new Vector2(x + 0.5f, y + 0.5f);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((mask & (1 << sector)) == 0) continue;
                Vector2 a = corners[sector], b = corners[(sector + 1) % 4]; int start = vertices.Count;
                vertices.Add(GridLocal(a, 0.015f)); vertices.Add(GridLocal(b, 0.015f)); vertices.Add(GridLocal(center, 0.015f));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                for (int edge = 0; edge < 3; edge++)
                {
                    SpellPatternLayout.Neighbor(x, y, sector, edge, out int nx, out int ny, out int ns);
                    if ((SpellPatternLayout.GridMask(spell, origin, rotation, nx, ny) & (1 << ns)) != 0) continue;
                    Vector2 from = edge == 0 ? b : edge == 1 ? center : a;
                    Vector2 to = edge == 0 ? center : edge == 1 ? a : b;
                    Line(parent, new[] { GridLocal(from, 0.295f), GridLocal(to, 0.295f) }, border, 0.045f);
                }
            }
        }
        Mesh mesh = new Mesh { name = "Pattern sectors" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
        GameObject obj = CreateRoot("Pattern surface", parent).gameObject;
        obj.AddComponent<MeshFilter>().sharedMesh = mesh; obj.AddComponent<MeshRenderer>().sharedMaterial = fill;
    }

    /// <summary>도안 외곽과 중심을 투영해 기존 이름표·시전 버튼을 3D 판 위에 붙인다.</summary>
    public void ProjectControls()
    {
        RectTransform parent = (RectTransform)_screen.Tabs[0].transform.parent;
        List<Rect> occupied = new List<Rect>();
        foreach (int spell in _game.Spells)
        {
            if (!_layout.IsPlaced(spell)) continue;
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity, sum = Vector2.zero; int weight = 0;
            for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
            {
                int mask = _layout.PlacedMask(spell, x, y);
                Vector2[] corners = { new Vector2(x, y), new Vector2(x + 1, y), new Vector2(x + 1, y + 1), new Vector2(x, y + 1) };
                Vector2 center = new Vector2(x + 0.5f, y + 0.5f);
                for (int sector = 0; sector < 4; sector++)
                {
                    if ((mask & (1 << sector)) == 0) continue;
                    // 카메라 각도에 따른 원근을 반영하도록 삼각형 꼭짓점을 투영해 범위를 구한다.
                    foreach (Vector2 point in new[] { corners[sector], corners[(sector + 1) % 4], center })
                    {
                        Vector2 overlay = OverlayPoint(parent, point);
                        min = Vector2.Min(min, overlay); max = Vector2.Max(max, overlay);
                    }
                    sum += center; weight++;
                }
            }
            Vector2 leftTop = new Vector2(min.x, max.y), rightBottom = new Vector2(max.x, min.y);
            float width = Vector2.Distance(OverlayPoint(parent, Vector2.right * TAB_WIDTH_CELLS), OverlayPoint(parent, Vector2.zero));
            Vector2[] candidates = { leftTop + Vector2.up * 36, new Vector2(leftTop.x, rightBottom.y), leftTop + Vector2.left * width, new Vector2(rightBottom.x, leftTop.y) };
            Vector2 selected = candidates[0];
            foreach (Vector2 candidate in candidates)
            {
                Rect rect = new Rect(candidate.x, -candidate.y, width, 36); bool collision = false;
                foreach (Rect previous in occupied) if (rect.Overlaps(previous)) collision = true;
                foreach (int other in _game.Spells)
                {
                    if (other == spell || !_layout.IsPlaced(other)) continue;
                    for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
                        if (_layout.PlacedMask(other, x, y) != 0 && rect.Overlaps(OverlayCellRect(parent, x, y))) collision = true;
                }
                if (!collision) { selected = candidate; break; }
            }
            occupied.Add(new Rect(selected.x, -selected.y, width, 36));
            _screen.Tabs[spell].Bind(SpellGame.PatternName(spell) + (_game.IsSealed(spell) ? " [봉인]" : ""), _ui.PatternColor(spell), selected, width);
            _screen.CastButtons[spell].Rect.anchoredPosition = OverlayPoint(parent, sum / Mathf.Max(1, weight));
        }
    }

    /// <summary>격자 위치를 이름표 부모의 왼쪽 위 기준 UI 좌표로 투영한다.</summary>
    private Vector2 OverlayPoint(RectTransform parent, Vector2 grid)
    {
        Vector3 screen = _camera.WorldToScreenPoint(_boardRoot.TransformPoint(GridLocal(grid, 0.3f)));
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out Vector2 point);
        return new Vector2(point.x - parent.rect.xMin, point.y - parent.rect.yMax);
    }

    /// <summary>격자 칸의 네 꼭짓점을 투영한 화면 범위를 이름표 충돌 검사용 좌표(x 오른쪽, y 아래)로 반환한다.</summary>
    private Rect OverlayCellRect(RectTransform parent, int x, int y)
    {
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = OverlayPoint(parent, new Vector2(x + (i == 1 || i == 2 ? 1 : 0), y + (i >= 2 ? 1 : 0)));
            p.y = -p.y; min = Vector2.Min(min, p); max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    void Update()
    {
        bool pressed = _hasPress, released = _hasRelease; _hasPress = _hasRelease = false;
        int rotations = _requestedRotations; _requestedRotations = 0;
        if (_world == null || !_world.gameObject.activeSelf) return;
        if (!Mathf.Approximately(_lastAngle, _cameraAngle) || !Mathf.Approximately(_lastAspect, _camera.aspect))
        {
            CancelPick(); ConfigureCamera(); Sync();
            foreach (SpellPiece3D piece in _pieces.Values) if (piece.IsHand) piece.DropToHand(piece.Slot, SlotPosition(piece.Slot), _cellSize, 0.2f);
        }
        RefreshTemplatePreview();
        if (!_ui.CanInteract) { CancelPick(); return; }
        if (_screen.MoreMenu.IsOpen) { CancelPick(); return; }
        if (!IsDragging && Keyboard.current != null)
        {
            Vector2 direction = new Vector2((Keyboard.current.rightArrowKey.isPressed ? 1 : 0) - (Keyboard.current.leftArrowKey.isPressed ? 1 : 0),
                (Keyboard.current.upArrowKey.isPressed ? 1 : 0) - (Keyboard.current.downArrowKey.isPressed ? 1 : 0));
            if (direction.sqrMagnitude > 0) PanBoard(direction.normalized * (3f * Time.unscaledDeltaTime));
        }
        if (Mouse.current == null) return;
        Vector2 pointer = Mouse.current.position.ReadValue();
        if (pressed && _castSource < 0 && _dragTemplate < 0) BeginOverlayInput();
        if (_castSource >= 0) { UpdateCastInput(pointer, released); return; }
        if (_dragTemplate >= 0) { UpdateTemplateInput(pointer, released, rotations); return; }
        if (_picked == null && pressed && !IsOverControl(_downPosition) && !_screen.Board.IsDraggingPattern)
        {
            Ray pressRay = _camera.ScreenPointToRay(_downPosition);
            if (Physics.Raycast(pressRay, out RaycastHit pressHit, 50f, 1 << TABLE_LAYER))
            {
                SpellPiece3D piece = pressHit.collider.GetComponent<SpellPiece3D>();
                if (piece != null && (piece.IsHand || _game.CanMovePiece(piece.Placement))) BeginPick(piece, _downPosition);
            }
        }
        else if (_picked != null && pressed && _isClickSelected) { FinishPick(_downPosition); return; }
        if (_picked != null)
        {
            if (rotations > 0) { SetPickRotation((_rotation + rotations) % 4); _isAutoRotateLocked = true; }
            UpdateAutoRotation(pointer);
            PreviewPick(pointer);
            if (released && _isHolding)
            {
                _isHolding = false;
                if ((_upPosition - _pressPosition).sqrMagnitude < 36f) _isClickSelected = true;
                else FinishPick(_upPosition);
            }
            return;
        }
        if (IsOverControl(pointer) || _screen.Board.IsDraggingPattern) { HideHover(pointer); return; }
        Ray ray = _camera.ScreenPointToRay(pointer);
        SpellPiece3D hovered = Physics.Raycast(ray, out RaycastHit hit, 50f, 1 << TABLE_LAYER) ? hit.collider.GetComponent<SpellPiece3D>() : null;
        if (hovered != null && hovered.IsHand) { _ui.ShowPieceTooltip(hovered.Card, pointer); _hadTableHover = true; }
        else if (ScreenToGrid(pointer, out Vector2 grid) && OnGrid(grid) && _layout.TryFindTarget((int)grid.x, (int)grid.y, Sector(grid), _game.Spells, out int spell))
        { _ui.ShowPatternTooltip(spell, pointer, hovered == null ? null : hovered.Card); _hadTableHover = true; }
        else HideHover(pointer);
    }

    /// <summary>격자와 배치된 조각을 방향키 입력만큼 이동하고 화면의 도안 조작 위치를 갱신한다.</summary>
    private void PanBoard(Vector2 direction)
    {
        Vector3 delta = new Vector3(direction.x, 0, direction.y);
        _boardPan += delta; _boardRoot.position += delta;
        foreach (SpellPiece3D piece in _pieces.Values) if (!piece.IsHand) piece.Body.position += delta;
        ProjectControls();
    }

    /// <summary>입력 이벤트 시점의 누른 위치를 저장해 한 프레임 안에 끝난 빠른 드래그도 보존한다.</summary>
    private void RecordPress(InputAction.CallbackContext context)
    {
        _downPosition = Mouse.current.position.ReadValue(); _hasPress = true;
    }

    /// <summary>놓은 위치를 저장하고 다음 화면 갱신에서 배치 또는 클릭 선택을 마무리한다.</summary>
    private void RecordRelease(InputAction.CallbackContext context)
    {
        _upPosition = Mouse.current.position.ReadValue(); _hasRelease = true;
    }

    /// <summary>집기 중 회전 입력 횟수를 보존해 짧거나 연속된 입력도 다음 갱신에 모두 반영한다.</summary>
    private void RecordRotation(InputAction.CallbackContext context)
    {
        if (_picked != null || _dragTemplate >= 0) _requestedRotations++;
    }

    /// <summary>시전 버튼과 도안 이름표의 누른 위치를 기록해 빠른 드래그를 기존 주문 입력에 연결한다.</summary>
    private void BeginOverlayInput()
    {
        foreach (int spell in _game.Spells)
        {
            RectTransform cast = _screen.CastButtons[spell].Rect;
            if (cast.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(cast, _downPosition, null))
            {
                CancelPick(); _castSource = spell; _isCastDragging = false; return;
            }
            RectTransform tab = _screen.Tabs[spell].Rect;
            if (!tab.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(tab, _downPosition, null)) continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(tab, _downPosition, null, out Vector2 local);
            if (local.x - tab.rect.xMin >= tab.rect.width - 40f) return;
            CancelPick(); _dragTemplate = spell; _isTemplateDragging = false; return;
        }
    }

    /// <summary>시전 버튼 클릭은 기존 대상 선택에, 드래그는 직선 조준 및 놓은 적 시전에 전달한다.</summary>
    private void UpdateCastInput(Vector2 position, bool released)
    {
        int spell = _castSource;
        Vector2 point = released ? _upPosition : position;
        if (!_isCastDragging && (point - _downPosition).sqrMagnitude >= 36f)
        {
            _isCastDragging = true; _ui.BeginCastDrag(spell);
        }
        _pointer.position = point; _pointer.pressPosition = _downPosition;
        if (_isCastDragging) _ui.PreviewCast(spell, _pointer);
        if (!released) return;
        bool dragged = _isCastDragging; _castSource = -1; _isCastDragging = false;
        if (dragged) _ui.DropCast(spell, _pointer);
        else if (RectTransformUtility.RectangleContainsScreenPoint(_screen.CastButtons[spell].Rect, point, null)) _ui.ClickCast(spell);
    }

    /// <summary>도안 핸들 드래그와 회전을 3D 격자 좌표의 기존 도안 이동 로직에 전달한다.</summary>
    private void UpdateTemplateInput(Vector2 position, bool released, int rotations)
    {
        Vector2 point = released ? _upPosition : position;
        if (!_isTemplateDragging && (point - _downPosition).sqrMagnitude >= 36f)
        {
            _screen.Board.BeginPatternDrag(_dragTemplate, Vector2Int.zero); _isTemplateDragging = true;
        }
        if (_isTemplateDragging)
        {
            _pointer.position = point; _pointer.pressPosition = _downPosition;
            _screen.Board.PreviewPattern(_pointer);
            for (int i = 0; i < rotations; i++) _screen.Board.RotatePatternDrag();
        }
        if (!released) return;
        bool dragged = _isTemplateDragging; _dragTemplate = -1; _isTemplateDragging = false;
        if (dragged) _screen.Board.EndPatternDrag(_pointer);
    }

    /// <summary>판에서 벗어나거나 UI로 포인터가 이동했을 때 테이블이 띄운 설명만 닫는다.</summary>
    private void HideHover(Vector2 pointer)
    {
        if (_hadTableHover) { _ui.ShowPieceTooltip(null, pointer); _hadTableHover = false; }
    }

    /// <summary>포인터가 기존 클릭·드래그 UI를 가리키는지 확인해 3D 집기와 중복되지 않게 한다.</summary>
    private bool IsOverControl(Vector2 position)
    {
        _pointer.position = position; _uiHits.Clear(); EventSystem.current.RaycastAll(_pointer, _uiHits);
        foreach (RaycastResult hit in _uiHits)
            if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) != null || ExecuteEvents.GetEventHandler<IBeginDragHandler>(hit.gameObject) != null) return true;
        return false;
    }

    /// <summary>소수 격자 위치가 판에 속한 구역 위에 있는지 반환한다.</summary>
    private static bool OnGrid(Vector2 grid) { return SpellPatternLayout.Contains(grid); }

    /// <summary>칸 중심에서 포인터가 향한 상·우·하·좌 삼각 구역을 반환한다.</summary>
    private static int Sector(Vector2 grid)
    {
        float x = grid.x - Mathf.Floor(grid.x) - 0.5f, y = grid.y - Mathf.Floor(grid.y) - 0.5f;
        return Mathf.Abs(x) > Mathf.Abs(y) ? (x > 0 ? 1 : 3) : (y > 0 ? 2 : 0);
    }

    /// <summary>조각의 현재 물리 각도와 가장 가까운 배치 가능 방향으로 확대해 집는다.</summary>
    private void BeginPick(SpellPiece3D piece, Vector2 pointer)
    {
        _picked = piece; _pressPosition = pointer; _isHolding = true; _isClickSelected = false;
        float yaw = piece.transform.eulerAngles.y, nearest = float.PositiveInfinity;
        _rotation = (Mathf.RoundToInt(yaw / 90f) % 4 + 4) % 4;
        // 판에 놓인 조각은 메시가 방향을 담고 있으므로 놓인 방향 그대로 집는다.
        if (!piece.IsHand) { _layout.PieceToGrid(piece.Board, piece.Placement, out Vector2Int _, out _rotation); nearest = 0; }
        for (int r = 0; r < 4 && piece.IsHand; r++)
        {
            bool valid = false;
            foreach (int spell in _game.Spells) for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
            {
                _layout.GridToLocalPlacement(spell, piece.Card, new Vector2Int(x, y), r, out Vector2Int local, out int localRotation);
                if (_layout.IsPlaced(spell) && _game.CanPlace(spell, piece.Card, local.x, local.y, localRotation, piece.Board == spell ? piece.Placement : null)) valid = true;
            }
            float delta = Mathf.Abs(Mathf.DeltaAngle(yaw, r * 90f));
            if (valid && delta < nearest) { _rotation = r; nearest = delta; }
        }
        BeginPivot(piece, pointer);
        _picked.Pick(_cellSize, HeldCells(), HeldSeals(), Quaternion.identity); HideHover(pointer);
        _hasAutoCell = false; _isAutoRotateLocked = false;
        UpdateAutoRotation(pointer);
        PreviewPick(pointer);
    }

    /// <summary>피벗 점이 커서를 따라가는 격자 앵커를 찾고 배치 가능 여부를 조각 색과 위치로 표시한다.</summary>
    private void PreviewPick(Vector2 pointer)
    {
        _destination = -1;
        if (!ScreenToGrid(pointer, out Vector2 grid)) return;
        IReadOnlyList<SpellGame.ShapeCell> cells = HeldCells();
        // 회전해도 피벗 점의 격자 위치가 유지되도록 피벗 기준으로 앵커를 계산한다.
        _anchor = PivotAnchor(grid, _pivot);
        if (OnGrid(grid))
        {
            foreach (int spell in _game.Spells)
            {
                if (!_layout.IsPlaced(spell)) continue;
                _layout.GridToLocalPlacement(spell, _picked.Card, _anchor, _rotation, out Vector2Int local, out int localRotation);
                if (!_game.CanPlace(spell, _picked.Card, local.x, local.y, localRotation, _picked.Board == spell ? _picked.Placement : null)) continue;
                _destination = spell; _local = local; _localRotation = localRotation; break;
            }
            _picked.transform.position = PiecePosition(_anchor, cells, 0.045f) + Vector3.up * 0.22f;
        }
        else
        {
            // 판 밖에서는 피벗 점이 커서와 고정 차이를 유지하는 연속 위치에 둔다.
            Vector2 center = grid + _pivotOffset - _pivot + SpellPiece3D.CellBounds(cells) / 2;
            _picked.transform.position = _boardRoot.TransformPoint(GridLocal(center, 0.42f));
        }
        _picked.transform.rotation = Quaternion.identity;
        _picked.SetTint(_destination >= 0 ? new Color(0.55f, 0.95f, 0.68f) : new Color(1f, 0.5f, 0.4f));
    }

    /// <summary>집은 조각의 방향을 목표 방향으로 바꾸고, 피벗 점을 새 방향 좌표계로 옮긴 뒤 메시를 다시 만든다.</summary>
    private void SetPickRotation(int rotation)
    {
        int target = (rotation % 4 + 4) % 4;
        _pivot = RotatedPivot(target);
        _rotation = target;
        _picked.SetCells(HeldCells(), HeldSeals());
    }

    /// <summary>현재 방향의 피벗 점을 목표 방향 좌표계로 옮긴 값을 반환한다. 상태는 바꾸지 않는다.</summary>
    private Vector2 RotatedPivot(int targetRotation)
    {
        int step = (targetRotation - _rotation + 4) % 4;
        Vector2 pivot = _pivot;
        for (int i = 0; i < step; i++)
        {
            // 시계 방향 한 단계는 회전 전 범위 높이 H에서 점 (x, y)를 (H - y, x)로 옮긴다.
            float height = SpellPiece3D.CellBounds(CellsAt((_rotation + i) % 4)).y;
            pivot = new Vector2(height - pivot.y, pivot.x);
        }
        return pivot;
    }

    /// <summary>점토는 방향 0으로 고정한 HeldCells와 같은 기준으로 지정 방향의 칸 목록을 반환한다.</summary>
    private IReadOnlyList<SpellGame.ShapeCell> CellsAt(int rotation)
    {
        return SpellGame.CardCells(_picked.Card, _picked.Card.Glued == null && _picked.Card.Shape == SpellGame.Shape.Clay ? 0 : rotation);
    }

    /// <summary>조각 범위의 중심을 피벗 점으로 정한다. 커서가 가리키는 위치에 조각 중심이 가장 가까운 칸 경계로 맞춰진다.</summary>
    private static Vector2 PivotPoint(IReadOnlyList<SpellGame.ShapeCell> cells)
    {
        return SpellPiece3D.CellBounds(cells) / 2;
    }

    /// <summary>피벗 점이 커서 위치와 고정 차이만큼 떨어진 격자 지점에 오도록 지정 피벗의 정수 앵커를 반환한다.</summary>
    private Vector2Int PivotAnchor(Vector2 grid, Vector2 pivot)
    {
        Vector2 anchor = grid + _pivotOffset - pivot;
        return new Vector2Int(Mathf.RoundToInt(anchor.x), Mathf.RoundToInt(anchor.y));
    }

    /// <summary>집기 시작 방향이 정해진 뒤 피벗 점과 커서·피벗 차이를 정한다. 판 위 조각은 집는 순간 위치가 바뀌지 않게 차이를 보존하고, 손패 조각은 피벗이 커서에 오게 한다.</summary>
    private void BeginPivot(SpellPiece3D piece, Vector2 pointer)
    {
        _pivot = PivotPoint(HeldCells()); _pivotOffset = Vector2.zero;
        if (piece.IsHand || !ScreenToGrid(pointer, out Vector2 grid)) return;
        _layout.PieceToGrid(piece.Board, piece.Placement, out Vector2Int anchor, out _);
        _pivotOffset = anchor + _pivot - grid;
    }

    /// <summary>포인터가 판 위에 있으면 집고 있는 조각의 자동 회전을 판정하고, 판을 벗어나면 잠금을 해제한다.</summary>
    private void UpdateAutoRotation(Vector2 pointer)
    {
        if (!ScreenToGrid(pointer, out Vector2 grid) || !OnGrid(grid)) { _isAutoRotateLocked = _hasAutoCell = false; return; }
        if (!_isAutoRotateLocked) AutoRotatePick(grid);
    }

    /// <summary>커서가 가리키는 격자 칸이 직전 판정 칸과 달라진 경우에만 조각 방향을 다시 판정해 돌린다.</summary>
    private void AutoRotatePick(Vector2 grid)
    {
        if (_picked.Card.Glued == null && _picked.Card.Shape == SpellGame.Shape.Clay) return;
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(grid.x), Mathf.FloorToInt(grid.y));
        if (_hasAutoCell && _autoCell == cell) return;
        _autoCell = cell; _hasAutoCell = true;
        int rotation = AutoRotationTarget(grid);
        if (rotation == _rotation) return;
        SetPickRotation(rotation);
    }

    /// <summary>현재 방향부터 시계 방향으로 처음 초록인 방향을 찾고, 없으면 처음으로 판 밖·겹침이 아닌 방향을 반환한다.</summary>
    private int AutoRotationTarget(Vector2 grid)
    {
        int possible = -1;
        FillOverlapCounts();
        for (int i = 0; i < 4; i++)
        {
            int rotation = (_rotation + i) % 4;
            IReadOnlyList<SpellGame.ShapeCell> cells = SpellGame.CardCells(_picked.Card, rotation);
            Vector2Int anchor = PivotAnchor(grid, RotatedPivot(rotation));
            if (IsGreenPlacement(anchor, rotation)) return rotation;
            if (possible < 0 && IsFreePlacement(cells, anchor)) possible = rotation;
        }
        return possible < 0 ? _rotation : possible;
    }

    /// <summary>기존 PreviewPick과 같은 기준으로 장착 도안 하나에서 배치가 확정되는 초록 방향인지 반환한다.</summary>
    private bool IsGreenPlacement(Vector2Int anchor, int rotation)
    {
        foreach (int spell in _game.Spells)
        {
            if (!_layout.IsPlaced(spell)) continue;
            _layout.GridToLocalPlacement(spell, _picked.Card, anchor, rotation, out Vector2Int local, out int localRotation);
            if (_game.CanPlace(spell, _picked.Card, local.x, local.y, localRotation, _picked.Board == spell ? _picked.Placement : null)) return true;
        }
        return false;
    }

    /// <summary>조각의 모든 구역이 판 안에 있고 미리 집계한 점유 수 기준으로 겹치지 않는지 반환한다. 덧칠은 한 겹 겹침을 허용한다.</summary>
    private bool IsFreePlacement(IReadOnlyList<SpellGame.ShapeCell> cells, Vector2Int anchor)
    {
        bool isOverpaint = SpellGame.SealCount(_picked.Card, SpellGame.Seal.Overpaint) > 0;
        foreach (SpellGame.ShapeCell cell in cells)
        {
            int gx = anchor.x + cell.X, gy = anchor.y + cell.Y;
            if ((cell.Mask & ~SpellPatternLayout.BoardMask(gx, gy)) != 0) return false;
            int index = (gy * SpellPatternLayout.COLUMNS + gx) * 4;
            for (int s = 0; s < 4; s++)
            {
                if ((cell.Mask & (1 << s)) == 0) continue;
                if (_overlapCounts[index + s] >= (isOverpaint ? 2 : 1)) return false;
            }
        }
        return true;
    }

    /// <summary>장착 도안의 배치 조각별 격자 구역 점유 수를 재사용 배열에 다시 집계한다. 옮기는 중인 조각 자신은 제외한다.</summary>
    private void FillOverlapCounts()
    {
        System.Array.Clear(_overlapCounts, 0, _overlapCounts.Length);
        foreach (int spell in _game.Spells)
        {
            if (!_layout.IsPlaced(spell)) continue;
            foreach (SpellGame.Placement piece in _game.Boards[spell].Pieces)
            {
                if (!_picked.IsHand && piece == _picked.Placement) continue;
                List<SpellGame.ShapeCell> cells = _layout.PlacementGridCells(spell, piece);
                foreach (SpellGame.ShapeCell cell in cells)
                {
                    int index = (cell.Y * SpellPatternLayout.COLUMNS + cell.X) * 4;
                    for (int s = 0; s < 4; s++) if ((cell.Mask & (1 << s)) != 0) _overlapCounts[index + s]++;
                }
            }
        }
    }

    /// <summary>유효한 도안에는 배치·이동하고 손패 영역에는 회수한다. 실패하면 원위치로 복구한다.</summary>
    private void FinishPick(Vector2 pointer)
    {
        PreviewPick(pointer); SpellPiece3D piece = _picked; bool changed = false;
        if (_destination >= 0)
            changed = piece.IsHand ? _game.Place(_destination, piece.Card, _local.x, _local.y, _localRotation) : _game.Move(piece.Board, piece.Placement, _destination, _local.x, _local.y, _localRotation);
        else if (!piece.IsHand)
        {
            Ray ray = _camera.ScreenPointToRay(pointer); new Plane(Vector3.up, _handRoot.position).Raycast(ray, out float distance);
            Vector3 local = _handRoot.InverseTransformPoint(ray.GetPoint(distance));
            if (Mathf.Abs(local.x) < _handSurface.localScale.x * 0.5f && Mathf.Abs(local.z) < _handSurface.localScale.z * 0.5f) changed = _game.Recall(piece.Board, piece.Placement);
        }
        _picked = null; _isClickSelected = _isHolding = false;
        if (changed) _ui.TableChanged(); else RestorePiece(piece);
    }

    /// <summary>취소한 조각을 모델 위치로 복원하고 손패 조각은 예약 자리에서 다시 내려놓는다.</summary>
    private void RestorePiece(SpellPiece3D piece)
    {
        if (piece.IsHand) piece.DropToHand(piece.Slot, SlotPosition(piece.Slot), _cellSize, 0.25f);
        else PlaceOnBoard(piece, piece.Board, piece.Placement);
    }

    /// <summary>모델을 바꾸지 않고 진행 중인 집기를 취소하며 물리 상태를 복구한다.</summary>
    public void CancelPick()
    {
        if (_picked == null) return;
        SpellPiece3D piece = _picked; _picked = null; _isClickSelected = _isHolding = false; RestorePiece(piece);
    }

    /// <summary>기존 도안 드래그 입력의 스냅 후보를 3D 판 위의 유효·실패 색 외곽선으로 표시한다.</summary>
    private void RefreshTemplatePreview()
    {
        SpellBoardGraphic board = _screen.Board; int spell = board.PreviewPatternIndex;
        if (spell == _previewPattern && board.PreviewOrigin == _previewOrigin && board.PreviewRotation == _previewRotation) return;
        _previewPattern = spell; _previewOrigin = board.PreviewOrigin; _previewRotation = board.PreviewRotation;
        ClearGeometry(_preview);
        if (spell < 0) return;
        bool valid = _layout.MoveIssue(spell, _previewOrigin, _previewRotation, _game.Spells) == null;
        Material color = new Material(_lineMaterial) { color = valid ? new Color(0.4f, 0.95f, 0.6f) : new Color(1, 0.3f, 0.3f) };
        DrawPattern(_preview, spell, _previewOrigin, _previewRotation, color, color);
    }

    void OnDisable()
    {
        if (_pickAction != null) _pickAction.Disable();
        if (_rotateAction != null) _rotateAction.Disable();
        _requestedRotations = 0;
        _hasPress = _hasRelease = false;
        CancelPick(); if (_world != null) _world.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (_pickAction != null) _pickAction.Enable();
        if (_rotateAction != null) _rotateAction.Enable();
    }

    void OnDestroy()
    {
        if (_world != null) { ClearGeometry(_patterns); ClearGeometry(_preview); Destroy(_world.gameObject); }
        foreach (Material material in _materials) if (material != null) Destroy(material);
        if (_physicsMaterial != null) Destroy(_physicsMaterial);
        if (_pickAction != null) { _pickAction.performed -= RecordPress; _pickAction.canceled -= RecordRelease; _pickAction.Dispose(); }
        if (_rotateAction != null) { _rotateAction.performed -= RecordRotation; _rotateAction.Dispose(); }
    }
    /// <summary>판 격자 좌표를 카메라의 화면 좌표로 변환한다.</summary>
    public Vector2 GridToScreen(Vector2 grid)
    {
        return _camera.WorldToScreenPoint(_boardRoot.TransformPoint(GridLocal(grid)));
    }

    /// <summary>저주된 카드의 메시를 붉게 강조하고 해제된 카드의 원래 색상을 복원한다.</summary>
    public void RefreshCurseTint()
    {
        foreach (SpellPiece3D piece in _pieces.Values)
        {
            SpellGame.Card card = piece.Card;
            piece.SetTint(_game.IsCursed(card) ? new Color(1f, 0.15f, 0.15f) : PieceTint(card));
        }
    }

}

