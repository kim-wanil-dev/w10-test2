using System.Collections.Generic;

using UnityEngine;

/// <summary>조각 하나의 입체 메시, 씰링 동그라미, 물리 낙하와 집기·격자 고정 상태를 표시한다.</summary>
public sealed class SpellPiece3D : MonoBehaviour
{
    // 칸 한 변 대비 두께다. 칸이 화면 정사각형(이전 마름모 칸의 1/√2)이라 같은 실제 두께가 되도록 맞춘 값이다.
    private const float THICKNESS = 0.23f;
    // 점토 원의 반지름이다. 화면 마름모(Square)에 내접하는 원 크기(칸 한 변의 √2/2)다.
    private const float CLAY_RADIUS = 0.7071f;
    private const int CLAY_SIDES = 32;
    private const string LUMP_KEY = "lump";
    private const float SEAL_RADIUS = 0.1f;
    private const float SEAL_HEIGHT = 0.03f;
    private const int SEAL_SIDES = 16;
    private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
    private static readonly int COLOR_ID = Shader.PropertyToID("_Color");
    private static Mesh _sealMesh;

    private readonly List<MeshRenderer> _sealDots = new List<MeshRenderer>();
    private MaterialPropertyBlock _sealBlock;
    private Material _sealMaterial;
    private System.Func<SpellGame.Seal, Color> _sealColor;
    private Rigidbody _body;
    private MeshCollider _collider;
    private MeshRenderer _renderer;
    private MeshFilter _filter;
    private Mesh _mesh;
    private Material _material;
    private Color _color;
    private string _clayKey;
    private SpellGame.Card _card;
    private SpellGame.Placement _placement;
    private int _board = -1;
    private int _slot = -1;
    private bool _isHand;

    public SpellGame.Card Card { get { return _card; } }
    public SpellGame.Placement Placement { get { return _placement; } }
    public int Board { get { return _board; } }
    public int Slot { get { return _slot; } }
    public bool IsHand { get { return _isHand; } }
    public Rigidbody Body { get { return _body; } }
    public static float Thickness { get { return THICKNESS; } }

