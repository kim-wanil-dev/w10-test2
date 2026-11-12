using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

/// <summary>patterns.json의 도안 목록, 효과, 칸 모양과 장착 화면 기본 배치·색을 편집하는 에디터 창이다.</summary>
public sealed class SpellPatternEditorWindow : EditorWindow
{
    private const string DRAFT_KEY = "SpellPatternEditorWindow.Draft";
    private const float LIST_WIDTH = 260f;
    private const float BOARD_CELL_SIZE = 40f;
    private const float SWATCH_SIZE = 14f;
    private const float MESSAGE_HEIGHT = 120f;
    private const string NEW_ID_PREFIX = "pattern_";
    private const float OTHER_PATTERN_ALPHA = 0.3f;

    private static readonly string[] ROTATION_LABELS = { "0°", "90°", "180°", "270°" };
    private static readonly Color BOARD_COLOR = new Color(0.3f, 0.3f, 0.3f);
    private static readonly Color OUTSIDE_COLOR = new Color(0.9f, 0.2f, 0.2f);

    private SpellPatternTable _table;
    private string _loadError;
    private int _selected;
    private Vector2 _listScroll;
    private Vector2 _detailScroll;
    private Vector2 _messageScroll;
    private DateTime _fileTime;
    private bool _isChangedOutside;
    private bool _isValidationDirty = true;
    private readonly List<string> _errors = new List<string>();
    private readonly List<string> _warnings = new List<string>();

    /// <summary>도안 에디터 창을 연다.</summary>
    [MenuItem("Tools/Spell/도안 에디터")]
    private static void Open()
    {
        GetWindow<SpellPatternEditorWindow>("도안 에디터");
    }

    void OnEnable()
    {
        wantsMouseMove = true;
        saveChangesMessage = "도안 에디터에 저장하지 않은 변경이 있습니다. 저장할까요?";
        string draft = SessionState.GetString(DRAFT_KEY, "");
        if (draft.Length > 0 && TryRestoreDraft(draft)) return;
        LoadFromFile();
    }

    void OnDisable()
    {
        // 도메인 리로드(Play 진입, 스크립트 컴파일) 사이에 미저장 편집을 보존한다.
        if (hasUnsavedChanges && _table != null) SessionState.SetString(DRAFT_KEY, SpellDataStore.Serialize(_table));
        else SessionState.EraseString(DRAFT_KEY);
    }

