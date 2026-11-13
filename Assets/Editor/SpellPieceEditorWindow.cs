using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

/// <summary>pieces.json의 조각 도형별 표시 이름, 칸 모양, 회전 방식과 보상 후보 여부를 편집하는 에디터 창이다. 목록은 Shape enum이 정한다.</summary>
public sealed class SpellPieceEditorWindow : EditorWindow
{
    private const string DRAFT_KEY = "SpellPieceEditorWindow.Draft";
    private const float LIST_WIDTH = 220f;
    private const float PREVIEW_CELL_SIZE = 26f;
    private const float PREVIEW_GAP = 12f;
    private const float MESSAGE_HEIGHT = 120f;

    // 망치 분해 형상과 점토 채우기 코드가 이 도형들의 모양을 전제하므로 칸 편집을 막는다.
    private static readonly SpellGame.Shape[] LOCKED_SHAPES = { SpellGame.Shape.Clay };
    private static readonly SpellGame.Shape[] SHAPES = (SpellGame.Shape[])Enum.GetValues(typeof(SpellGame.Shape));
    private static readonly string[] ROTATION_LABELS = { "0°", "90°", "180°", "270°" };
    private static readonly Color PIECE_COLOR = new Color(0.72f, 0.9f, 0.94f);
    private static readonly Color MISSING_COLOR = new Color(1f, 0.45f, 0.45f);

    private SpellPieceTable _table;
    private string _loadError;
    private SpellGame.Shape _selected;
    private Vector2 _listScroll;
    private Vector2 _detailScroll;
    private Vector2 _messageScroll;
    private DateTime _fileTime;
    private bool _isChangedOutside;
    private bool _isValidationDirty = true;
    private readonly List<string> _errors = new List<string>();
    private readonly List<string> _warnings = new List<string>();

    /// <summary>조각 에디터 창을 연다.</summary>
    [MenuItem("Tools/Spell/조각 에디터")]
    private static void Open()
    {
        GetWindow<SpellPieceEditorWindow>("조각 에디터");
    }

    void OnEnable()
    {
        wantsMouseMove = true;
        saveChangesMessage = "조각 에디터에 저장하지 않은 변경이 있습니다. 저장할까요?";
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
        if (!File.Exists(SpellDataStore.PIECES_ASSET_PATH) || File.GetLastWriteTimeUtc(SpellDataStore.PIECES_ASSET_PATH) == _fileTime) return;
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
        GUILayout.Label(SpellDataStore.PIECES_ASSET_PATH + (hasUnsavedChanges ? "  * 저장 안 됨" : ""), EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(_table == null || !hasUnsavedChanges || _errors.Count > 0))
            if (GUILayout.Button("저장", EditorStyles.toolbarButton)) Save();
        if (GUILayout.Button("파일에서 다시 읽기", EditorStyles.toolbarButton)
            && (!hasUnsavedChanges || EditorUtility.DisplayDialog("조각 에디터", "저장하지 않은 변경을 버리고 파일에서 다시 읽을까요?", "다시 읽기", "취소")))
            LoadFromFile();
        EditorGUILayout.EndHorizontal();
        if (_isChangedOutside) EditorGUILayout.HelpBox("파일이 외부에서 바뀌었습니다. 저장하면 외부 변경을 덮어씁니다.", MessageType.Warning);
        if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("Play 중 저장한 값은 다음 Play부터 반영됩니다.", MessageType.Info);
    }