    /// <summary>카드 모양으로 입체 메시와 볼록 충돌체, 강체 및 색을 초기화한다. material은 코팅별 Material이고, 씰링 동그라미는 sealMaterial에 sealColor 색을 입혀 그린다.</summary>
    public void Setup(SpellGame.Card card, Material material, PhysicsMaterial physicsMaterial, Color color, Material sealMaterial, System.Func<SpellGame.Seal, Color> sealColor)
    {
        _card = card; _color = color; _sealMaterial = sealMaterial; _sealColor = sealColor; _sealBlock = new MaterialPropertyBlock();
        _filter = gameObject.AddComponent<MeshFilter>();
        _renderer = gameObject.AddComponent<MeshRenderer>();
        _material = new Material(material); _renderer.sharedMaterial = _material;
        SetTint(color);
        _collider = gameObject.AddComponent<MeshCollider>(); _collider.convex = true;
        _collider.sharedMaterial = physicsMaterial;
        SetHandMesh();
        _body = gameObject.AddComponent<Rigidbody>(); _body.mass = 0.35f;
        _body.interpolation = RigidbodyInterpolation.Interpolate;
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _body.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    /// <summary>손패에서 보일 기본 메시로 바꾼다. 점토는 원판으로 표시한다.</summary>
    private void SetHandMesh()
    {
        if (_card.IsUnique) ShowClay(null);
        else SetCells(SpellGame.CardCells(_card, 0), SpellGame.SealSpots(_card, 0));
    }

    /// <summary>점토를 cells 모양으로 바꾸고, cells가 null이면 원판으로 되돌린다. 모양이 같으면 메시를 다시 만들지 않는다.</summary>
    public void ShowClay(IReadOnlyList<SpellGame.ShapeCell> cells)
    {
        string key = LUMP_KEY;
        if (cells != null) { System.Text.StringBuilder builder = new System.Text.StringBuilder(); foreach (SpellGame.ShapeCell cell in cells) builder.Append(cell.X).Append(',').Append(cell.Y).Append(',').Append(cell.Mask).Append('|'); key = builder.ToString(); }
        if (key == _clayKey) return;
        if (cells == null) { ReplaceMesh(CreateLump()); ShowSeals(System.Array.Empty<SpellGame.ShapeCell>(), System.Array.Empty<SpellGame.SealSpot>()); }
        else SetCells(cells, System.Array.Empty<SpellGame.SealSpot>());
        _clayKey = key;
    }

    /// <summary>점토 손패용 원판 메시를 만든다.</summary>
    private static Mesh CreateLump()
    {
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        Vector2 half = new Vector2(0.5f, 0.5f), center = half;
        for (int i = 0; i < CLAY_SIDES; i++)
        {
            float a0 = i * Mathf.PI * 2 / CLAY_SIDES, a1 = (i + 1) * Mathf.PI * 2 / CLAY_SIDES;
            Vector2 p = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * CLAY_RADIUS, q = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * CLAY_RADIUS;
            AddTriangle(vertices, triangles, Local(p, half, THICKNESS), Local(q, half, THICKNESS), Local(center, half, THICKNESS), Vector3.up);
            AddTriangle(vertices, triangles, Local(p, half, 0), Local(q, half, 0), Local(center, half, 0), Vector3.down);
            AddWall(vertices, triangles, p, q, center, half, THICKNESS);
        }
        Mesh mesh = new Mesh { name = "Spell Clay Lump" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>범위 왼쪽 위가 (0, 0)인 칸 목록으로 메시를 다시 만들고 같은 좌표의 씰링 동그라미를 윗면에 올린다. 메시 중심은 범위 중심이다.</summary>
    public void SetCells(IReadOnlyList<SpellGame.ShapeCell> cells, IReadOnlyList<SpellGame.SealSpot> seals)
    {
        ReplaceMesh(CreateSectorPrism(cells, THICKNESS));
        ShowSeals(cells, seals);
        _clayKey = null;
    }

    /// <summary>씰링마다 해당 1/4 구역 무게중심 위에 색 동그라미를 둔다. 필요한 만큼만 자식을 만들고 남는 동그라미는 숨긴다.</summary>
    private void ShowSeals(IReadOnlyList<SpellGame.ShapeCell> cells, IReadOnlyList<SpellGame.SealSpot> seals)
    {
        Vector2 half = CellBounds(cells) / 2;
        while (_sealDots.Count < seals.Count)
        {
            GameObject dot = new GameObject("Seal " + _sealDots.Count) { layer = gameObject.layer };
            dot.transform.SetParent(transform, false);
            dot.AddComponent<MeshFilter>().sharedMesh = SealMesh();
            MeshRenderer dotRenderer = dot.AddComponent<MeshRenderer>(); dotRenderer.sharedMaterial = _sealMaterial;
            _sealDots.Add(dotRenderer);
        }
        for (int i = 0; i < _sealDots.Count; i++)
        {
            bool show = i < seals.Count;
            _sealDots[i].gameObject.SetActive(show);
            if (!show) continue;
            SpellGame.SealSpot seal = seals[i];
            Vector2 center = new Vector2(seal.X + 0.5f, seal.Y + 0.5f);
            Vector2[] corners = { new Vector2(seal.X, seal.Y), new Vector2(seal.X + 1, seal.Y), new Vector2(seal.X + 1, seal.Y + 1), new Vector2(seal.X, seal.Y + 1) };
            Vector2 centroid = (corners[seal.Sector] + corners[(seal.Sector + 1) % 4] + center) / 3;
            _sealDots[i].transform.localPosition = Local(centroid, half, THICKNESS);
            Color color = _sealColor(seal.Seal);
            _sealBlock.SetColor(BASE_COLOR_ID, color); _sealBlock.SetColor(COLOR_ID, color);
            _sealDots[i].SetPropertyBlock(_sealBlock);
        }
    }

    /// <summary>모든 조각이 함께 쓰는 낮은 원기둥 모양 씰링 메시를 처음 한 번 만든다.</summary>
    private static Mesh SealMesh()
    {
        if (_sealMesh != null) return _sealMesh;
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        for (int i = 0; i < SEAL_SIDES; i++)
        {
            float a0 = i * Mathf.PI * 2 / SEAL_SIDES, a1 = (i + 1) * Mathf.PI * 2 / SEAL_SIDES;
            Vector3 p = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * SEAL_RADIUS, q = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * SEAL_RADIUS;
            Vector3 up = Vector3.up * SEAL_HEIGHT;
            AddTriangle(vertices, triangles, p + up, q + up, up, Vector3.up);
            AddTriangle(vertices, triangles, p, q, p + up, (p + q).normalized); AddTriangle(vertices, triangles, q, q + up, p + up, (p + q).normalized);
        }
        _sealMesh = new Mesh { name = "Spell Seal Dot" };
        _sealMesh.SetVertices(vertices); _sealMesh.SetTriangles(triangles, 0); _sealMesh.RecalculateNormals(); _sealMesh.RecalculateBounds();
        return _sealMesh;
    }

    /// <summary>표시·충돌 메시를 교체하고 이전 메시를 정리한다.</summary>
    private void ReplaceMesh(Mesh mesh)
    {
        Mesh previous = _mesh; _mesh = mesh;
        _filter.sharedMesh = mesh; _collider.sharedMesh = mesh;
        if (previous != null) Destroy(previous);
    }

    /// <summary>손패 위치와 축소 비율을 설정하고 실제 중력으로 낙하·반동하게 한다.</summary>
    public void DropToHand(int slot, Vector3 position, float scale, float height)
    {
        _isHand = true; _board = -1; _placement = null; _slot = slot;
        gameObject.SetActive(true); _collider.enabled = true;
        _body.isKinematic = true;
        SetHandMesh();
        transform.SetPositionAndRotation(position + Vector3.up * height, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
        transform.localScale = Vector3.one * scale;
        _body.isKinematic = false; _body.useGravity = true;
        _body.linearVelocity = Vector3.down * 0.4f;
        _body.angularVelocity = Vector3.up * Random.Range(-1.2f, 1.2f);
        SetTint(_color);
    }

    /// <summary>논리 배치의 격자 칸 모양·씰링과 위치, 판 격자 방향(rotation)에 조각을 고정하고 물리 이동을 멈춘다.</summary>
    public void PlaceOnBoard(int board, SpellGame.Placement placement, IReadOnlyList<SpellGame.ShapeCell> cells, IReadOnlyList<SpellGame.SealSpot> seals, Vector3 position, Quaternion rotation, float scale)
    {
        _isHand = false; _slot = -1; _board = board; _placement = placement;
        gameObject.SetActive(true); _body.isKinematic = true; _collider.enabled = true;
        SetCells(cells, seals);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = Vector3.one * scale;
        SetTint(_color);
    }

    /// <summary>집은 조각을 격자 크기와 지정 칸 모양·씰링, 판 격자 방향(rotation)으로 바꾸고 충돌·중력을 잠시 중단한다.</summary>
    public void Pick(float scale, IReadOnlyList<SpellGame.ShapeCell> cells, IReadOnlyList<SpellGame.SealSpot> seals, Quaternion rotation)
    {
        _body.isKinematic = true; _collider.enabled = false;
        if (_card.IsUnique) ShowClay(null); else SetCells(cells, seals);
        transform.localScale = Vector3.one * scale;
        transform.rotation = rotation;
    }

    /// <summary>현재 조각의 물리 표시 색을 지정 색으로 갱신한다.</summary>
    public void SetTint(Color color) { _material.color = color.linear * 0.65f; }

    /// <summary>칸 목록의 범위 크기를 칸 단위로 반환한다.</summary>
    public static Vector2 CellBounds(IReadOnlyList<SpellGame.ShapeCell> cells)
    {
        Vector2 size = Vector2.zero;
        foreach (SpellGame.ShapeCell cell in cells) size = Vector2.Max(size, new Vector2(cell.X + 1, cell.Y + 1));
        return size;
    }

    /// <summary>1/4 구역 삼각형들을 지정 두께의 입체로 만들고 같은 조각끼리 맞닿은 안쪽 벽은 생략한다.</summary>
    public static Mesh CreateSectorPrism(IReadOnlyList<SpellGame.ShapeCell> cells, float thickness)
    {
        Vector2 size = CellBounds(cells), half = size / 2;
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        foreach (SpellGame.ShapeCell cell in cells)
        {
            Vector2[] corners = { new Vector2(cell.X, cell.Y), new Vector2(cell.X + 1, cell.Y), new Vector2(cell.X + 1, cell.Y + 1), new Vector2(cell.X, cell.Y + 1) };
            Vector2 center = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((cell.Mask & (1 << sector)) == 0) continue;
                Vector2 a = corners[sector], b = corners[(sector + 1) % 4];
                Vector2 centroid = (a + b + center) / 3;
                AddTriangle(vertices, triangles, Local(a, half, thickness), Local(b, half, thickness), Local(center, half, thickness), Vector3.up);
                AddTriangle(vertices, triangles, Local(a, half, 0), Local(b, half, 0), Local(center, half, 0), Vector3.down);
                // 바깥 변은 옆 칸의 맞은편 구역, 두 안쪽 변은 같은 칸의 이웃 구역과 맞닿는다.
                int nx = cell.X + (sector == 1 ? 1 : sector == 3 ? -1 : 0), ny = cell.Y + (sector == 2 ? 1 : sector == 0 ? -1 : 0);
                if ((MaskAt(cells, nx, ny) & (1 << ((sector + 2) % 4))) == 0) AddWall(vertices, triangles, a, b, centroid, half, thickness);
                if ((cell.Mask & (1 << ((sector + 1) % 4))) == 0) AddWall(vertices, triangles, b, center, centroid, half, thickness);
                if ((cell.Mask & (1 << ((sector + 3) % 4))) == 0) AddWall(vertices, triangles, center, a, centroid, half, thickness);
            }
        }
        Mesh mesh = new Mesh { name = "Spell Piece Sectors" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>칸 목록에서 지정 칸의 구역 마스크를 찾는다.</summary>
    private static int MaskAt(IReadOnlyList<SpellGame.ShapeCell> cells, int x, int y)
    {
        foreach (SpellGame.ShapeCell cell in cells) if (cell.X == x && cell.Y == y) return cell.Mask;
        return 0;
    }

    /// <summary>격자 좌표(y 아래)를 범위 중심 기준 로컬 좌표(z 위)로 바꾼다.</summary>
    private static Vector3 Local(Vector2 point, Vector2 half, float height)
    {
        return new Vector3(point.x - half.x, height, -(point.y - half.y));
    }

    /// <summary>지정 방향을 향하도록 감기 순서를 맞춰 삼각형을 추가한다.</summary>
    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
    {
        int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
        bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0;
        triangles.Add(start); triangles.Add(flip ? start + 2 : start + 1); triangles.Add(flip ? start + 1 : start + 2);
    }

    /// <summary>두 점 사이 변에 구역 중심 반대쪽을 향하는 옆벽을 추가한다.</summary>
    private static void AddWall(List<Vector3> vertices, List<int> triangles, Vector2 from, Vector2 to, Vector2 centroid, Vector2 half, float thickness)
    {
        Vector3 a = Local(from, half, 0), b = Local(to, half, 0), c = Local(to, half, thickness), d = Local(from, half, thickness);
        Vector3 inside = Local(centroid, half, thickness / 2), middle = (a + c) / 2, outward = middle - inside; outward.y = 0;
        AddTriangle(vertices, triangles, a, b, c, outward); AddTriangle(vertices, triangles, a, c, d, outward);
    }

    void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
    }
}