    void OnFocus()
    {
        if (!File.Exists(SpellDataStore.PATTERNS_ASSET_PATH) || File.GetLastWriteTimeUtc(SpellDataStore.PATTERNS_ASSET_PATH) == _fileTime) return;
        if (hasUnsavedChanges) _isChangedOutside = true;
        else LoadFromFile();
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.MouseMove) Repaint();
        if (_table != null && _isValidationDirty) Validate();
        DrawToolbar();
        if (_loadError != null)
        {
            EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            return;
        }
        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
        DrawMessages();
    }

    /// <summary>미저장 확인창에서 저장을 고르면 호출된다. 검증 오류로 저장하지 못하면 창 닫기를 취소한다.</summary>
    public override void SaveChanges()
    {
        if (Save()) base.SaveChanges();
    }

    /// <summary>미저장 확인창에서 저장하지 않음을 고르면 보존한 편집본을 지운다.</summary>
    public override void DiscardChanges()
    {
        SessionState.EraseString(DRAFT_KEY);
        base.DiscardChanges();
    }

    /// <summary>파일 경로, 저장·다시 읽기 버튼과 미저장·외부 변경·Play 중 안내를 그린다.</summary>
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(SpellDataStore.PATTERNS_ASSET_PATH + (hasUnsavedChanges ? "  * 저장 안 됨" : ""), EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(_table == null || !hasUnsavedChanges || _errors.Count > 0))
            if (GUILayout.Button("저장", EditorStyles.toolbarButton)) Save();
        if (GUILayout.Button("파일에서 다시 읽기", EditorStyles.toolbarButton)
            && (!hasUnsavedChanges || EditorUtility.DisplayDialog("도안 에디터", "저장하지 않은 변경을 버리고 파일에서 다시 읽을까요?", "다시 읽기", "취소")))
            LoadFromFile();
        EditorGUILayout.EndHorizontal();
        if (_isChangedOutside) EditorGUILayout.HelpBox("파일이 외부에서 바뀌었습니다. 저장하면 외부 변경을 덮어씁니다.", MessageType.Warning);
        if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("Play 중 저장한 값은 다음 Play부터 반영됩니다.", MessageType.Info);
    }

    /// <summary>도안 목록과 추가·복제·삭제·순서 이동 버튼을 그리고 선택 도안을 바꾼다.</summary>
    private void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(LIST_WIDTH));
        _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
        for (int i = 0; i < _table.Patterns.Count; i++)
        {
            SpellPatternData pattern = _table.Patterns[i];
            Rect row = EditorGUILayout.GetControlRect();
            Rect swatch = new Rect(row.x, row.y + (row.height - SWATCH_SIZE) / 2, SWATCH_SIZE, SWATCH_SIZE);
            SpellDataStore.ParseColor(pattern.Color, out Color color);
            EditorGUI.DrawRect(swatch, color);
            Rect label = new Rect(swatch.xMax + 4, row.y, row.width - SWATCH_SIZE - 4, row.height);
            string text = $"{i}. {pattern.Name} ({pattern.Id}){(pattern.IsStarter ? "  시작" : "")}";
            if (GUI.Toggle(label, i == _selected, text, EditorStyles.toolbarButton) && i != _selected) { _selected = i; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("추가")) AddPattern(CreatePattern());
        using (new EditorGUI.DisabledScope(!HasSelection()))
        {
            if (GUILayout.Button("복제")) DuplicateSelected();
            if (GUILayout.Button("삭제")) DeleteSelected();
            if (GUILayout.Button("▲")) MoveSelected(-1);
            if (GUILayout.Button("▼")) MoveSelected(1);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    /// <summary>선택 도안의 속성, 칸 격자와 공유 판 미리보기를 그린다. 값이 바뀌면 미저장 상태로 표시한다.</summary>
    private void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
        if (!HasSelection())
        {
            EditorGUILayout.HelpBox("왼쪽에서 도안을 선택하세요.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }
        SpellPatternData pattern = _table.Patterns[_selected];
        SpellDataStore.ParseColor(pattern.Color, out Color color);
        EditorGUI.BeginChangeCheck();
        pattern.Id = EditorGUILayout.TextField("id", pattern.Id);
        pattern.Name = EditorGUILayout.TextField("이름", pattern.Name);
        pattern.Kind = (SpellGame.SpellKind)EditorGUILayout.EnumPopup("효과 종류", pattern.Kind);
        pattern.BaseValue = Mathf.Max(0, EditorGUILayout.IntField("기본 수치", pattern.BaseValue));
        pattern.Keyword = (SpellGame.Keyword)EditorGUILayout.EnumPopup("키워드", pattern.Keyword);
        Color picked = EditorGUILayout.ColorField(new GUIContent("색"), color, true, false, false);
        if (picked != color) pattern.Color = SpellDataStore.ToColorCode(picked);
        pattern.IsStarter = EditorGUILayout.Toggle("시작 도안", pattern.IsStarter);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("장착 화면 기본 배치", EditorStyles.boldLabel);
        pattern.LayoutX = EditorGUILayout.IntSlider("x", pattern.LayoutX, 0, SpellPatternLayout.COLUMNS - 1);
        pattern.LayoutY = EditorGUILayout.IntSlider("y", pattern.LayoutY, 0, SpellPatternLayout.ROWS - 1);
        pattern.LayoutRotation = EditorGUILayout.Popup("회전", pattern.LayoutRotation, ROTATION_LABELS);
        bool isChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(GUILayout.Width(SpellGridDrawer.CELL_SIZE * SpellDataStore.GRID_SIZE + 8));
        EditorGUILayout.LabelField("칸 모양", EditorStyles.boldLabel);
        Rect grid = GUILayoutUtility.GetRect(SpellGridDrawer.CELL_SIZE * SpellDataStore.GRID_SIZE, SpellGridDrawer.CELL_SIZE * SpellDataStore.GRID_SIZE, GUILayout.ExpandWidth(false));
        isChanged |= SpellGridDrawer.DrawEditable(grid, pattern.Cells, color, false);
        EditorGUILayout.LabelField($"구역 {CountSectors(pattern.Cells)}개", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("클릭: 구역 · Shift+클릭: 칸 전체 · 우클릭: 칸 비우기", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();
        GUILayout.Space(16);
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField("공유 판 미리보기", EditorStyles.boldLabel);
        Rect board = GUILayoutUtility.GetRect(BOARD_CELL_SIZE * SpellPatternLayout.COLUMNS, BOARD_CELL_SIZE * SpellPatternLayout.ROWS, GUILayout.ExpandWidth(false));
        bool isInside = DrawBoardPreview(board, pattern);
        EditorGUILayout.LabelField(isInside ? "격자 좌표 기준이며 게임 화면에서는 45도 돌아가 보입니다. 옅은 색은 시작 도안입니다." : "빨간 구역이 판 밖으로 나갑니다.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        if (isChanged) MarkDirty();
    }

    /// <summary>4×4 공유 판의 유효 구역 위에 다른 시작 도안을 옅게, 선택 도안을 진하게 그린다. 판 밖 구역은 빨간색이다. 선택 도안이 판 안에 다 들어가면 true를 반환한다.</summary>
    private bool DrawBoardPreview(Rect area, SpellPatternData selected)
    {
        int[] coverage = SpellDataStore.BoardCoverage(selected, out bool isInside);
        if (Event.current.type != EventType.Repaint) return isInside;
        for (int y = 0; y < SpellPatternLayout.ROWS; y++)
            for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
            {
                Rect cell = SpellGridDrawer.CellRect(area, x, y, BOARD_CELL_SIZE);
                SpellGridDrawer.DrawSectors(cell, SpellPatternLayout.BoardMask(x, y), BOARD_COLOR);
            }
        foreach (SpellPatternData other in _table.Patterns)
        {
            if (other == selected || !other.IsStarter) continue;
            SpellDataStore.ParseColor(other.Color, out Color otherColor);
            otherColor.a = OTHER_PATTERN_ALPHA;
            DrawCoverage(area, SpellDataStore.BoardCoverage(other, out _), otherColor);
        }
        SpellDataStore.ParseColor(selected.Color, out Color color);
        DrawCoverage(area, coverage, color);
        for (int i = 0; i < coverage.Length; i++)
        {
            int outside = coverage[i] & ~SpellPatternLayout.BoardMask(i % SpellPatternLayout.COLUMNS, i / SpellPatternLayout.COLUMNS);
            SpellGridDrawer.DrawSectors(SpellGridDrawer.CellRect(area, i % SpellPatternLayout.COLUMNS, i / SpellPatternLayout.COLUMNS, BOARD_CELL_SIZE), outside, OUTSIDE_COLOR);
        }
        for (int y = 0; y < SpellPatternLayout.ROWS; y++)
            for (int x = 0; x < SpellPatternLayout.COLUMNS; x++) SpellGridDrawer.DrawCellLines(SpellGridDrawer.CellRect(area, x, y, BOARD_CELL_SIZE));
        return isInside;
    }

    /// <summary>판 칸별 구역 마스크 배열을 지정 색으로 칠한다.</summary>
    private static void DrawCoverage(Rect area, int[] coverage, Color color)
    {
        for (int i = 0; i < coverage.Length; i++)
            SpellGridDrawer.DrawSectors(SpellGridDrawer.CellRect(area, i % SpellPatternLayout.COLUMNS, i / SpellPatternLayout.COLUMNS, BOARD_CELL_SIZE), coverage[i], color);
    }

    /// <summary>검증 오류와 경고 목록을 창 아래쪽에 그린다.</summary>
    private void DrawMessages()
    {
        if (_errors.Count == 0 && _warnings.Count == 0) return;
        _messageScroll = EditorGUILayout.BeginScrollView(_messageScroll, GUILayout.Height(MESSAGE_HEIGHT));
        foreach (string error in _errors) EditorGUILayout.HelpBox(error, MessageType.Error);
        foreach (string warning in _warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        EditorGUILayout.EndScrollView();
    }

    /// <summary>파일을 직접 읽어 편집본으로 삼고 미저장·외부 변경 상태를 지운다. 파싱에 실패하면 오류 문구를 보관한다.</summary>
    private void LoadFromFile()
    {
        _table = null; _loadError = null; _isChangedOutside = false;
        try
        {
            _table = SpellDataStore.ParsePatterns(File.ReadAllText(SpellDataStore.PATTERNS_ASSET_PATH));
            _fileTime = File.GetLastWriteTimeUtc(SpellDataStore.PATTERNS_ASSET_PATH);
        }
        catch (Exception exception)
        {
            _loadError = $"{SpellDataStore.PATTERNS_ASSET_PATH}을 읽지 못했습니다.\n{exception.Message}";
        }
        _selected = Mathf.Clamp(_selected, 0, _table == null ? 0 : _table.Patterns.Count - 1);
        hasUnsavedChanges = false;
        SessionState.EraseString(DRAFT_KEY);
        _isValidationDirty = true;
        Repaint();
    }

    /// <summary>도메인 리로드 전에 보존한 편집본 JSON을 복원한다. 복원하면 미저장 상태로 두고 true를 반환한다.</summary>
    private bool TryRestoreDraft(string draft)
    {
        try { _table = SpellDataStore.ParsePatterns(draft); }
        catch (Exception) { SessionState.EraseString(DRAFT_KEY); return false; }
        _loadError = null;
        if (File.Exists(SpellDataStore.PATTERNS_ASSET_PATH)) _fileTime = File.GetLastWriteTimeUtc(SpellDataStore.PATTERNS_ASSET_PATH);
        hasUnsavedChanges = true;
        _isValidationDirty = true;
        return true;
    }

    /// <summary>편집본 복사본의 칸을 정규화해 검증하고, 오류가 없으면 파일에 쓰고 가져오기를 갱신한다. 저장하면 true를 반환한다.</summary>
    private bool Save()
    {
        SpellPatternTable normalized = SpellDataStore.Clone(_table);
        foreach (SpellPatternData pattern in normalized.Patterns) SpellDataStore.Normalize(pattern.Cells);
        List<string> errors = new List<string>();
        SpellDataStore.ValidatePatterns(normalized, errors, null);
        if (errors.Count > 0)
        {
            EditorUtility.DisplayDialog("도안 에디터", "검증 오류가 있어 저장하지 않았습니다.\n" + string.Join("\n", errors), "확인");
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(SpellDataStore.PATTERNS_ASSET_PATH));
        File.WriteAllText(SpellDataStore.PATTERNS_ASSET_PATH, SpellDataStore.Serialize(normalized));
        AssetDatabase.ImportAsset(SpellDataStore.PATTERNS_ASSET_PATH);
        _table = normalized;
        _fileTime = File.GetLastWriteTimeUtc(SpellDataStore.PATTERNS_ASSET_PATH);
        _isChangedOutside = false;
        hasUnsavedChanges = false;
        SessionState.EraseString(DRAFT_KEY);
        _isValidationDirty = true;
        return true;
    }

    /// <summary>편집본 복사본의 칸을 정규화해 오류·경고 목록을 다시 계산한다. 저장 시 정규화하므로 칸 위치 오류는 보이지 않는다.</summary>
    private void Validate()
    {
        _errors.Clear(); _warnings.Clear();
        SpellPatternTable normalized = SpellDataStore.Clone(_table);
        foreach (SpellPatternData pattern in normalized.Patterns) SpellDataStore.Normalize(pattern.Cells);
        SpellDataStore.ValidatePatterns(normalized, _errors, _warnings);
        _isValidationDirty = false;
    }

    /// <summary>편집본을 미저장 상태로 표시하고 검증을 다시 하도록 예약한다.</summary>
    private void MarkDirty()
    {
        hasUnsavedChanges = true;
        _isValidationDirty = true;
        Repaint();
    }

    /// <summary>선택 인덱스가 현재 목록 범위 안인지 반환한다.</summary>
    private bool HasSelection()
    {
        return _table != null && _selected >= 0 && _selected < _table.Patterns.Count;
    }

    /// <summary>새 도안 기본값을 만든다. 겹치지 않는 id, 단일 피해 1, 흰색, 꽉 찬 칸 하나다.</summary>
    private SpellPatternData CreatePattern()
    {
        SpellPatternData pattern = new SpellPatternData
        {
            Id = UniqueId(NEW_ID_PREFIX), Name = "새 도안", Kind = SpellGame.SpellKind.Single, BaseValue = 1, Keyword = SpellGame.Keyword.None,
            LayoutX = 1, LayoutY = 1, LayoutRotation = 0, Color = SpellDataStore.ToColorCode(Color.white), IsStarter = false
        };
        pattern.Cells.Add(new SpellCellData { X = 0, Y = 0, Mask = SpellDataStore.FULL_MASK });
        return pattern;
    }

    /// <summary>도안을 선택 위치 바로 뒤(선택이 없으면 끝)에 넣고 선택한다.</summary>
    private void AddPattern(SpellPatternData pattern)
    {
        int index = HasSelection() ? _selected + 1 : _table.Patterns.Count;
        _table.Patterns.Insert(index, pattern);
        _selected = index;
        GUI.FocusControl(null);
        MarkDirty();
    }

    /// <summary>선택 도안을 깊은 복사해 새 id와 "(복사)" 이름으로 바로 뒤에 넣는다.</summary>
    private void DuplicateSelected()
    {
        SpellPatternData copy = SpellDataStore.Clone(_table.Patterns[_selected]);
        copy.Id = UniqueId(copy.Id + "_");
        copy.Name += " (복사)";
        copy.IsStarter = false;
        AddPattern(copy);
    }

    /// <summary>확인 후 선택 도안을 지운다. 도안 번호가 뒤에서 하나씩 당겨진다.</summary>
    private void DeleteSelected()
    {
        SpellPatternData pattern = _table.Patterns[_selected];
        if (!EditorUtility.DisplayDialog("도안 에디터", $"{pattern.Name} ({pattern.Id})을(를) 삭제할까요?\n뒤 도안들의 번호가 하나씩 당겨집니다.", "삭제", "취소")) return;
        _table.Patterns.RemoveAt(_selected);
        _selected = Mathf.Min(_selected, _table.Patterns.Count - 1);
        GUI.FocusControl(null);
        MarkDirty();
    }

    /// <summary>선택 도안을 목록에서 direction만큼 옮긴다. 범위를 벗어나면 아무것도 하지 않는다.</summary>
    private void MoveSelected(int direction)
    {
        int target = _selected + direction;
        if (target < 0 || target >= _table.Patterns.Count) return;
        SpellPatternData pattern = _table.Patterns[_selected];
        _table.Patterns[_selected] = _table.Patterns[target];
        _table.Patterns[target] = pattern;
        _selected = target;
        MarkDirty();
    }

    /// <summary>prefix 뒤에 1부터 번호를 붙여 목록에 없는 id를 반환한다.</summary>
    private string UniqueId(string prefix)
    {
        HashSet<string> ids = new HashSet<string>();
        foreach (SpellPatternData pattern in _table.Patterns) ids.Add(pattern.Id);
        int number = 1;
        while (ids.Contains(prefix + number)) number++;
        return prefix + number;
    }

    /// <summary>칸 목록의 켜진 1/4 구역 수를 반환한다. 데이터 로드를 일으키지 않도록 SpellGame 정적 멤버를 쓰지 않는다.</summary>
    private static int CountSectors(List<SpellCellData> cells)
    {
        int count = 0;
        foreach (SpellCellData cell in cells)
            for (int s = 0; s < 4; s++) if ((cell.Mask & (1 << s)) != 0) count++;
        return count;
    }
}