    /// <summary>Shape enum 순서로 조각 목록을 그린다. 칸 잠금 도형은 [잠금], JSON에 없는 도형은 빨간 [누락]으로 표시한다.</summary>
    private void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(LIST_WIDTH));
        _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
        foreach (SpellGame.Shape shape in SHAPES)
        {
            SpellPieceData piece = Find(shape);
            string text = piece == null ? $"{shape}  [누락]" : $"{piece.Name} ({shape}){(IsLocked(shape) ? "  [잠금]" : "")}{(piece.StarterCount > 0 ? $"  기본 {piece.StarterCount}장" : "")}";
            Color previous = GUI.contentColor;
            if (piece == null) GUI.contentColor = MISSING_COLOR;
            if (GUILayout.Toggle(shape == _selected, text, EditorStyles.toolbarButton) && shape != _selected) { _selected = shape; GUI.FocusControl(null); }
            GUI.contentColor = previous;
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>선택 조각의 속성, 칸 격자와 네 방향 미리보기를 그린다. JSON에 없으면 추가 버튼만 표시한다.</summary>
    private void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
        SpellPieceData piece = Find(_selected);
        if (piece == null)
        {
            EditorGUILayout.HelpBox($"{_selected}의 JSON 항목이 없습니다. 실행하려면 모든 Shape 값에 항목이 있어야 합니다.", MessageType.Error);
            if (GUILayout.Button("기본 항목 추가")) AddPiece(_selected);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }
        bool isLocked = IsLocked(piece.Shape);
        EditorGUILayout.LabelField("Shape", piece.Shape.ToString());
        EditorGUI.BeginChangeCheck();
        piece.Name = EditorGUILayout.TextField("이름", piece.Name);
        piece.Rotation = (SpellPieceData.RotationMode)EditorGUILayout.EnumPopup(new GUIContent("회전 방식", "MirrorAlternate는 2·3 방향에 좌우를 뒤집은 모양을 씁니다."), piece.Rotation);
        piece.IsInRewardPool = EditorGUILayout.Toggle("보상 후보", piece.IsInRewardPool);
        piece.DecompositionPriority = Math.Max(0, EditorGUILayout.IntField(new GUIContent("분해 재료 우선순위", "작은 값의 도형을 분해 결과로 우선 사용합니다. 분해 위치와 개수는 실제 모양에서 자동 계산합니다."), piece.DecompositionPriority));
        DrawStarterCount(piece);
        bool isChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("칸 모양 (기본 방향)", EditorStyles.boldLabel);
        if (isLocked) EditorGUILayout.HelpBox("점토는 전용 변형 규칙을 사용하므로 칸을 편집할 수 없습니다.", MessageType.Info);
        Rect grid = GUILayoutUtility.GetRect(SpellGridDrawer.CELL_SIZE * SpellDataStore.GRID_SIZE, SpellGridDrawer.CELL_SIZE * SpellDataStore.GRID_SIZE, GUILayout.ExpandWidth(false));
        isChanged |= SpellGridDrawer.DrawEditable(grid, piece.Cells, PIECE_COLOR, isLocked);
        if (!isLocked) EditorGUILayout.LabelField("클릭: 가까운 꼭짓점 쪽 1/4 삼각형 · Shift+클릭: 칸 전체 · 우클릭: 칸 비우기", EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("회전 미리보기", EditorStyles.boldLabel);
        DrawRotationPreview(piece);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        if (isChanged) MarkDirty();
    }

    /// <summary>기본 덱 체크와, 체크했을 때 넣을 장수 입력을 그린다. 체크하면 1장으로 시작하고 해제하면 0장이다. 아래에 전체 기본 덱 장수를 표시한다.</summary>
    private void DrawStarterCount(SpellPieceData piece)
    {
        bool isStarter = EditorGUILayout.Toggle(new GUIContent("기본 덱", "체크하면 새 게임 시작 덱에 이 조각을 넣습니다."), piece.StarterCount > 0);
        if (!isStarter) piece.StarterCount = 0;
        else
        {
            EditorGUI.indentLevel++;
            piece.StarterCount = Mathf.Max(1, EditorGUILayout.IntField("장수", Mathf.Max(1, piece.StarterCount)));
            EditorGUI.indentLevel--;
        }
        int total = 0;
        foreach (SpellPieceData other in _table.Pieces) total += Mathf.Max(0, other.StarterCount);
        EditorGUILayout.LabelField($"기본 덱 합계 {total}장", EditorStyles.miniLabel);
    }

    /// <summary>편집 중인 칸을 정규화해 SpellGame.OrientCells로 네 방향을 계산하고 나란히 그린다. 런타임 데이터를 읽지 못하는 상태면 안내만 표시한다.</summary>
    private void DrawRotationPreview(SpellPieceData piece)
    {
        if (piece.Cells.Count == 0) { EditorGUILayout.LabelField("칸이 없습니다.", EditorStyles.miniLabel); return; }
        List<SpellCellData> normalized = SpellDataStore.Clone(piece.Cells);
        SpellDataStore.Normalize(normalized);
        SpellGame.ShapeCell[] baseCells = new SpellGame.ShapeCell[normalized.Count];
        for (int i = 0; i < baseCells.Length; i++) baseCells[i] = new SpellGame.ShapeCell(normalized[i].X, normalized[i].Y, normalized[i].Mask);
        SpellGame.ShapeCell[][] orientations = new SpellGame.ShapeCell[ROTATION_LABELS.Length][];
        try
        {
            for (int r = 0; r < orientations.Length; r++) orientations[r] = SpellGame.OrientCells(baseCells, piece.Rotation == SpellPieceData.RotationMode.MirrorAlternate, r);
        }
        catch (TypeInitializationException exception)
        {
            // OrientCells 호출이 SpellGame 정적 초기화(JSON 로드)를 일으키므로, 디스크 데이터가 잘못되면 미리보기만 포기한다.
            EditorGUILayout.HelpBox("런타임 데이터 로드에 실패해 미리보기를 표시할 수 없습니다. 파일을 고쳐 저장한 뒤 스크립트를 다시 불러오세요.\n" + exception.InnerException?.Message, MessageType.Warning);
            return;
        }
        float size = PREVIEW_CELL_SIZE * SpellDataStore.GRID_SIZE;
        Rect row = GUILayoutUtility.GetRect(orientations.Length * (size + PREVIEW_GAP), size + EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(false));
        for (int r = 0; r < orientations.Length; r++)
        {
            float x = row.x + r * (size + PREVIEW_GAP);
            GUI.Label(new Rect(x, row.y, size, EditorGUIUtility.singleLineHeight), ROTATION_LABELS[r], EditorStyles.miniLabel);
            SpellGridDrawer.DrawPreview(new Rect(x, row.y + EditorGUIUtility.singleLineHeight, size, size), orientations[r], SpellDataStore.GRID_SIZE, SpellDataStore.GRID_SIZE, PREVIEW_CELL_SIZE, PIECE_COLOR);
        }
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
            _table = SpellDataStore.ParsePieces(File.ReadAllText(SpellDataStore.PIECES_ASSET_PATH));
            _fileTime = File.GetLastWriteTimeUtc(SpellDataStore.PIECES_ASSET_PATH);
        }
        catch (Exception exception)
        {
            _loadError = $"{SpellDataStore.PIECES_ASSET_PATH}을 읽지 못했습니다.\n{exception.Message}";
        }
        hasUnsavedChanges = false;
        SessionState.EraseString(DRAFT_KEY);
        _isValidationDirty = true;
        Repaint();
    }

    /// <summary>도메인 리로드 전에 보존한 편집본 JSON을 복원한다. 복원하면 미저장 상태로 두고 true를 반환한다.</summary>
    private bool TryRestoreDraft(string draft)
    {
        try { _table = SpellDataStore.ParsePieces(draft); }
        catch (Exception) { SessionState.EraseString(DRAFT_KEY); return false; }
        _loadError = null;
        if (File.Exists(SpellDataStore.PIECES_ASSET_PATH)) _fileTime = File.GetLastWriteTimeUtc(SpellDataStore.PIECES_ASSET_PATH);
        hasUnsavedChanges = true;
        _isValidationDirty = true;
        return true;
    }

    /// <summary>편집본 복사본의 칸을 정규화하고 Shape 순서로 정렬해 검증한 뒤, 오류가 없으면 파일에 쓰고 가져오기를 갱신한다. 저장하면 true를 반환한다.</summary>
    private bool Save()
    {
        SpellPieceTable normalized = CreateNormalized();
        List<string> errors = new List<string>();
        SpellDataStore.ValidatePieces(normalized, errors, null);
        if (errors.Count > 0)
        {
            EditorUtility.DisplayDialog("조각 에디터", "검증 오류가 있어 저장하지 않았습니다.\n" + string.Join("\n", errors), "확인");
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(SpellDataStore.PIECES_ASSET_PATH));
        File.WriteAllText(SpellDataStore.PIECES_ASSET_PATH, SpellDataStore.Serialize(normalized));
        AssetDatabase.ImportAsset(SpellDataStore.PIECES_ASSET_PATH);
        _table = normalized;
        _fileTime = File.GetLastWriteTimeUtc(SpellDataStore.PIECES_ASSET_PATH);
        _isChangedOutside = false;
        hasUnsavedChanges = false;
        SessionState.EraseString(DRAFT_KEY);
        _isValidationDirty = true;
        return true;
    }

    /// <summary>편집본 복사본의 오류·경고 목록을 다시 계산한다. 저장 시 정규화하므로 칸 위치 오류는 보이지 않는다.</summary>
    private void Validate()
    {
        _errors.Clear(); _warnings.Clear();
        SpellDataStore.ValidatePieces(CreateNormalized(), _errors, _warnings);
        _isValidationDirty = false;
    }

    /// <summary>편집본을 깊은 복사해 모든 칸을 정규화하고 항목을 Shape enum 순서로 정렬한 표를 반환한다.</summary>
    private SpellPieceTable CreateNormalized()
    {
        SpellPieceTable normalized = SpellDataStore.Clone(_table);
        foreach (SpellPieceData piece in normalized.Pieces) SpellDataStore.Normalize(piece.Cells);
        normalized.Pieces.Sort((a, b) => ((int)a.Shape).CompareTo((int)b.Shape));
        return normalized;
    }

    /// <summary>편집본을 미저장 상태로 표시하고 검증을 다시 하도록 예약한다.</summary>
    private void MarkDirty()
    {
        hasUnsavedChanges = true;
        _isValidationDirty = true;
        Repaint();
    }

    /// <summary>누락된 도형의 기본 항목을 추가한다. 이름은 enum 이름, 꽉 찬 칸 하나, 일반 회전, 보상 후보 포함이다.</summary>
    private void AddPiece(SpellGame.Shape shape)
    {
        SpellPieceData piece = new SpellPieceData { Shape = shape, Name = shape.ToString(), Rotation = SpellPieceData.RotationMode.Rotate, IsInRewardPool = true };
        piece.Cells.Add(new SpellCellData { X = 0, Y = 0, Mask = SpellDataStore.FULL_MASK });
        _table.Pieces.Add(piece);
        MarkDirty();
    }

    /// <summary>편집본에서 지정 도형의 첫 항목을 반환한다. 없으면 null이다.</summary>
    private SpellPieceData Find(SpellGame.Shape shape)
    {
        if (_table == null) return null;
        foreach (SpellPieceData piece in _table.Pieces) if (piece.Shape == shape) return piece;
        return null;
    }

    /// <summary>칸 편집을 막는 규칙 연동 도형인지 반환한다.</summary>
    private static bool IsLocked(SpellGame.Shape shape)
    {
        return Array.IndexOf(LOCKED_SHAPES, shape) >= 0;
    }
}
