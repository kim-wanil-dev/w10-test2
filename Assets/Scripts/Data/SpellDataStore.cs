using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>도안·조각 JSON의 읽기, 쓰기, 정규화와 검증을 런타임과 에디터에 함께 제공한다.</summary>
public static class SpellDataStore
{
    public const int CURRENT_VERSION = 1;
    public const int GRID_SIZE = SpellGame.LOCAL_SIZE;
    public const int FULL_MASK = 15;
    public const string PATTERNS_RESOURCE = "SpellData/patterns";
    public const string PIECES_RESOURCE = "SpellData/pieces";
    public const string PATTERNS_ASSET_PATH = "Assets/Resources/SpellData/patterns.json";
    public const string PIECES_ASSET_PATH = "Assets/Resources/SpellData/pieces.json";
    private const int COLOR_CODE_LENGTH = 7;

    public static readonly JsonSerializerSettings SETTINGS = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() },
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    /// <summary>Resources의 patterns.json을 읽어 파싱·검증한 도안 표를 반환한다. 파일이 없거나 오류가 있으면 InvalidDataException을 던진다.</summary>
    public static SpellPatternTable LoadPatterns()
    {
        SpellPatternTable table = ParsePatterns(ReadResource(PATTERNS_RESOURCE));
        List<string> errors = new List<string>();
        ValidatePatterns(table, errors, null);
        ThrowIfInvalid(PATTERNS_RESOURCE, errors);
        return table;
    }

    /// <summary>Resources의 pieces.json을 읽어 파싱·검증한 조각 표를 반환한다. 파일이 없거나 오류가 있으면 InvalidDataException을 던진다.</summary>
    public static SpellPieceTable LoadPieces()
    {
        SpellPieceTable table = ParsePieces(ReadResource(PIECES_RESOURCE));
        List<string> errors = new List<string>();
        ValidatePieces(table, errors, null);
        ThrowIfInvalid(PIECES_RESOURCE, errors);
        return table;
    }

    /// <summary>JSON 문자열을 도안 표로 파싱한다. 형식 오류는 JsonException으로 전달된다.</summary>
    public static SpellPatternTable ParsePatterns(string json)
    {
        return JsonConvert.DeserializeObject<SpellPatternTable>(json, SETTINGS) ?? throw new InvalidDataException("도안 JSON이 비어 있습니다");
    }

    /// <summary>JSON 문자열을 조각 표로 파싱한다. 형식 오류는 JsonException으로 전달된다.</summary>
    public static SpellPieceTable ParsePieces(string json)
    {
        return JsonConvert.DeserializeObject<SpellPieceTable>(json, SETTINGS) ?? throw new InvalidDataException("조각 JSON이 비어 있습니다");
    }

    /// <summary>공용 설정으로 객체를 JSON 문자열로 만든다. 줄바꿈은 LF로 맞추고 마지막에 줄바꿈 하나를 붙인다.</summary>
    public static string Serialize(object value)
    {
        return JsonConvert.SerializeObject(value, SETTINGS).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>JSON 왕복으로 데이터 객체의 깊은 복사본을 만든다.</summary>
    public static T Clone<T>(T value)
    {
        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value, SETTINGS), SETTINGS);
    }

    /// <summary>칸 목록을 최소 x·y가 0이 되도록 옮기고 (y, x) 순으로 정렬한다. 목록을 직접 변경한다.</summary>
    public static void Normalize(List<SpellCellData> cells)
    {
        if (cells.Count == 0) return;
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (SpellCellData cell in cells) { minX = Math.Min(minX, cell.X); minY = Math.Min(minY, cell.Y); }
        foreach (SpellCellData cell in cells) { cell.X -= minX; cell.Y -= minY; }
        cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
    }

    /// <summary>"#RRGGBB" 형식 색 코드를 Color로 바꾼다. 형식이 다르면 false를 반환한다.</summary>
    public static bool ParseColor(string code, out Color color)
    {
        color = Color.white;
        return code != null && code.Length == COLOR_CODE_LENGTH && code[0] == '#' && ColorUtility.TryParseHtmlString(code, out color);
    }

    /// <summary>Color를 알파를 뺀 "#RRGGBB" 형식 색 코드로 바꾼다.</summary>
    public static string ToColorCode(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    /// <summary>도안 이름과 효과를 게임 화면 표기로 반환한다. 첫 줄은 "이름 - 기본 효과", 키워드 보상이 있으면 둘째 줄에 "태그: 보상"을 붙인다. 예: "방패 밀치기 - 방어 9\n순수: 무작위 적 피해 6"</summary>
    public static string PatternTitle(SpellPatternData data)
    {
        string description = PatternDescription(data);
        return $"{data.Name} - {description}";
    }

    /// <summary>도안의 기본 효과 줄과 키워드 태그 줄을 줄바꿈으로 이어 반환한다. SpellGame 정적 데이터를 읽지 않으므로 에디터 미저장 편집본에도 쓸 수 있다.</summary>
    public static string PatternDescription(SpellPatternData data)
    {
        string text = BaseEffectText(data);
        if (text.Length == 0) text = "기본 효과 없음";
        string keyword = KeywordLine(data);
        return keyword.Length == 0 ? text : text + "\n" + keyword;
    }

    /// <summary>키워드 태그와 보상을 "태그: 보상" 한 줄로 반환한다. 키워드가 없거나 보상 수치가 0이면 빈 문자열이다. 모자이크는 "× 조각 수"를 붙인다.</summary>
    public static string KeywordLine(SpellPatternData data)
    {
        if (!HasKeywordBonus(data.Keyword) || data.KeywordBonusValue == 0) return "";
        string line = $"{KeywordName(data.Keyword)}: {KeywordBonusText(data)}";
        return data.Keyword == SpellGame.Keyword.Mosaic ? line + " × 조각 수" : line;
    }

    /// <summary>키워드 태그의 표시 이름을 반환한다. 없음이면 빈 문자열이다.</summary>
    public static string KeywordName(SpellGame.Keyword keyword)
    {
        switch (keyword)
        {
            case SpellGame.Keyword.OneStroke: return "한 방";
            case SpellGame.Keyword.Mosaic: return "모자이크";
            case SpellGame.Keyword.Purity: return "순수";
            default: return "";
        }
    }

    /// <summary>도안 키워드 태그의 발동 조건 설명을 "태그 이름\n조건" 형식으로 반환한다. 태그 줄이 없으면 빈 문자열이다.</summary>
    public static string KeywordExplanation(SpellPatternData data)
    {
        if (KeywordLine(data).Length == 0) return "";
        switch (data.Keyword)
        {
            case SpellGame.Keyword.OneStroke: return "<b>한 방</b>\n조각 1장으로 도안을 완성해 시전하면 보너스를 받습니다.";
            case SpellGame.Keyword.Mosaic: return "<b>모자이크</b>\n도안을 채운 조각 1장마다 보너스를 받습니다.";
            case SpellGame.Keyword.Purity: return "<b>순수</b>\n같은 모양의 조각만으로 완성하면 보너스를 받습니다.\n<color=#B8C4D4>(회전·반전은 같은 모양 취급)</color>";
            default: return "";
        }
    }

    /// <summary>도안 효과 종류의 기본 효과 문구를 반환한다. 효과 종류가 None이면 빈 문자열이다.</summary>
    private static string BaseEffectText(SpellPatternData data)
    {
        int value = data.BaseValue;
        switch (data.Kind)
        {
            case SpellGame.SpellKind.Single: return $"단일 피해 {value}";
            case SpellGame.SpellKind.Block: return $"방어도 {value}";
            case SpellGame.SpellKind.All: return $"전체 피해 {value}";
            case SpellGame.SpellKind.Scatter: return data.Hits <= 1 ? $"무작위 적 피해 {value}" : $"무작위 적 피해 {value} × {data.Hits}회";
            default: return "";
        }
    }

    /// <summary>키워드가 만족 시 보상(종류·수치)을 쓰는지 반환한다. 한 방·순수·모자이크가 해당한다.</summary>
    public static bool HasKeywordBonus(SpellGame.Keyword keyword)
    {
        return keyword == SpellGame.Keyword.OneStroke || keyword == SpellGame.Keyword.Purity || keyword == SpellGame.Keyword.Mosaic;
    }

    /// <summary>도안 키워드 보상의 종류와 수치를 설명 문구로 반환한다. 예: "추가 피해 +6", "무작위 적 피해 6", "방어도 +6"</summary>
    private static string KeywordBonusText(SpellPatternData data)
    {
        switch (data.KeywordBonus)
        {
            case SpellGame.KeywordBonus.RandomDamage: return $"무작위 적 피해 {data.KeywordBonusValue}";
            case SpellGame.KeywordBonus.Block: return $"방어도 +{data.KeywordBonusValue}";
            default: return $"추가 피해 +{data.KeywordBonusValue}";
        }
    }

    /// <summary>도안 표를 검사해 오류와 경고 문구를 목록에 추가한다. warnings가 null이면 경고 검사를 건너뛴다.</summary>
    public static void ValidatePatterns(SpellPatternTable table, List<string> errors, List<string> warnings)
    {
        if (table.Patterns == null || table.Patterns.Count == 0) { errors.Add("도안이 하나 이상 있어야 합니다"); return; }
        if (table.Version != CURRENT_VERSION) errors.Add($"지원하지 않는 도안 JSON 버전입니다: {table.Version}");
        HashSet<string> ids = new HashSet<string>();
        bool hasStarter = false;
        for (int i = 0; i < table.Patterns.Count; i++)
        {
            SpellPatternData pattern = table.Patterns[i];
            string label = $"도안 {i} ({pattern.Name})";
            if (!IsValidId(pattern.Id)) errors.Add($"{label}: id는 영문 소문자, 숫자, _만 쓸 수 있습니다");
            else if (!ids.Add(pattern.Id)) errors.Add($"{label}: id '{pattern.Id}'가 중복됩니다");
            if (string.IsNullOrWhiteSpace(pattern.Name)) errors.Add($"{label}: 이름이 비어 있습니다");
            if (pattern.BaseValue < 0) errors.Add($"{label}: 기본 수치는 0 이상이어야 합니다");
            if (pattern.KeywordBonusValue < 0) errors.Add($"{label}: 키워드 보상 수치는 0 이상이어야 합니다");
            if (pattern.Kind == SpellGame.SpellKind.Scatter && pattern.Hits < 1) errors.Add($"{label}: 산탄 타격 횟수는 1 이상이어야 합니다");
            if (pattern.LayoutRotation < 0 || pattern.LayoutRotation > 3) errors.Add($"{label}: 배치 회전은 0~3이어야 합니다");
            if (!ParseColor(pattern.Color, out _)) errors.Add($"{label}: 색은 #RRGGBB 형식이어야 합니다");
            ValidateCells(label, pattern.Cells, errors);
            hasStarter |= pattern.IsStarter;
            if (warnings == null) continue;
            bool hasBonus = HasKeywordBonus(pattern.Keyword);
            if (hasBonus && pattern.KeywordBonusValue == 0) warnings.Add($"{label}: 키워드 보상 수치가 0이라 키워드 효과가 없습니다");
            if (pattern.Kind == SpellGame.SpellKind.None && hasBonus && pattern.KeywordBonus == SpellGame.KeywordBonus.Damage && pattern.KeywordBonusValue > 0)
                warnings.Add($"{label}: 기본 효과가 없어 키워드 추가 피해가 적용되지 않습니다. 무작위 적 피해나 방어도를 고르세요");
            if (pattern.Kind == SpellGame.SpellKind.None && (!hasBonus || pattern.KeywordBonusValue == 0))
                warnings.Add($"{label}: 기본 효과와 키워드 보상이 모두 없어 시전해도 씰링·코팅 효과만 납니다");
            if (hasBonus && pattern.Kind == SpellGame.SpellKind.Block && pattern.KeywordBonus == SpellGame.KeywordBonus.Damage)
                warnings.Add($"{label}: 방어 도안의 키워드 추가 피해는 방어 수치에 더해집니다");
            if (pattern.Cells != null && !FitsBoard(pattern)) warnings.Add($"{label}: 기본 위치·회전에서 판 밖으로 나갑니다. 실행 중에는 빈 자리를 찾아 배치합니다");
        }
        if (!hasStarter) errors.Add("시작 도안이 하나 이상 있어야 합니다");
        bool hasRewardPattern = false;
        foreach (SpellPatternData pattern in table.Patterns) hasRewardPattern |= pattern.IsInRewardPool && !pattern.IsStarter;
        if (warnings != null && !hasRewardPattern) warnings.Add("보상 후보인 비시작 도안이 없어 전투 보상에 도안이 나오지 않습니다");
        if (warnings != null) AddStarterOverlapWarnings(table, warnings);
    }

    /// <summary>조각 표를 검사해 오류와 경고 문구를 목록에 추가한다. Shape enum 값마다 정확히 하나가 있어야 한다.</summary>
    public static void ValidatePieces(SpellPieceTable table, List<string> errors, List<string> warnings)
    {
        if (table.Pieces == null) { errors.Add("조각 목록이 없습니다"); return; }
        if (table.Version != CURRENT_VERSION) errors.Add($"지원하지 않는 조각 JSON 버전입니다: {table.Version}");
        HashSet<SpellGame.Shape> found = new HashSet<SpellGame.Shape>();
        bool hasNormalReward = false;
        int starterTotal = 0;
        foreach (SpellPieceData piece in table.Pieces)
        {
            string label = $"조각 {piece.Shape}";
            if (piece.StarterCount < 0) errors.Add($"{label}: 기본 덱 장수는 0 이상이어야 합니다");
            if (piece.DecompositionPriority < 0) errors.Add($"{label}: 분해 후보 우선순위는 0 이상이어야 합니다");
            starterTotal += Math.Max(0, piece.StarterCount);
            // 보상은 점토를 뺀 도형을 반드시 뽑을 수 있어야 한다.
            hasNormalReward |= piece.IsInRewardPool && piece.Shape != SpellGame.Shape.Clay;
            if (!Enum.IsDefined(typeof(SpellGame.Shape), piece.Shape)) { errors.Add($"{label}: 알 수 없는 도형입니다"); continue; }
            if (!found.Add(piece.Shape)) errors.Add($"{label}: 항목이 중복됩니다");
            if (string.IsNullOrWhiteSpace(piece.Name)) errors.Add($"{label}: 이름이 비어 있습니다");
            if (ValidateCells(label, piece.Cells, errors) && !IsConnected(piece.Cells)) errors.Add($"{label}: 모든 구역이 변으로 이어져야 합니다");
        }
        foreach (SpellGame.Shape shape in Enum.GetValues(typeof(SpellGame.Shape)))
            if (!found.Contains(shape)) errors.Add($"조각 {shape}: JSON 항목이 없습니다");
        if (!hasNormalReward) errors.Add("점토가 아닌 보상 후보 조각이 하나 이상 있어야 합니다");
        if (starterTotal == 0) errors.Add("기본 덱에 넣은 조각이 없습니다. 조각 하나 이상에 기본 덱을 체크하세요");
    }

    /// <summary>칸 목록의 범위·마스크·중복·정규화를 검사해 오류를 추가한다. 오류가 없으면 true를 반환한다.</summary>
    public static bool ValidateCells(string label, List<SpellCellData> cells, List<string> errors)
    {
        if (cells == null || cells.Count == 0) { errors.Add($"{label}: 칸이 하나 이상 있어야 합니다"); return false; }
        int count = errors.Count, minX = int.MaxValue, minY = int.MaxValue;
        HashSet<int> positions = new HashSet<int>();
        foreach (SpellCellData cell in cells)
        {
            if (cell == null) { errors.Add($"{label}: 빈 칸 항목이 있습니다"); continue; }
            if (cell.X < 0 || cell.X >= GRID_SIZE || cell.Y < 0 || cell.Y >= GRID_SIZE) errors.Add($"{label}: 칸 ({cell.X}, {cell.Y})이 {GRID_SIZE}×{GRID_SIZE} 범위를 벗어납니다");
            if (!IsHalfUnitMask(cell.Mask)) errors.Add($"{label}: 칸 ({cell.X}, {cell.Y})의 마스크는 1/4 삼각형 단위(3, 6, 9, 12, 15)여야 합니다");
            if (!positions.Add(cell.Y * GRID_SIZE + cell.X)) errors.Add($"{label}: 칸 ({cell.X}, {cell.Y})이 중복됩니다");
            minX = Math.Min(minX, cell.X); minY = Math.Min(minY, cell.Y);
        }
        if (errors.Count == count && (minX != 0 || minY != 0)) errors.Add($"{label}: 칸이 왼쪽 위 (0, 0) 기준으로 정규화되지 않았습니다");
        return errors.Count == count;
    }

    /// <summary>칸 마스크가 1/4 삼각형(인접한 두 구역) 하나 또는 칸 전체인지 반환한다.</summary>
    public static bool IsHalfUnitMask(int mask)
    {
        return mask == FULL_MASK || mask == 3 || mask == 6 || mask == 9 || mask == 12;
    }

    /// <summary>칸 목록의 모든 구역이 같은 칸의 이웃 구역 또는 옆 칸의 마주 보는 구역을 통해 이어져 있는지 반환한다.</summary>
    public static bool IsConnected(List<SpellCellData> cells)
    {
        int[] masks = new int[GRID_SIZE * GRID_SIZE];
        int total = 0, startCell = -1, startSector = -1;
        foreach (SpellCellData cell in cells)
        {
            masks[cell.Y * GRID_SIZE + cell.X] = cell.Mask;
            for (int s = 0; s < 4; s++)
            {
                if ((cell.Mask & (1 << s)) == 0) continue;
                total++;
                if (startCell < 0) { startCell = cell.Y * GRID_SIZE + cell.X; startSector = s; }
            }
        }
        if (total == 0) return false;
        bool[] visited = new bool[masks.Length * 4];
        Stack<int> stack = new Stack<int>();
        stack.Push(startCell * 4 + startSector); visited[startCell * 4 + startSector] = true;
        int reached = 0;
        while (stack.Count > 0)
        {
            int node = stack.Pop(); reached++;
            int index = node / 4, sector = node % 4, x = index % GRID_SIZE, y = index / GRID_SIZE;
            // 같은 칸의 양옆 구역과 옆 칸의 마주 보는 구역이 변을 공유한다.
            VisitSector(masks, visited, stack, x, y, (sector + 1) % 4);
            VisitSector(masks, visited, stack, x, y, (sector + 3) % 4);
            VisitSector(masks, visited, stack, x + (sector == 1 ? 1 : sector == 3 ? -1 : 0), y + (sector == 2 ? 1 : sector == 0 ? -1 : 0), (sector + 2) % 4);
        }
        return reached == total;
    }

    /// <summary>지정 칸의 구역이 존재하고 아직 방문하지 않았으면 방문 표시 후 스택에 넣는다.</summary>
    private static void VisitSector(int[] masks, bool[] visited, Stack<int> stack, int x, int y, int sector)
    {
        if (x < 0 || x >= GRID_SIZE || y < 0 || y >= GRID_SIZE) return;
        int index = y * GRID_SIZE + x, node = index * 4 + sector;
        if ((masks[index] & (1 << sector)) == 0 || visited[node]) return;
        visited[node] = true; stack.Push(node);
    }

    /// <summary>도안을 기본 위치·회전으로 공유 판에 놓았을 때 격자 칸별 구역 마스크를 반환한다. 판 크기 밖 칸은 무시한다.</summary>
    public static int[] BoardCoverage(SpellPatternData pattern, out bool isInside)
    {
        int[] coverage = new int[SpellPatternLayout.COLUMNS * SpellPatternLayout.ROWS];
        isInside = true;
        int width = 0, height = 0;
        foreach (SpellCellData cell in pattern.Cells) { width = Math.Max(width, cell.X + 1); height = Math.Max(height, cell.Y + 1); }
        int rotation = (pattern.LayoutRotation % 4 + 4) % 4;
        foreach (SpellCellData cell in pattern.Cells)
        {
            int x = cell.X, y = cell.Y, w = width, h = height;
            for (int r = 0; r < rotation; r++) { int nx = h - 1 - y; y = x; x = nx; int t = w; w = h; h = t; }
            int mask = RotateMask(cell.Mask, rotation), gx = pattern.LayoutX + x, gy = pattern.LayoutY + y;
            if ((mask & ~SpellPatternLayout.BoardMask(gx, gy)) != 0) isInside = false;
            if (gx >= 0 && gx < SpellPatternLayout.COLUMNS && gy >= 0 && gy < SpellPatternLayout.ROWS) coverage[gy * SpellPatternLayout.COLUMNS + gx] |= mask;
        }
        return coverage;
    }

    /// <summary>구역 비트를 시계 방향으로 rotation번 돌린 마스크를 반환한다.</summary>
    public static int RotateMask(int mask, int rotation)
    {
        int r = (rotation % 4 + 4) % 4;
        return ((mask << r) | (mask >> (4 - r))) & FULL_MASK;
    }

    /// <summary>도안이 기본 위치·회전에서 공유 판 안에 모두 들어가는지 반환한다.</summary>
    private static bool FitsBoard(SpellPatternData pattern)
    {
        BoardCoverage(pattern, out bool isInside);
        return isInside;
    }

    /// <summary>시작 도안끼리 기본 위치에서 같은 구역을 차지하면 경고를 추가한다.</summary>
    private static void AddStarterOverlapWarnings(SpellPatternTable table, List<string> warnings)
    {
        int[] used = new int[SpellPatternLayout.COLUMNS * SpellPatternLayout.ROWS];
        foreach (SpellPatternData pattern in table.Patterns)
        {
            if (!pattern.IsStarter || pattern.Cells == null) continue;
            int[] coverage = BoardCoverage(pattern, out _);
            bool overlaps = false;
            for (int i = 0; i < used.Length; i++) { overlaps |= (used[i] & coverage[i]) != 0; used[i] |= coverage[i]; }
            if (overlaps) warnings.Add($"시작 도안 {pattern.Name}: 기본 위치가 앞선 시작 도안과 겹칩니다");
        }
    }

    /// <summary>id가 비어 있지 않고 영문 소문자, 숫자, _로만 이루어졌는지 반환한다.</summary>
    private static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (char c in id) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
        return true;
    }

    /// <summary>Resources 경로의 TextAsset 내용을 반환한다. 없으면 InvalidDataException을 던진다.</summary>
    private static string ReadResource(string path)
    {
        TextAsset asset = Resources.Load<TextAsset>(path);
        if (asset == null) throw new InvalidDataException($"Resources/{path}.json 파일이 없습니다");
        return asset.text;
    }

    /// <summary>오류가 있으면 파일 경로와 오류 목록을 담은 InvalidDataException을 던진다.</summary>
    private static void ThrowIfInvalid(string path, List<string> errors)
    {
        if (errors.Count == 0) return;
        throw new InvalidDataException($"Resources/{path}.json 검증 실패\n" + string.Join("\n", errors));
    }
}
