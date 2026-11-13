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

    // 소멸 조각(회수 복사본) 윗면에 그리는 균열의 두께와 색이다.
    private const float CRACK_WIDTH = 0.028f;
    private const float CRACK_EDGE_MARGIN = 0.03f;
    private const float CRACK_LIFT = 0.008f;
    private static readonly Color CRACK_COLOR = new Color(0.07f, 0.06f, 0.06f);

    // 이전 턴에 놓여 고정된 조각의 표시다. 색을 이번 턴 조각보다 조금만 진하게 하고, 윗면에 어두운 받침 위로 금색 핀 머리를 박는다.
    private const float LOCKED_BRIGHTNESS = 0.8f;
    private const float LOCKED_SATURATION = 0.8f;
    private const float PIN_RADIUS = 0.15f;
    private const float PIN_TOP_RADIUS = 0.07f;
    private const float PIN_HEIGHT = 0.1f;
    private const float PIN_BASE_RADIUS = 0.21f;
    private const float PIN_BASE_HEIGHT = 0.006f;
    private const int PIN_SIDES = 24;
    private static readonly Color PIN_COLOR = new Color(1f, 0.8f, 0.3f);
    private static readonly Color PIN_BASE_COLOR = new Color(0.08f, 0.07f, 0.06f);
    private static Mesh _pinMesh, _pinBaseMesh;
    private MeshRenderer _pin, _pinBase;
    private bool _isLocked;
    private Color _tint;

    private readonly List<MeshRenderer> _sealDots = new List<MeshRenderer>();
    private MeshRenderer _crack;
    private Mesh _crackMesh;
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
        if (cells == null) { ReplaceMesh(CreateLump()); ShowSeals(System.Array.Empty<SpellGame.ShapeCell>(), System.Array.Empty<SpellGame.SealSpot>()); ShowCracks(null); }
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
        ShowCracks(_card != null && _card.IsRecallCopy ? cells : null);
        _clayKey = null;
    }

    /// <summary>소멸 조각이면 윗면에 균열을 그리고, cells가 null이면 균열을 숨긴다. 균열은 카드마다 같은 모양이다.</summary>
    private void ShowCracks(IReadOnlyList<SpellGame.ShapeCell> cells)
    {
        if (cells == null || cells.Count == 0)
        {
            if (_crack != null) _crack.gameObject.SetActive(false);
            return;
        }
        if (_crack == null) _crack = CreateCrackRenderer("Crack", CRACK_COLOR);
        List<CrackLine> lines = CrackLines(cells, new System.Random(_card.Id * 7919 + 17));
        ReplaceCrackMesh(ref _crackMesh, _crack, CrackMesh(lines, CellBounds(cells) / 2, THICKNESS + CRACK_LIFT));
        _crack.gameObject.SetActive(true);
    }

    /// <summary>균열을 그릴 자식 렌더러를 씰링 재질 사본에 지정 색을 입혀 만든다.</summary>
    private MeshRenderer CreateCrackRenderer(string name, Color color)
    {
        GameObject crack = new GameObject(name) { layer = gameObject.layer };
        crack.transform.SetParent(transform, false);
        crack.AddComponent<MeshFilter>();
        MeshRenderer renderer = crack.AddComponent<MeshRenderer>();
        Material material = new Material(_sealMaterial);
        material.SetColor(BASE_COLOR_ID, color); material.SetColor(COLOR_ID, color);
        // 씰링 재질의 반사가 남으면 어두운 균열이 번들거려 보이므로 끈다.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    /// <summary>균열 메시를 교체하고 이전 메시를 정리한다.</summary>
    private static void ReplaceCrackMesh(ref Mesh field, MeshRenderer renderer, Mesh mesh)
    {
        if (field != null) Destroy(field);
        field = mesh; renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    /// <summary>균열 한 줄기다. 점마다 두께가 있어 갈라진 곳은 굵고 끝으로 갈수록 가늘어진다.</summary>
    private struct CrackLine
    {
        public List<Vector2> Points;
        public List<float> Widths;
    }

    /// <summary>조각 전체에 퍼진 가지 모양 균열을 만든다. 넓이에 비례한 여러 갈라짐 지점에서 구불구불한 줄기가 뻗고, 줄기에서 다시 가는 잔가지가 갈라진다. 조각 밖으로 나가면 그 줄기를 멈춘다.</summary>
    private static List<CrackLine> CrackLines(IReadOnlyList<SpellGame.ShapeCell> cells, System.Random random)
    {
        List<CrackLine> lines = new List<CrackLine>();
        List<Vector2> centroids = new List<Vector2>();
        foreach (SpellGame.ShapeCell cell in cells)
            for (int s = 0; s < 4; s++)
            {
                if ((cell.Mask & (1 << s)) == 0) continue;
                Vector2[] tri = Triangle(cell, s);
                centroids.Add((tri[0] + tri[1] + tri[2]) / 3);
            }
        if (centroids.Count == 0) return lines;
        // 갈라짐 지점을 넓이에 맞춰 여러 곳에 고르게 흩어 조각 전체에 균열이 보이게 한다.
        float area = centroids.Count / 4f;
        int seeds = Mathf.Clamp(Mathf.RoundToInt(area * 1.6f), 2, 8);
        for (int i = centroids.Count - 1; i > 0; i--) { int j = random.Next(i + 1); Vector2 t = centroids[i]; centroids[i] = centroids[j]; centroids[j] = t; }
        List<Vector2> chosen = new List<Vector2>();
        foreach (Vector2 c in centroids)
        {
            bool far = true; foreach (Vector2 o in chosen) if ((o - c).sqrMagnitude < 0.2f) far = false;
            if (far) chosen.Add(c);
            if (chosen.Count >= seeds) break;
        }
        foreach (Vector2 seed in chosen)
        {
            int arms = 3 + random.Next(2);
            float start = (float)random.NextDouble() * Mathf.PI * 2;
            for (int arm = 0; arm < arms; arm++)
            {
                float angle = start + arm * Mathf.PI * 2 / arms + ((float)random.NextDouble() - 0.5f) * 0.9f;
                GrowCrack(lines, cells, random, seed, angle, 0.45f + 0.4f * (float)random.NextDouble(), CRACK_WIDTH, 0);
            }
        }
        return lines;
    }

    /// <summary>from에서 angle 방향으로 length만큼 구불구불하게 뻗는 줄기를 만들고, 도중에 가는 잔가지를 낸다. 끝으로 갈수록 가늘어진다.</summary>
    private static void GrowCrack(List<CrackLine> lines, IReadOnlyList<SpellGame.ShapeCell> cells, System.Random random, Vector2 from, float angle, float length, float width, int depth)
    {
        const float STEP = 0.035f;
        int steps = Mathf.Max(2, Mathf.RoundToInt(length / STEP));
        CrackLine line = new CrackLine { Points = new List<Vector2> { from }, Widths = new List<float> { width } };
        Vector2 point = from;
        for (int i = 1; i <= steps; i++)
        {
            angle += ((float)random.NextDouble() - 0.5f) * 0.55f;
            Vector2 next = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * STEP;
            if (!InsidePiece(cells, next, CRACK_EDGE_MARGIN)) break;
            point = next;
            float t = i / (float)steps;
            float w = width * Mathf.Pow(1 - t, 0.75f);
            line.Points.Add(point); line.Widths.Add(w);
            // 잔가지는 줄기 중간에서 비스듬히 갈라져 더 짧고 가늘게 뻗는다.
            if (depth < 2 && i > 2 && i < steps - 1 && random.NextDouble() < 0.11)
            {
                float side = random.NextDouble() < 0.5 ? -1 : 1;
                GrowCrack(lines, cells, random, point, angle + side * (0.5f + 0.6f * (float)random.NextDouble()), length * (1 - t) * (0.5f + 0.3f * (float)random.NextDouble()), w * 0.8f, depth + 1);
            }
        }
        if (line.Points.Count >= 2) { line.Widths[line.Widths.Count - 1] = 0; lines.Add(line); }
    }

    /// <summary>격자 점이 조각 모양 안쪽(가장자리에서 margin 이상 떨어진 곳)에 있는지 반환한다.</summary>
    private static bool InsidePiece(IReadOnlyList<SpellGame.ShapeCell> cells, Vector2 point, float margin)
    {
        return InsideSector(cells, point) && InsideSector(cells, point + new Vector2(margin, margin)) && InsideSector(cells, point + new Vector2(-margin, margin))
            && InsideSector(cells, point + new Vector2(margin, -margin)) && InsideSector(cells, point + new Vector2(-margin, -margin));
    }

    /// <summary>격자 점이 놓인 칸의 1/4 구역이 조각에 포함되는지 반환한다.</summary>
    private static bool InsideSector(IReadOnlyList<SpellGame.ShapeCell> cells, Vector2 point)
    {
        int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
        float dx = point.x - x - 0.5f, dy = point.y - y - 0.5f;
        int sector = Mathf.Abs(dy) >= Mathf.Abs(dx) ? (dy < 0 ? 0 : 2) : (dx > 0 ? 1 : 3);
        return (MaskAt(cells, x, y) & (1 << sector)) != 0;
    }

    /// <summary>칸의 구역 삼각형 꼭짓점(바깥 변 두 점, 칸 중심)을 반환한다.</summary>
    private static Vector2[] Triangle(SpellGame.ShapeCell cell, int sector)
    {
        Vector2[] corners = { new Vector2(cell.X, cell.Y), new Vector2(cell.X + 1, cell.Y), new Vector2(cell.X + 1, cell.Y + 1), new Vector2(cell.X, cell.Y + 1) };
        return new[] { corners[sector], corners[(sector + 1) % 4], new Vector2(cell.X + 0.5f, cell.Y + 0.5f) };
    }

    /// <summary>균열 줄기들을 윗면 높이의 띠 메시로 만든다. 점마다 두께가 달라 끝이 뾰족하게 가늘어진다.</summary>
    private static Mesh CrackMesh(List<CrackLine> lines, Vector2 half, float height)
    {
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        foreach (CrackLine line in lines)
        {
            int count = line.Points.Count, start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 tangent = (line.Points[Mathf.Min(i + 1, count - 1)] - line.Points[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x) * line.Widths[i] / 2;
                vertices.Add(Local(line.Points[i] + normal, half, height)); vertices.Add(Local(line.Points[i] - normal, half, height));
            }
            for (int i = 0; i < count - 1; i++)
            {
                int a = start + i * 2;
                // 위에서 보이도록 감기 순서를 윗방향에 맞춘다.
                Vector3 p0 = vertices[a], p1 = vertices[a + 1], p2 = vertices[a + 2];
                bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), Vector3.up) < 0;
                if (!flip) { triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2); }
                else { triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1); triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3); }
            }
        }
        Mesh mesh = new Mesh { name = "Spell Piece Crack" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
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
        SetLocked(false, null, null);
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

    /// <summary>현재 조각의 물리 표시 색을 지정 색으로 갱신한다. 고정 조각은 어둡고 탁하게 낮춘다.</summary>
    public void SetTint(Color color)
    {
        _tint = color;
        if (_isLocked)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            color = Color.HSVToRGB(h, s * LOCKED_SATURATION, v * LOCKED_BRIGHTNESS);
        }
        _material.color = color.linear * 0.65f;
    }

    /// <summary>이전 턴에 놓여 움직일 수 없는 조각인지 표시한다. 고정이면 색을 낮추고 cells 윗면에 핀을 박는다.</summary>
    public void SetLocked(bool locked, IReadOnlyList<SpellGame.ShapeCell> cells, IReadOnlyList<SpellGame.SealSpot> seals)
    {
        bool changed = locked != _isLocked;
        _isLocked = locked;
        if (changed) SetTint(_tint);
        if (!locked || cells == null || cells.Count == 0)
        {
            if (_pin != null) { _pin.gameObject.SetActive(false); _pinBase.gameObject.SetActive(false); }
            return;
        }
        if (_pin == null)
        {
            _pinBase = CreatePinPart("Locked Pin Base", PinBaseMesh(), PIN_BASE_COLOR, 0f);
            _pin = CreatePinPart("Locked Pin", PinMesh(), PIN_COLOR, 0.35f);
        }
        Vector3 point = Local(PinPoint(cells, seals), CellBounds(cells) / 2, THICKNESS);
        _pinBase.transform.localPosition = point;
        _pin.transform.localPosition = point + Vector3.up * PIN_BASE_HEIGHT;
        _pinBase.gameObject.SetActive(true); _pin.gameObject.SetActive(true);
    }

    /// <summary>핀의 한 부분(받침·머리)을 씰링 재질 사본에 지정 색을 입혀 자식으로 만든다. 금속성이 높으면 반사할 환경이 없어 어둡게 보이므로 낮게 둔다.</summary>
    private MeshRenderer CreatePinPart(string name, Mesh mesh, Color color, float metallic)
    {
        GameObject part = new GameObject(name) { layer = gameObject.layer };
        part.transform.SetParent(transform, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        Material material = new Material(_sealMaterial);
        material.SetColor(BASE_COLOR_ID, color); material.SetColor(COLOR_ID, color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.55f);
        renderer.sharedMaterial = material;
        return renderer;
    }

    /// <summary>핀 머리 아래 깔리는 어두운 납작한 원판 메시를 처음 한 번 만든다. 밝은 조각 위에서 핀 윤곽을 또렷하게 한다.</summary>
    private static Mesh PinBaseMesh()
    {
        if (_pinBaseMesh != null) return _pinBaseMesh;
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        Vector3 up = Vector3.up * PIN_BASE_HEIGHT;
        for (int i = 0; i < PIN_SIDES; i++)
        {
            float a0 = i * Mathf.PI * 2 / PIN_SIDES, a1 = (i + 1) * Mathf.PI * 2 / PIN_SIDES;
            Vector3 p = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * PIN_BASE_RADIUS, q = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * PIN_BASE_RADIUS;
            AddTriangle(vertices, triangles, p + up, q + up, up, Vector3.up);
        }
        _pinBaseMesh = new Mesh { name = "Spell Locked Pin Base" };
        _pinBaseMesh.SetVertices(vertices); _pinBaseMesh.SetTriangles(triangles, 0); _pinBaseMesh.RecalculateNormals(); _pinBaseMesh.RecalculateBounds();
        return _pinBaseMesh;
    }

    /// <summary>조각 전체 중심에 가장 가까운 1/4 구역 중심을 핀 위치로 고른다. 씰링이 있는 구역은 가능하면 피한다.</summary>
    private static Vector2 PinPoint(IReadOnlyList<SpellGame.ShapeCell> cells, IReadOnlyList<SpellGame.SealSpot> seals)
    {
        List<Vector2> points = new List<Vector2>(); List<bool> sealed_ = new List<bool>();
        Vector2 sum = Vector2.zero;
        foreach (SpellGame.ShapeCell cell in cells)
        {
            Vector2 center = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
            Vector2[] corners = { new Vector2(cell.X, cell.Y), new Vector2(cell.X + 1, cell.Y), new Vector2(cell.X + 1, cell.Y + 1), new Vector2(cell.X, cell.Y + 1) };
            for (int sector = 0; sector < 4; sector++)
            {
                if ((cell.Mask & (1 << sector)) == 0) continue;
                Vector2 point = (corners[sector] + corners[(sector + 1) % 4] + center) / 3;
                bool hasSeal = false;
                if (seals != null) foreach (SpellGame.SealSpot seal in seals) if (seal.X == cell.X && seal.Y == cell.Y && seal.Sector == sector) hasSeal = true;
                points.Add(point); sealed_.Add(hasSeal); sum += point;
            }
        }
        if (points.Count == 0) return CellBounds(cells) / 2;
        Vector2 middle = sum / points.Count;
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float score = (points[i] - middle).sqrMagnitude + (sealed_[i] ? 100f : 0f);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return points[best];
    }

    /// <summary>모든 조각이 함께 쓰는 낮은 원뿔대 모양 핀 머리 메시를 처음 한 번 만든다. 납작한 씰링 동그라미와 구분되도록 위가 좁다.</summary>
    private static Mesh PinMesh()
    {
        if (_pinMesh != null) return _pinMesh;
        List<Vector3> vertices = new List<Vector3>(); List<int> triangles = new List<int>();
        Vector3 top = Vector3.up * PIN_HEIGHT;
        for (int i = 0; i < PIN_SIDES; i++)
        {
            float a0 = i * Mathf.PI * 2 / PIN_SIDES, a1 = (i + 1) * Mathf.PI * 2 / PIN_SIDES;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Vector3 p = d0 * PIN_RADIUS, q = d1 * PIN_RADIUS, pt = d0 * PIN_TOP_RADIUS + top, qt = d1 * PIN_TOP_RADIUS + top;
            Vector3 side = ((d0 + d1).normalized * PIN_HEIGHT + Vector3.up * (PIN_RADIUS - PIN_TOP_RADIUS)).normalized;
            AddTriangle(vertices, triangles, p, q, qt, side); AddTriangle(vertices, triangles, p, qt, pt, side);
            AddTriangle(vertices, triangles, pt, qt, top, Vector3.up);
        }
        _pinMesh = new Mesh { name = "Spell Locked Pin" };
        _pinMesh.SetVertices(vertices); _pinMesh.SetTriangles(triangles, 0); _pinMesh.RecalculateNormals(); _pinMesh.RecalculateBounds();
        return _pinMesh;
    }

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
        if (_crackMesh != null) Destroy(_crackMesh);
        if (_crack != null) Destroy(_crack.sharedMaterial);
        if (_pin != null) { Destroy(_pin.sharedMaterial); Destroy(_pinBase.sharedMaterial); }
    }
}

