using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>주문 조각 PoC의 화면과 입력을 전투 상태에 연결한다.</summary>
public sealed class SpellGameUI : MonoBehaviour
{
    private const float THEFT_MIN_START_DISTANCE = 360;
    private const float THEFT_ARRIVAL_RADIUS = 12;
    private enum HammerPhase { None, Selecting, Timing, Striking, Result }
    private const float LEGACY_SCALE = 1.5f;
    private const float LEGACY_CELL = 84f;
    private const float TOAST_SECONDS = 2.2f;
    private const float CURSOR_HINT_OFFSET = 24f;
    private const float CURSOR_HINT_PADDING = 24f;
    private const float TAB_HEIGHT = 36f;
    private const int BATTLE_COUNT = 6;
    private const float ENEMY_BAR_WIDTH = 320f;
    private const float ENEMY_NARROW_BAR_WIDTH = 280f;
    private const float MENU_GAP = 4f;
    private const float TRAIT_WIDTH = 250f;
    private const float TRAIT_PADDING = 12f;
    private const float TRAIT_BAR = 5f;
    private const float TRAIT_POINTER_GAP = 22f;
    private static readonly Vector2[] THEFT_START_POINTS = { new Vector2(1180, 130), new Vector2(1180, 430), new Vector2(640, 65), new Vector2(80, 65) };
    private static readonly bool ONBOARDING_ENABLED = false;
    private static readonly Vector2 MESSAGE_POSITION = new Vector2(705, -480);
    private static readonly Vector2 FEEDBACK_POSITION = new Vector2(960, -100);
    private static readonly Vector2 NORMAL_ENEMY_SIZE = new Vector2(150, 190);
    private static readonly Vector2 LARGE_ENEMY_SIZE = new Vector2(190, 230);
    private static readonly Vector2 BOSS_SIZE = new Vector2(210, 245);

    [Header("화면 연결")]
    [SerializeField] private GameObject _canvas;
    [SerializeField] private RectTransform _content;
    [SerializeField] private SpellCombatScreen _combatScreen;
    [SerializeField] private SpellTitleScreen _titleScreen;
    [SerializeField] private SpellRewardScreen _rewardScreen;
    [SerializeField] private SpellResultScreen _resultScreen;
    [SerializeField] private SpellPauseMenu _pauseMenu;
    [SerializeField] private SpellHelpPanel _helpPanel;
    [SerializeField] private SpellPileViewer _pileViewer;
    [SerializeField] private SpellLoadoutScreen _loadoutScreen;
    [SerializeField] private SpellTable3D _table;
    [SerializeField] private GameObject _background;

    [Header("상태 색")]
    [SerializeField] private Color _flameColor = new Color(0.878f, 0.478f, 0.247f);
    [SerializeField] private Color _guardColor = new Color(0.31f, 0.584f, 0.839f);
    [SerializeField] private Color _resonanceColor = new Color(0.655f, 0.467f, 0.851f);
    [SerializeField] private Color _afterimageColor = new Color(0.333f, 0.78f, 0.702f);
    [SerializeField] private Color _scribeColor = new Color(0.93f, 0.85f, 0.55f);
    [SerializeField] private Color _sparkColor = new Color(0.95f, 0.35f, 0.45f);
    [SerializeField] private Color _periodColor = new Color(0.65f, 0.9f, 0.35f);
    [SerializeField] private Color _cogColor = new Color(0.72f, 0.55f, 0.38f);
    [SerializeField] private Color _overpaintColor = new Color(0.9f, 0.45f, 0.85f);
    [SerializeField] private Color[] _enemyColors = { new Color(0.478f, 0.416f, 0.549f), new Color(0.373f, 0.498f, 0.447f), new Color(0.549f, 0.431f, 0.369f) };
    [SerializeField] private Color _textColor = new Color(0.945f, 0.922f, 0.867f);
    [SerializeField] private Color _subColor = new Color(0.741f, 0.706f, 0.627f);
    [SerializeField] private Color _dimColor = new Color(0.224f, 0.267f, 0.416f);
    [SerializeField] private Color _dangerColor = new Color(0.878f, 0.376f, 0.353f);
    [SerializeField] private Color _warnColor = new Color(0.949f, 0.722f, 0.294f);

    private readonly System.Random _random = new System.Random();
    private RectTransform _testControls;
    private RectTransform _sealLayer;

    // SpellGame은 처음 쓰일 때 Resources에서 JSON을 읽으므로 필드 초기화식이 아닌 Awake에서 만든다.
    private SpellGame _game;
    private SpellPatternLayout _layout;
    private Color[] _patternColorValues;
    private RectTransform _root;
    private Font _font;
    private SpellBoardGraphic _boardGraphic;
    private Vector2[] _enemyPositions;
    private RectTransform _dragGhost;
    private RectTransform _aimElbow;
    private string _hintText = "";
    private RectTransform _debugRemote;
    private RectTransform _debugContents;
    private bool _showDebugRemote;
    private int _debugPage;
    private string _debugNotice = "";

    private SpellGame.Card _selectedCard;
    private SpellGame.Placement _selectedPiece;
    private int _selectedBoard = -1;
    private int _rotation;
    private string _message = "";
    private string _notice = "";
    private Color _noticeColor;
    private SpellGame.Phase _renderedPhase = (SpellGame.Phase)(-1);

    private string _feedback = "";
    private Color _feedbackColor;
    private float _feedbackUntil;
    private int _feedbackVersion;
    private int _feedbackShownVersion;
    private int _impactTarget = -1;
    private bool _impactAll;

    private bool _showHelp;
    private int _openPile = -1;
    private bool _showPauseMenu;
    private int _pendingCast = -1;
    private bool _castDragging;
    private int _castDragBoard = -1;
    private int _hoverEnemy = -1;

    private bool _onboardingComplete;
    private bool _onboardingPlacementExplained;
    private string _onboardingStep = "";
    private float _onboardingActivity;
    private RectTransform _onboardingGhost;
    private CanvasGroup _onboardingGhostGroup;
    private RectTransform _onboardingGhostIcon;
    private int _onboardingGhostRotation;
    private Vector2 _onboardingGhostStart;
    private Vector2 _onboardingGhostEnd;

    [Header("망치 프로토타입")]
    [SerializeField, Min(0.1f)] private float _hammerGaugeSeconds = 0.25f;
    [SerializeField, Min(0.1f)] private float _hammerStrikeSeconds = 0.24f;
    [SerializeField, Min(0.2f)] private float _hammerResultSeconds = 1.1f;

    private HammerPhase _hammerPhase;
    private SpellGame.Card _hammerCard;
    private int _hammerTurn;
    private int _hammerBattle;
    private float _hammerTimingStart;
    private float _hammerStrikeStart;
    private float _hammerStrength;
    private string _hammerMessage = "";
    private SpellGame.Shape[] _hammerResultShapes = Array.Empty<SpellGame.Shape>();
    private SpellGame.Card _hammerDragCard;
    private RectTransform _hammerOverlay;
    private RectTransform _hammerVisual;
    private RectTransform _hammerSource;
    private RectTransform _hammerResults;
    private RectTransform _hammerMarker;
    private UnityEngine.UI.Image _hammerGaugeFill;
    private UnityEngine.UI.Text _hammerHint;
    private CanvasGroup _hammerImpact;

    private readonly SpellGame.Card[] _glueCards = new SpellGame.Card[2];
    private readonly Vector2Int[] _gluePositions = { new Vector2Int(2, 1), new Vector2Int(4, 1) };
    private readonly int[] _glueRotations = new int[2];
    private RectTransform _glueOverlay;
    private SpellGlueGraphic _glueGraphic;
    private UnityEngine.UI.Text _glueHint;
    private UnityEngine.UI.Button _glueConfirm;
    private int _glueSelected;
    private int _glueTurn;
    private int _glueBattle;
    private string _glueMessage = "";
    private SpellGame.Card _glueDragCard;
    private int _glueDragRotation;
    private RectTransform _glueCoatingChoice;
    private RectTransform _coatingTooltip;
    private RectTransform _sealTooltip;
    private UnityEngine.UI.Text _coatingTooltipText;
    private UnityEngine.UI.Text _sealTooltipText;
    private UnityEngine.UI.Image _coatingTooltipBar;
    private UnityEngine.UI.Image _sealTooltipBar;
    private bool _isSealerRun;
    private bool _isThiefRun;
    private bool _isCurserRun;

    [Header("도둑손 프로토타입")]
    [SerializeField, Min(0.1f)] private float _theftIntervalSeconds = 15;
    [SerializeField, Min(1f)] private float _theftMoveSpeed = 90;

    private float _theftElapsed;
    private int _theftBattle = -1;
    private bool _theftApproaching;
    private bool _theftWasInteractive;
    private Vector2 _theftPosition;
    private RectTransform _theftHand;

    public SpellGame.Card SelectedCard { get { return _selectedCard; } }
    public int Rotation { get { return _rotation; } }
    public bool IsTableActive { get { return _table != null; } }
    public bool CanInteract { get { return isActiveAndEnabled && _glueOverlay == null && _hammerPhase == HammerPhase.None && !_showDebugRemote && !_showPauseMenu && !_showHelp && _openPile < 0 && _game.CurrentPhase == SpellGame.Phase.Combat; } }

    void Awake()
    {
        _game = new SpellGame();
        _layout = new SpellPatternLayout();
        _patternColorValues = new Color[SpellGame.PatternCount];
        for (int i = 0; i < _patternColorValues.Length; i++) SpellDataStore.ParseColor(SpellGame.PatternColorCode(i), out _patternColorValues[i]);
        _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 24);
        // 꺼 둔 첫 주문 온보딩만 기존 1280×720 좌표로 그린다. 전투 화면 바로 위, 오버레이 아래에 둔다. 새 화면 연결은 다음 작업이다.
        _root = Element("Onboarding Layer", _content, 0, 0, 1280, 720);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = Vector2.zero;
        _root.localScale = Vector3.one * LEGACY_SCALE;
        _root.SetAsLastSibling();
        _boardGraphic = _combatScreen.Board;
        _boardGraphic.Setup(this, _game, _layout);
        SetupCombatScreen();
        _table.Setup(this, _game, _layout, _combatScreen);
        SetupOtherScreens();
        SetupOverlays();
        SetupDebugRemote();
        SetupFeatureControls();
        Render();
    }

    void OnEnable()
    {
        if (_canvas != null && !_canvas.activeSelf)
        {
            _canvas.SetActive(true); _onboardingActivity = Time.unscaledTime; Render();
        }
    }

    void OnDisable()
    {
        _theftWasInteractive = false; CloseGlue(); CloseHammer();
        ClearSelection();
        _pendingCast = -1;
        _openPile = -1;
        _castDragging = false;
        if (_boardGraphic != null) _boardGraphic.CancelDrag();
        HideDragGhost();
        if (_table != null) _table.SetVisible(false);
        if (_canvas != null) _canvas.SetActive(false);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame) { ToggleDebugRemote(); return; }
        if (_showDebugRemote && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { ToggleDebugRemote(); return; }
        if (_showDebugRemote) return;
        UpdateTheft();
        if (_glueOverlay != null) { UpdateGlue(); return; }
        if (_hammerPhase != HammerPhase.None) { UpdateHammer(); return; }
        UpdateFeedback();
        UpdateCursorHint();
        if (_game.CurrentPhase == SpellGame.Phase.Combat && _game.Curse != null) RefreshCurseTint();
        if (ONBOARDING_ENABLED) UpdateOnboarding();
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_combatScreen.MoreMenu.IsOpen) { _combatScreen.MoreMenu.Close(); return; }
            if (_openPile >= 0) _openPile = -1;
            else if (_showPauseMenu) _showPauseMenu = false;
            else OpenPauseState();
            _onboardingActivity = Time.unscaledTime; Render(); return;
        }
        if (_showPauseMenu || _openPile >= 0 || _showHelp) return;
        if (_game.CurrentPhase != SpellGame.Phase.Combat) return;
        if (_table.IsDragging) return;
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) { BeginGlue(); return; }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) { BeginHammer(); return; }
        bool rotate = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        bool rightClick = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        if (!rotate && !rightClick) return;
        if (_boardGraphic.IsDraggingPattern) _boardGraphic.RotatePatternDrag();
        else if (_selectedCard != null)
        {
            _rotation = (_rotation + 1) % 4;
            if (_dragGhost == null) Render();
            else
            {
                _dragGhost.localRotation = Quaternion.Euler(0, 0, -90 * _rotation);
                _boardGraphic.RefreshPreview();
            }
        }
        else if (rightClick && _pendingCast >= 0) { _pendingCast = -1; Render(); }
    }

    /// <summary>일시정지 메뉴를 열면서 조준·선택·드래그를 정리한다.</summary>
    private void OpenPauseState()
    {
        _showHelp = false; _showPauseMenu = true; _pendingCast = -1; _castDragging = false; _message = "";
        ClearSelection(); HideDragGhost();
    }

    /// <summary>전투 화면의 고정 버튼·슬롯에 동작과 입력 대상을 한 번만 연결한다.</summary>
    private void SetupCombatScreen()
    {
        SpellCombatScreen screen = _combatScreen;
        screen.EnsurePatternCount(SpellGame.PatternCount);
        _enemyPositions = new Vector2[screen.Enemies.Length];
        for (int i = 0; i < screen.Enemies.Length; i++) _enemyPositions[i] = ((RectTransform)screen.Enemies[i].transform).anchoredPosition;
        screen.Help.SetAction(() => { if (!CanInteract) return; _showHelp = true; Render(); });
        screen.Menu.SetAction(() => { OpenPauseState(); Render(); });
        screen.DrawPile.Setup("드로우 덱", () => OpenPile(0));
        screen.DiscardPile.Setup("묘지", () => OpenPile(1));
        screen.EndTurn.SetAction(() => { if (CanInteract) EndPlayerTurn(); });
        for (int i = 0; i < screen.Tabs.Length; i++) { int index = i; screen.Tabs[i].Setup(this, i, () => OpenPatternMenu(index)); }
        for (int i = 0; i < screen.CastButtons.Length; i++) screen.CastButtons[i].Setup(this, i);
        for (int i = 0; i < screen.Enemies.Length; i++) screen.Enemies[i].Setup(this, i, ClickEnemy);
        _aimElbow = Instantiate(screen.AimLine, screen.AimLine.parent);
        _aimElbow.name = "Aim Line Elbow";
    }

    /// <summary>일시정지·도움말·더미 열람 오버레이의 버튼 동작을 연결한다.</summary>
    private void SetupOverlays()
    {
        _pauseMenu.Continue.SetAction(() => { _showPauseMenu = false; _onboardingActivity = Time.unscaledTime; Render(); });
        _pauseMenu.Title.SetAction(() => { _game.ReturnToTitle(); _showPauseMenu = false; _showHelp = false; _pendingCast = -1; _feedbackUntil = 0; ClearSelection(); Render(); });
        _pauseMenu.Quit.SetAction(Application.Quit);
        _helpPanel.Close.SetAction(() => { _showHelp = false; Render(); });
    }

    /// <summary>드로우 덱 또는 묘지 열람을 연다.</summary>
    private void OpenPile(int pile)
    {
        if (!CanInteract) return;
        _pendingCast = -1; ClearSelection(); _openPile = pile; Render();
    }

    /// <summary>시전 결과의 적 강조를 시간에 따라 흐리게 한다.</summary>
    private void UpdateFeedback()
    {
        if (_game.CurrentPhase != SpellGame.Phase.Combat) return;
        float remaining = _feedbackUntil - Time.unscaledTime;
        SpellEnemyView[] enemies = _combatScreen.Enemies;
        float alpha = remaining > 0 ? 0.32f * Mathf.Clamp01((remaining - 1.05f) / 0.45f) : 0;
        for (int i = 0; i < enemies.Length; i++) enemies[i].SetImpact(_feedbackColor, (_impactAll || i == _impactTarget) ? alpha : 0);
    }

    /// <summary>선택·조준·도안 이동 중에만 조작 안내를 커서 옆에 표시한다.</summary>
    private void UpdateCursorHint()
    {
        RectTransform hint = _combatScreen.CursorHint;
        bool show = CanInteract && Mouse.current != null && !string.IsNullOrEmpty(_hintText)
            && (_selectedCard != null || _boardGraphic.IsDraggingPattern || _pendingCast >= 0 || _castDragging);
        if (hint.gameObject.activeSelf != show) hint.gameObject.SetActive(show);
        if (!show) return;
        UnityEngine.UI.Text label = _combatScreen.CursorHintText;
        if (label.text != _hintText) label.text = _hintText;
        Vector2 point = ToLayer(Mouse.current.position.ReadValue());
        Rect area = _combatScreen.DragLayer.rect;
        float width = label.preferredWidth + CURSOR_HINT_PADDING;
        hint.sizeDelta = new Vector2(width, hint.sizeDelta.y);
        hint.anchoredPosition = new Vector2(Mathf.Min(point.x + CURSOR_HINT_OFFSET, area.width - width), Mathf.Max(point.y - CURSOR_HINT_OFFSET, -area.height + hint.sizeDelta.y));
    }

    /// <summary>주문 결과를 기록해 화면이 갱신된 뒤에도 일시적으로 표시한다.</summary>
    private void ShowFeedback(string message, Color color)
    {
        _feedback = message; _feedbackColor = color; _feedbackUntil = Time.unscaledTime + 1.5f; _feedbackVersion++;
    }

    /// <summary>현재 선택과 회전 상태를 해제한다.</summary>
    private void ClearSelection() { _selectedCard = null; _selectedPiece = null; _selectedBoard = -1; _rotation = 0; }

    /// <summary>지정 위치와 크기의 화면 요소를 생성한다.</summary>
    private RectTransform Element(string name, RectTransform parent, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
    }

    /// <summary>패널 배경을 만들고 위치를 반환한다.</summary>
    private RectTransform Panel(RectTransform parent, float x, float y, float width, float height, Color color)
    {
        RectTransform rect = Element("Panel", parent, x, y, width, height);
        rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = color; return rect;
    }

    /// <summary>한글 텍스트를 지정 영역에 표시한다.</summary>
    private void Label(RectTransform parent, string value, float x, float y, float width, float height, int size = 19, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        RectTransform rect = Element("Text", parent, x, y, width, height);
        UnityEngine.UI.Text text = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
        text.font = _font; text.text = value; text.fontSize = size; text.alignment = alignment; text.color = new Color(0.92f, 0.94f, 0.96f); text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
    }

    /// <summary>텍스트와 클릭 동작을 가진 버튼을 만든다.</summary>
    private RectTransform Button(RectTransform parent, string caption, float x, float y, float width, float height, Action action, Color? color = null, int fontSize = 18)
    {
        RectTransform rect = Panel(parent, x, y, width, height, color ?? new Color(0.24f, 0.31f, 0.39f));
        UnityEngine.UI.Button button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(() => action()); Label(rect, caption, 4, 2, width - 8, height - 4, fontSize);
        return rect;
    }

    /// <summary>현재 게임 단계에 맞춰 화면을 갱신한다. 전투 화면은 다시 만들지 않고 값만 갱신한다.</summary>
    private void Render()
    {
        if (_pendingCast >= 0 && (_game.CurrentPhase != SpellGame.Phase.Combat || !_game.IsComplete(_pendingCast) || _game.IsSealed(_pendingCast))) _pendingCast = -1;
        CloseGlue(); CloseHammer();
        _boardGraphic.CancelDrag();
        if (_dragGhost != null) ClearSelection();
        HideDragGhost(); HideAim();
        _castDragging = false; _hoverEnemy = -1;
        HideTooltips();
        _combatScreen.MoreMenu.Close();
        SpellGame.Phase phase = _game.CurrentPhase;
        bool combat = phase == SpellGame.Phase.Combat;
        _background.SetActive(!combat);
        bool result = phase == SpellGame.Phase.Victory || phase == SpellGame.Phase.Defeat;
        _combatScreen.gameObject.SetActive(combat);
        _table.SetVisible(combat);
        _titleScreen.gameObject.SetActive(phase == SpellGame.Phase.Title);
        _rewardScreen.gameObject.SetActive(phase == SpellGame.Phase.Reward);
        _loadoutScreen.gameObject.SetActive(phase == SpellGame.Phase.Loadout);
        _resultScreen.gameObject.SetActive(result);
        if (combat) RefreshCombat();
        else if (phase == SpellGame.Phase.Reward) RefreshRewards();
        else if (phase == SpellGame.Phase.Loadout) RefreshLoadout();
        else if (result) _resultScreen.Bind(phase == SpellGame.Phase.Victory ? "승리" : "패배");
        RenderOnboardingLayer(combat);
        RefreshFeatureControls();
        RefreshOverlays();
        _renderedPhase = phase;
    }

    /// <summary>타이틀 시작·결과 다시 시작·보상 건너뛰기 버튼의 동작을 연결한다.</summary>
    private void SetupOtherScreens()
    {
        _titleScreen.StartButton.SetAction(StartNewGame);
        _resultScreen.Restart.SetAction(() => StartRun(_isSealerRun, _isThiefRun, _isCurserRun));
        _rewardScreen.Skip.SetAction(() => ChooseReward(-1));
        _loadoutScreen.Setup(_layout, _game.OwnedSpells, PatternColor, StartLoadoutBattle);
    }

    /// <summary>장착 화면에 들어올 때 새로 얻은 도안을 빈자리에 먼저 놓아 보고 장착 화면을 갱신한다.</summary>
    private void RefreshLoadout()
    {
        if (_renderedPhase != SpellGame.Phase.Loadout && _game.LastAcquiredPattern >= 0) _layout.TryAutoPlace(_game.LastAcquiredPattern, PlacedOwned());
        _loadoutScreen.Refresh();
    }

    /// <summary>보유 도안 중 격자에 놓인 도안 목록을 반환한다.</summary>
    private List<int> PlacedOwned()
    {
        List<int> placed = new List<int>();
        foreach (int spell in _game.OwnedSpells) if (_layout.IsPlaced(spell)) placed.Add(spell);
        return placed;
    }

    /// <summary>격자에 놓인 도안을 장착해 다음 전투를 시작한다. 하나도 없으면 안내만 갱신한다.</summary>
    private void StartLoadoutBattle()
    {
        List<int> equipped = PlacedOwned();
        if (!_game.StartNextBattle(equipped)) { _loadoutScreen.ShowHint("도안을 하나 이상 격자에 놓아 주세요"); return; }
        ClearSelection(); _message = ""; Render();
    }

    /// <summary>새 게임을 시작하고 도안 배치와 선택을 초기화한다.</summary>
    private void StartNewGame()
    {
        ResetTheft(); _isSealerRun = _isThiefRun = _isCurserRun = false;
        _game.StartGame(); ResetPatterns(); ClearSelection(); Render();
    }

    /// <summary>개발자 전투를 시작하고 판에 들어가는 만큼 모든 도안을 배치한다.</summary>
    private void StartDeveloperGame()
    {
        ResetTheft(); _isSealerRun = _isThiefRun = _isCurserRun = false;
        _game.StartGame(true); ResetPatterns();
        _layout.EnsurePlaced(_game.Spells); ClearSelection(); Render();
    }

    /// <summary>타이틀과 전투 화면에 개발자 시작과 리모콘 버튼을 연결한다.</summary>
    private void SetupDebugRemote()
    {
        RectTransform title = (RectTransform)_titleScreen.transform;
        Button(title, "개발자 모드 시작", 20, 20, 240, 48, StartDeveloperGame);
        RectTransform combat = (RectTransform)_combatScreen.transform;
        Button(combat, "리모콘 (F2)", 20, 20, 180, 44, ToggleDebugRemote);
        Button(combat, "◀ 손패", 20, 75, 120, 40, () => { if (CanInteract) _table.ChangeHandPage(-1); });
        Button(combat, "손패 ▶", 145, 75, 120, 40, () => { if (CanInteract) _table.ChangeHandPage(1); });
        Label(combat, "방향키: 판 이동", 20, 120, 245, 30, 16, TextAnchor.MiddleLeft);
        _debugRemote = Panel(_content, 0, 0, 1920, 1080, new Color(0, 0, 0, 0.55f));
        _debugRemote.SetAsLastSibling();
        _debugContents = Panel(_debugRemote, 220, 120, 1480, 850, new Color(0.05f, 0.07f, 0.12f, 0.97f));
        _debugRemote.gameObject.SetActive(false);
    }

    /// <summary>리모콘을 전환하고 진행 중인 선택과 조준을 취소한다.</summary>
    private void ToggleDebugRemote()
    {
        _showDebugRemote = !_showDebugRemote;
        if (_showDebugRemote)
        {
            _table.CancelInteractions(); _boardGraphic.CancelDrag(); ClearSelection(); HideDragGhost();
            _pendingCast = -1; _castDragging = false; _debugPage = 0; _debugNotice = "";
            RefreshDebugRemote();
        }
        _debugRemote.gameObject.SetActive(_showDebugRemote);
    }

    /// <summary>리모콘의 조각, 도안, 격자와 덱 편집 버튼을 갱신한다.</summary>
    private void RefreshDebugRemote()
    {
        foreach (Transform child in _debugContents) Destroy(child.gameObject);
        Label(_debugContents, "개발자 리모콘", 20, 10, 400, 40, 26, TextAnchor.MiddleLeft);
        Button(_debugContents, "닫기", 1320, 10, 130, 40, ToggleDebugRemote);
        if (_game.CurrentPhase != SpellGame.Phase.Combat)
        {
            Label(_debugContents, "전투 중에 사용할 수 있습니다", 40, 80, 600, 50, 22, TextAnchor.MiddleLeft);
            return;
        }
        Label(_debugContents, "조각 추가", 20, 60, 300, 35, 20, TextAnchor.MiddleLeft);
        for (int i = 0; i < SpellGame.DebugPieces.Count; i++)
        {
            int index = i; SpellGame.Card card = SpellGame.DebugPieces[i]; string traits = TraitText(card);
            Button(_debugContents, SpellGame.ShapeName(card.Shape) + (traits.Length == 0 ? "" : " / " + traits), 20 + i % 4 * 240, 100 + i / 4 * 45, 230, 40,
                () => { _game.AddDebugCard(index); Render(); RefreshDebugRemote(); }, null, 16);
        }
        Label(_debugContents, "도안 추가", 1000, 60, 250, 35, 20, TextAnchor.MiddleLeft);
        for (int i = 0; i < SpellGame.PatternCount; i++)
        {
            int spell = i;
            Button(_debugContents, SpellGame.PatternName(i), 1000 + i % 2 * 210, 100 + i / 2 * 45, 200, 40,
                () =>
                {
                    if (_game.Spells.Contains(spell)) _debugNotice = "이미 장착한 도안입니다.";
                    else if (!_layout.TryAutoPlace(spell, _game.Spells)) _debugNotice = "빈 공간이 부족합니다.";
                    else { _game.AddDebugPattern(spell); _debugNotice = "도안을 배치했습니다."; }
                    Render(); RefreshDebugRemote();
                });
        }
        Label(_debugContents, _debugNotice, 1000, 350, 410, 35, 16, TextAnchor.MiddleLeft);
        Label(_debugContents, $"덱 {_game.OwnedCards.Count}장 · 손패 {_game.Hand.Count}장", 20, 390, 1000, 35, 20, TextAnchor.MiddleLeft);
        int maxPage = Mathf.Max(0, (_game.OwnedCards.Count - 1) / 16); _debugPage = Mathf.Clamp(_debugPage, 0, maxPage);
        Button(_debugContents, "이전", 1050, 390, 120, 40, () => { _debugPage--; RefreshDebugRemote(); });
        Button(_debugContents, $"다음 {_debugPage + 1}/{maxPage + 1}", 1180, 390, 230, 40, () => { _debugPage++; RefreshDebugRemote(); });
        for (int i = _debugPage * 16; i < Mathf.Min(_game.OwnedCards.Count, (_debugPage + 1) * 16); i++)
        {
            SpellGame.Card card = _game.OwnedCards[i]; int slot = i - _debugPage * 16;
            Button(_debugContents, "삭제 " + DisplayName(card), 20 + slot % 4 * 345, 445 + slot / 4 * 60, 330, 50,
                () => { _game.RemoveDebugCard(card); Render(); RefreshDebugRemote(); }, null, 16);
        }
    }

    /// <summary>보상을 고르거나(-1은 건너뛰기) 다음 전투로 넘어간다.</summary>
    private void ChooseReward(int reward)
    {
        if (_game.ChooseReward(reward)) { ClearSelection(); _message = ""; Render(); }
    }

    /// <summary>꺼 둔 첫 주문 온보딩 층을 비우고, 켜져 있으면 전투 중에만 다시 그린다.</summary>
    private void RenderOnboardingLayer(bool combat)
    {
        _root.gameObject.SetActive(true);
    }

    /// <summary>일시정지·도움말·더미 열람 오버레이를 상태에 맞게 켜고 내용을 갱신한다.</summary>
    private void RefreshOverlays()
    {
        _helpPanel.gameObject.SetActive(_showHelp);
        _pauseMenu.gameObject.SetActive(_showPauseMenu);
        _pileViewer.gameObject.SetActive(_openPile >= 0);
        if (_openPile < 0) return;
        List<string> lines = new List<string>();
        foreach (KeyValuePair<SpellGame.Card, int> entry in _game.GetPileSummary(_openPile == 0))
            lines.Add(CardName(entry.Key).Replace("\n", " · ") + "  × " + entry.Value);
        lines.Sort(StringComparer.Ordinal);
        System.Text.StringBuilder body = new System.Text.StringBuilder();
        foreach (string line in lines) body.Append(line).Append('\n');
        if (lines.Count == 0) body.Append("비어 있습니다");
        _pileViewer.Bind(_openPile == 0 ? "드로우 덱" : "묘지", body.ToString(), () => { _openPile = -1; Render(); });
    }

    /// <summary>전투 화면의 모든 뷰에 현재 상태를 넣는다.</summary>
    private void RefreshCombat()
    {
        SpellCombatScreen screen = _combatScreen;
        EnsurePatterns();
        screen.Battle.text = $"전투 {_game.Battle}/{BATTLE_COUNT}";
        for (int i = 0; i < screen.ProgressDots.Length; i++)
            screen.ProgressDots[i].color = i < _game.Battle - 1 ? _subColor : i == _game.Battle - 1 ? _textColor : _dimColor;
        int incoming = 0;
        foreach (SpellGame.Enemy enemy in _game.Enemies) if (enemy.Health > 0) incoming += enemy.Intent;
        screen.PlayerStatus.Bind(_game.Health, _game.MaxHealth, _game.Block, Math.Max(0, incoming - _game.Block) + (_game.Curse != null && !_game.Curse.IsResolved ? _game.Curse.Amount : 0));
        screen.Placement.gameObject.SetActive(false);
        screen.BoardCount.gameObject.SetActive(false);
        _boardGraphic.MarkStateDirty();
        RefreshPatternControls();
        RefreshPreviewBar();
        RefreshEnemies();
        screen.DrawPile.Bind(_game.DrawCount);
        screen.DiscardPile.Bind(_game.DiscardCount);
        for (int i = 0; i < screen.Hand.Length; i++)
        {
            bool has = !IsTableActive && i < _game.Hand.Count;
            screen.Hand[i].gameObject.SetActive(has);
            if (!has) continue;
            SpellGame.Card card = _game.Hand[i];
            screen.Hand[i].Bind(this, card, DisplayName(card), TraitText(card), AccentColor(card), false, card == _selectedCard && _selectedPiece == null, SelectHandCard);
        }
        screen.EndTurn.SetLabel($"{_game.Turn}턴 종료");
        if (!string.IsNullOrEmpty(_message)) { screen.Toast.Show(_message, MESSAGE_POSITION, TOAST_SECONDS, _warnColor, true); _message = _notice = ""; }
        else if (!string.IsNullOrEmpty(_notice)) { screen.Toast.Show(_notice, MESSAGE_POSITION, TOAST_SECONDS, _noticeColor, false); _notice = ""; }
        else if (Time.unscaledTime < _feedbackUntil && _feedbackShownVersion != _feedbackVersion)
        {
            screen.Toast.Show(_feedback, FEEDBACK_POSITION, _feedbackUntil - Time.unscaledTime, _feedbackColor, false);
            _feedbackShownVersion = _feedbackVersion;
        }
        SetPreviewHint(-1, Vector2Int.zero);
        _table.Sync();
    }

    /// <summary>3D 판에서 모델 배치가 바뀌면 기존 선택과 화면 상태를 갱신한다.</summary>
    public void TableChanged()
    {
        ClearSelection(); Render();
    }

    /// <summary>3D 판 위 화면 위치를 소수 격자 좌표로 바꾸고 교차 여부를 반환한다.</summary>
    public bool TryTableGrid(Vector2 position, out Vector2 grid)
    {
        return _table.ScreenToGrid(position, out grid);
    }

    /// <summary>도안 탭과 완성 도안의 시전 버튼을 도안 위치·회전에 맞춰 놓는다.</summary>
    private void RefreshPatternControls()
    {
        float cell = _boardGraphic.CellSize;
        for (int i = 0; i < _combatScreen.Tabs.Length; i++)
        {
            bool show = _game.Spells.Contains(i) && _layout.IsPlaced(i);
            _combatScreen.Tabs[i].gameObject.SetActive(show);
            bool castable = show && _game.IsComplete(i) && !_game.IsSealed(i);
            _combatScreen.CastButtons[i].gameObject.SetActive(castable);
            if (!show) continue;
            Rect bounds = PatternBounds(i, cell);
            Vector2[] candidates = { new Vector2(bounds.xMin, -bounds.yMin + TAB_HEIGHT), new Vector2(bounds.xMin, -bounds.yMax), new Vector2(bounds.xMin - cell, -bounds.yMin), new Vector2(bounds.xMax, -bounds.yMin) };
            Vector2 position = candidates[0];
            foreach (Vector2 candidate in candidates) if (IsTabPositionFree(i, candidate, cell)) { position = candidate; break; }
            _combatScreen.Tabs[i].Bind(SpellGame.PatternName(i) + (_game.IsSealed(i) ? " [봉인]" : ""), PatternColor(i), position, cell);
            if (castable) _combatScreen.CastButtons[i].Bind(PatternColor(i), _pendingCast == i || (_castDragging && _castDragBoard == i), PatternCenter(i, cell), FormulaText(i));
        }
        _table.ProjectControls();
    }

    /// <summary>회전된 도안의 실제 삼각 구역을 포함하는 격자 기준 픽셀 경계를 반환한다.</summary>
    private Rect PatternBounds(int spell, float cell)
    {
        float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            int mask = _layout.PlacedMask(spell, x, y);
            for (int sector = 0; sector < 4; sector++)
            {
                if ((mask & (1 << sector)) == 0) continue;
                left = Mathf.Min(left, (x + (sector == 1 ? 0.5f : 0f)) * cell);
                right = Mathf.Max(right, (x + (sector == 3 ? 0.5f : 1f)) * cell);
                top = Mathf.Min(top, (y + (sector == 2 ? 0.5f : 0f)) * cell);
                bottom = Mathf.Max(bottom, (y + (sector == 0 ? 0.5f : 1f)) * cell);
            }
        }
        return Rect.MinMaxRect(left, top, right, bottom);
    }

    /// <summary>이름표 후보가 다른 도안과 이미 놓인 이름표에 겹치지 않는지 반환한다.</summary>
    private bool IsTabPositionFree(int spell, Vector2 position, float cell)
    {
        Rect candidate = new Rect(position.x, -position.y, cell, TAB_HEIGHT);
        foreach (int other in _game.Spells)
        {
            if (other == spell || !_layout.IsPlaced(other)) continue;
            for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
                if (_layout.PlacedMask(other, x, y) != 0 && candidate.Overlaps(new Rect(x * cell, y * cell, cell, cell))) return false;
            if (other < spell && _combatScreen.Tabs[other].gameObject.activeSelf)
            {
                RectTransform tab = _combatScreen.Tabs[other].Rect;
                if (candidate.Overlaps(new Rect(tab.anchoredPosition.x, -tab.anchoredPosition.y, tab.sizeDelta.x, tab.sizeDelta.y))) return false;
            }
        }
        return true;
    }

    /// <summary>지정 줄의 열 범위를 다른 배치 도안이 쓰는지 반환한다.</summary>
    private bool RowUsedByOther(int spell, int row, int fromX, int toX)
    {
        for (int i = 0; i < _game.Spells.Count; i++)
        {
            int other = _game.Spells[i];
            if (other == spell) continue;
            for (int x = fromX; x < toX; x++) if (_layout.PlacedMask(other, x, row) != 0) return true;
        }
        return false;
    }

    /// <summary>도안이 차지한 구역 수로 가중한 칸 중심의 평균을 격자 기준 좌표(y 아래가 음수)로 반환한다.</summary>
    private Vector2 PatternCenter(int spell, float cell)
    {
        Vector2 sum = Vector2.zero; int weight = 0;
        for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
        {
            int count = BitCount(_layout.PlacedMask(spell, x, y));
            sum += new Vector2(x + 0.5f, y + 0.5f) * count; weight += count;
        }
        Vector2 center = weight > 0 ? sum / weight : Vector2.zero;
        return new Vector2(center.x * cell, -center.y * cell);
    }

    /// <summary>4비트 구역 마스크에서 켜진 구역 수를 반환한다.</summary>
    private static int BitCount(int mask)
    {
        int count = 0;
        for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) count++;
        return count;
    }

    /// <summary>조준 중·선택 중·완성된 도안 순으로 하나를 골라 고정 미리보기에 계산식을 표시한다.</summary>
    private void RefreshPreviewBar()
    {
        _combatScreen.Preview.gameObject.SetActive(false);
    }

    /// <summary>도안의 빈 구역을 1/4칸 단위 문구로 반환한다.</summary>
    private string RemainingText(int spell)
    {
        int left = Math.Max(0, SpellGame.PatternArea(spell) - _game.FilledArea(spell));
        if (left % 4 == 0) return $"{left / 4}칸 남음";
        return left < 4 ? $"{left}/4칸 남음" : $"{left / 4}칸 {left % 4}/4 남음";
    }

    /// <summary>도안을 지금 시전했을 때의 예상 수식을 한 줄로 만든다. 예: 산탄 · 4타 × (2 + 증폭 1) = 무작위 12</summary>
    public string FormulaText(int spell)
    {
        SpellGame.SpellEvaluation e = _game.Evaluate(spell);
        string terms = "";
        foreach (string term in e.Terms) terms += " + " + term.Replace(" +", " ");
        string main;
        if (e.Kind == SpellGame.SpellKind.Scatter) main = e.Terms.Count > 0 ? $"{e.Hits}타 × ({e.Base}{terms}) = 무작위 {e.Total}" : $"{e.Hits}타 × {e.Base} = 무작위 {e.Total}";
        else
        {
            string word = e.Kind == SpellGame.SpellKind.Block ? "방어" : e.Kind == SpellGame.SpellKind.All ? "전체" : "피해";
            main = e.Terms.Count > 0 ? $"{word} {e.Base}{terms} = {e.Value}" : $"{word} {e.Value}";
        }
        List<string> extras = new List<string>();
        int guard = e.Block - (e.Kind == SpellGame.SpellKind.Block ? e.Value : 0);
        if (guard > 0) extras.Add($"방어 +{guard}");
        if (e.ShardHits > 0) extras.Add($"파편 {e.ShardHits}타");
        if (e.Draws > 0) extras.Add($"보충 {e.Draws}장");
        if (e.Recalls > 0) extras.Add($"회수 {e.Recalls}장");
        return extras.Count > 0 ? main + "  ·  " + string.Join(" · ", extras) : main;
    }

    /// <summary>도안의 예상 수식과 미완성 안내를 툴팁용 문구로 만든다.</summary>
    private string BuildFormula(int spell, bool complete, out string detail)
    {
        detail = complete ? "" : "빈 구역을 채우면 시전할 수 있습니다";
        return SpellGame.PatternDescription(spell) + "\n예상 " + FormulaText(spell);
    }

    /// <summary>적 슬롯을 적 수에 맞춰 배치하고 상태·대상·조준 미리보기를 갱신한다.</summary>
    private void RefreshEnemies()
    {
        SpellEnemyView[] views = _combatScreen.Enemies;
        int count = Mathf.Clamp(_game.Enemies.Count, 1, views.Length);
        bool aiming = _pendingCast >= 0 || _castDragging;
        int castBoard = _pendingCast >= 0 ? _pendingCast : _castDragBoard;
        for (int i = 0; i < views.Length; i++)
        {
            bool show = i < _game.Enemies.Count;
            views[i].gameObject.SetActive(show);
            if (!show) continue;
            SpellGame.Enemy enemy = _game.Enemies[i];
            ((RectTransform)views[i].transform).anchoredPosition = count == 1 ? _enemyPositions[1] : count == 2 ? Vector2.Lerp(_enemyPositions[i], _enemyPositions[i + 1], 0.5f) : _enemyPositions[i];
            views[i].SetWidth(count >= 3 ? ENEMY_NARROW_BAR_WIDTH : ENEMY_BAR_WIDTH);
            bool boss = enemy.MaxHealth >= 60, large = enemy.MaxHealth >= 26;
            Vector2 size = boss ? BOSS_SIZE : large ? LARGE_ENEMY_SIZE : NORMAL_ENEMY_SIZE;
            Color color = _enemyColors[boss ? 2 : large ? 1 : 0];
            string pattern = enemy.Pattern.Count > 1 ? "패턴 " + string.Join(" · ", enemy.Pattern) + " 반복" : "";
            int preview = aiming && i == _hoverEnemy && enemy.Health > 0 ? _game.GetSpellDamage(castBoard) : 0;
            views[i].Bind(enemy.Name, enemy.Health, enemy.MaxHealth, enemy.Intent, pattern, color, size, boss, i == _game.Target, aiming, preview);
        }
    }

    /// <summary>적 클릭으로 조준 중 주문을 시전하거나 다음 공격 대상을 고른다.</summary>
    private void ClickEnemy(int index)
    {
        if (!CanInteract) return;
        if (_pendingCast >= 0) CastSpell(_pendingCast, index);
        else { _game.SelectTarget(index); Render(); }
    }

    /// <summary>손패 카드를 클릭해 선택한다.</summary>
    private void SelectHandCard(SpellGame.Card card)
    {
        if (!CanInteract) return;
        _pendingCast = -1; _selectedCard = card; _selectedPiece = null; _selectedBoard = -1; _rotation = 0;
        Render();
    }

    /// <summary>도안 탭의 더보기 메뉴를 탭 오른쪽 아래에 연다.</summary>
    private void OpenPatternMenu(int spell)
    {
        if (!CanInteract) return;
        Vector3[] corners = new Vector3[4];
        _combatScreen.Tabs[spell].Rect.GetWorldCorners(corners);
        // 탭의 오른쪽 아래 모서리에 메뉴의 오른쪽 위를 맞춘다.
        Vector2 bottomRight = ToLayer(RectTransformUtility.WorldToScreenPoint(null, corners[3]));
        _combatScreen.MoreMenu.Open(new Vector2(bottomRight.x, bottomRight.y - MENU_GAP), () => AbandonPattern(spell));
    }

    /// <summary>도안을 비워 이번 턴 조각은 손패로, 이전 턴 조각은 묘지로 보낸다.</summary>
    private void AbandonPattern(int spell)
    {
        if (!_game.Abandon(spell)) _message = "손패가 가득 차 비울 수 없습니다";
        else { ClearSelection(); _message = ""; }
        Render();
    }

    /// <summary>화면 좌표를 전투 화면 끌기 층의 왼쪽 위 기준 좌표(y 아래가 음수)로 바꾼다.</summary>
    private Vector2 ToLayer(Vector2 screenPosition)
    {
        RectTransform layer = _combatScreen.DragLayer;
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screenPosition, null, out local);
        return new Vector2(local.x - layer.rect.xMin, local.y - layer.rect.yMax);
    }

    /// <summary>첫 주문까지 현재 가능한 조작을 강조하고 설명과 고스트를 준비한다. 기존 좌표를 쓰며 새 화면 연결은 다음 작업이다.</summary>
    private void RenderOnboarding()
    {
        List<Rect> holes = new List<Rect>(); string step; string message; SpellGame.Card ghostCard = null; int ghostRotation = 0; bool showContinue = false;
        Vector2 start, end;
        int complete = -1;
        foreach (int spell in _game.Spells) if (_game.IsComplete(spell)) { complete = spell; break; }
        if (_pendingCast >= 0)
        {
            int target = 0;
            while (target < _game.Enemies.Count && _game.Enemies[target].Health <= 0) target++;
            float x = 640 - _game.Enemies.Count * 125 + target * 250;
            holes.Add(new Rect(x, 15, 235, 230)); start = end = new Vector2(x + 117, 125);
            step = "target"; message = "이 주문은 선택한 적 하나에게 효과를 적용합니다.\n공격할 적을 선택하세요.";
        }
        else if (!_onboardingPlacementExplained && _game.CraftCount > 0)
        {
            holes.Add(new Rect(30, 570, 190, 30)); start = end = Vector2.zero;
            step = "placements"; showContinue = true;
            message = "한 턴에 기본 3회 조각을 배치할 수 있습니다.\n시전하지 않은 조각은 다음 턴에도 도안에 남습니다.";
        }
        else if (complete >= 0)
        {
            float x = _layout.Origin(complete).x < 3 ? 230 : 900;
            float y = 280 + complete * 32;
            holes.Add(new Rect(x, y, 150, 28)); start = end = new Vector2(x + 75, y + 14);
            step = "cast" + complete; message = "도안이 완성되어 주문이 준비됐습니다.\n시전 버튼을 눌러 주문을 사용하세요.";
        }
        else
        {
            int cardIndex = -1, board = -1, cellX = 0, cellY = 0, rotation = 0;
            if (_game.Hand.Count > 0)
            {
                for (int pass = 0; pass < 2 && cardIndex < 0; pass++)
                    for (int i = 0; i < _game.Hand.Count && cardIndex < 0; i++)
                    {
                        SpellGame.Card candidate = _game.Hand[i];
                        if (_selectedCard != null && _selectedPiece == null && candidate != _selectedCard) continue;
                        SpellGame.Shape preferred = _game.CraftCount == 0 ? SpellGame.Shape.Square : SpellGame.Shape.QuarterTriangle;
                        if (pass == 0 && candidate.Shape != preferred) continue;
                        foreach (int spell in _game.Spells)
                        {
                            if (!_layout.IsPlaced(spell)) continue;
                            for (int y = 0; y < 3 && cardIndex < 0; y++) for (int x = 0; x < 3 && cardIndex < 0; x++) for (int r = 0; r < 4; r++)
                                if (_game.CanPlace(spell, candidate, x, y, r)) { cardIndex = i; board = spell; cellX = x; cellY = y; rotation = r; break; }
                            if (cardIndex >= 0) break;
                        }
                    }
            }
            if (cardIndex < 0)
            {
                holes.Add(new Rect(1080, 635, 160, 65)); start = end = new Vector2(1160, 667);
                step = "turn"; message = "턴을 종료하면 적이 공격하고 새 턴에 조각을 뽑습니다.\n주문을 이어서 완성하세요.";
            }
            else
            {
                ghostCard = _game.Hand[cardIndex]; ghostRotation = rotation;
                float cardX = 350 + (7 - _game.Hand.Count) * 25 + cardIndex * 85;
                Vector2Int gridCell = _layout.LocalToGrid(board, new Vector2Int(cellX, cellY));
                float gridX = 390 + gridCell.x * LEGACY_CELL, gridY = 280 + gridCell.y * LEGACY_CELL;
                holes.Add(new Rect(cardX, 565, 81, 115)); holes.Add(new Rect(gridX, gridY, LEGACY_CELL, LEGACY_CELL));
                start = new Vector2(cardX + 40, 622); end = new Vector2(gridX + 42, gridY + 42);
                step = "place" + ghostCard.Id + ":" + board + ":" + cellX + ":" + cellY + ":" + rotation;
                message = "도안에 조각을 채우면 주문을 시전할 수 있습니다.\n손패에서 조각을 선택해 도안의 빈칸으로 드래그하세요." + ((_selectedCard == ghostCard ? _rotation : 0) != rotation ? "\nR 또는 우클릭으로 회전해 빈 영역에 맞추세요." : "");
            }
        }
        if (_onboardingStep != step) { _onboardingStep = step; _onboardingActivity = Time.unscaledTime; }
        List<float> xs = new List<float> { 0, 1280 }, ys = new List<float> { 0, 720 };
        foreach (Rect hole in holes) { xs.Add(hole.xMin); xs.Add(hole.xMax); ys.Add(hole.yMin); ys.Add(hole.yMax); }
        xs.Sort(); ys.Sort();
        for (int y = 0; y < ys.Count - 1; y++) for (int x = 0; x < xs.Count - 1; x++)
        {
            float width = xs[x + 1] - xs[x], height = ys[y + 1] - ys[y];
            if (width <= 0 || height <= 0) continue;
            Vector2 center = new Vector2(xs[x] + width / 2, ys[y] + height / 2);
            bool exposed = false; foreach (Rect hole in holes) if (hole.Contains(center)) { exposed = true; break; }
            if (!exposed) Panel(_root, xs[x], ys[y], width, height, new Color(0, 0, 0, 0.76f)).GetComponent<UnityEngine.UI.Image>().raycastTarget = showContinue;
        }
        const float popupWidth = 460, popupHeight = 132;
        Vector2 popupPosition = OnboardingPopupPosition(holes, popupWidth, popupHeight);
        RectTransform popup = Panel(_root, popupPosition.x, popupPosition.y, popupWidth, popupHeight, new Color(0.12f, 0.19f, 0.25f, 0.98f));
        popup.GetComponent<UnityEngine.UI.Image>().raycastTarget = showContinue;
        Label(popup, "주문 시전 안내", 16, 6, popupWidth - 32, 25, 17, TextAnchor.MiddleLeft);
        Label(popup, message, 16, 31, popupWidth - 32, showContinue ? 58 : 94, 16, TextAnchor.MiddleLeft);
        if (showContinue)
        {
            Button(popup, "확인", popupWidth - 110, 96, 94, 30, () => { _onboardingPlacementExplained = true; Render(); }, null, 15);
            start = end = new Vector2(popupPosition.x + popupWidth - 63, popupPosition.y + 111);
        }
        _onboardingGhostStart = start; _onboardingGhostEnd = end;
        _onboardingGhostRotation = ghostRotation;
        _onboardingGhost = Element("Onboarding Ghost", _root, start.x, start.y, 48, 48);
        _onboardingGhost.pivot = new Vector2(0.5f, 0.5f);
        _onboardingGhostGroup = _onboardingGhost.gameObject.AddComponent<CanvasGroup>();
        _onboardingGhostGroup.blocksRaycasts = false; _onboardingGhostGroup.interactable = false; _onboardingGhostGroup.alpha = 0;
        if (ghostCard != null)
        {
            float width = ghostCard.Shape == SpellGame.Shape.Rectangle ? 72 : 36;
            RectTransform iconRect = Element("Ghost Shape", _onboardingGhost, 0, 0, width, 36);
            iconRect.gameObject.AddComponent<CanvasRenderer>();
            SpellShapeGraphic icon = iconRect.gameObject.AddComponent<SpellShapeGraphic>();
            icon.Shape = ghostCard.Shape; icon.color = new Color(0.6f, 1f, 0.85f, 0.75f); icon.raycastTarget = false;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            _onboardingGhostIcon = iconRect;
        }
        Label(_onboardingGhost, "☛", 20, 17, 28, 30, 24);
    }

    /// <summary>강조 영역들을 가리지 않고 화면 안에서 첫 강조 영역에 가까운 팝업 좌표를 반환한다.</summary>
    private Vector2 OnboardingPopupPosition(List<Rect> holes, float width, float height)
    {
        Rect focus = holes[0];
        Vector2[] candidates = {
            new Vector2(focus.center.x - width / 2, focus.yMin - height - 12),
            new Vector2(focus.center.x - width / 2, focus.yMax + 12),
            new Vector2(focus.xMin - width - 12, focus.center.y - height / 2),
            new Vector2(focus.xMax + 12, focus.center.y - height / 2)
        };
        Vector2 best = candidates[0]; float bestScore = float.MaxValue;
        foreach (Vector2 candidate in candidates)
        {
            Vector2 position = new Vector2(Mathf.Clamp(candidate.x, 12, 1280 - width - 12), Mathf.Clamp(candidate.y, 12, 720 - height - 12));
            Rect popup = new Rect(position.x, position.y, width, height);
            float score = Vector2.Distance(popup.center, focus.center);
            foreach (Rect hole in holes) if (popup.Overlaps(hole)) score += 10000;
            if (score < bestScore) { bestScore = score; best = position; }
        }
        return best;
    }

    /// <summary>입력 후 대기 시간을 초기화하고 일정 시간이 지나면 안내 동작을 반복한다.</summary>
    private void UpdateOnboarding()
    {
        if (_onboardingComplete || _onboardingGhost == null || _showHelp || _showPauseMenu) return;
        if ((Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.delta.ReadValue().sqrMagnitude > 0)) ||
            (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)) _onboardingActivity = Time.unscaledTime;
        float elapsed = Time.unscaledTime - _onboardingActivity;
        bool visible = elapsed >= 3f && _dragGhost == null;
        _onboardingGhostGroup.alpha = visible ? 0.85f : 0f;
        if (!visible) return;
        float progress = Mathf.Repeat((elapsed - 3f) / 2f, 1f);
        _onboardingGhost.localScale = Vector3.one * (_onboardingGhostStart == _onboardingGhostEnd ? 1f - 0.22f * Mathf.Pow(Mathf.Sin(progress * Mathf.PI), 4) : 1f);
        if (_onboardingGhostIcon != null) _onboardingGhostIcon.localRotation = Quaternion.Euler(0, 0, -90 * _onboardingGhostRotation * Mathf.Clamp01(progress * 3f));
        Vector2 point = Vector2.Lerp(_onboardingGhostStart, _onboardingGhostEnd, Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - 0.3f) * 1.7f)));
        _onboardingGhost.anchoredPosition = new Vector2(point.x, -point.y);
    }

    /// <summary>적 행동 전후 체력과 방어 차이를 안내하고 다음 화면을 표시한다.</summary>
    private void EndPlayerTurn()
    {
        int healthBefore = _game.Health;
        int incoming = 0;
        foreach (SpellGame.Enemy enemy in _game.Enemies) if (enemy.Health > 0) incoming += enemy.Intent;
        int absorbed = Math.Min(_game.Block, incoming);
        _pendingCast = -1; ClearSelection(); _game.EndTurn(); _message = "";
        int damage = healthBefore - _game.Health;
        _impactTarget = -1; _impactAll = false;
        ShowFeedback($"턴 종료 · 저주 HP -{_game.LastCurseDamage} · 방어 {(_game.LastCurseDamage >= healthBefore ? 0 : absorbed)} · 총 HP -{damage}", _dangerColor);
        Render();
    }

    /// <summary>완성된 주문의 시전을 시작하고 단일 대상 주문은 적 선택을 기다린다.</summary>
    private void BeginCast(int board)
    {
        if (_game.IsSealed(board)) { _pendingCast = -1; _message = "봉인된 주문 도안은 시전할 수 없습니다"; Render(); return; }
        if (!_game.IsComplete(board)) { _message = "도안을 먼저 완성하세요"; Render(); return; }
        ClearSelection();
        if (SpellGame.NeedsTarget(board))
        {
            int count = 0, onlyTarget = -1;
            for (int i = 0; i < _game.Enemies.Count; i++) if (_game.Enemies[i].Health > 0) { count++; onlyTarget = i; }
            if (count == 1) CastSpell(board, onlyTarget);
            else { _pendingCast = board; Render(); _hintText = "적을 클릭해 시전 · 우클릭·Esc 취소"; }
        }
        else CastSpell(board, _game.Target);
    }

    /// <summary>완성된 도안과 선택한 적으로 주문을 시전하고 결과를 표시한다.</summary>
    private void CastSpell(int board, int target)
    {
        if (_game.IsSealed(board)) { _pendingCast = -1; _message = "봉인된 주문 도안은 시전할 수 없습니다"; Render(); return; }
        string result = $"{SpellGame.PatternName(board)} 시전  ·  {FormulaText(board)}";
        if (!_game.Cast(board, target)) { _message = "살아 있는 대상을 선택하세요"; Render(); return; }
        _onboardingComplete = true;
        _pendingCast = -1; ClearSelection(); SpellGame.SpellKind kind = SpellGame.PatternKind(board); _impactAll = kind == SpellGame.SpellKind.All || kind == SpellGame.SpellKind.Scatter; _impactTarget = kind == SpellGame.SpellKind.Single ? target : -1;
        _message = ""; ShowFeedback(result, PatternColor(board)); Render();
    }

    /// <summary>완성된 도안 버튼 클릭으로 해당 주문의 시전을 시작한다.</summary>
    public void ClickCast(int board)
    {
        if (CanInteract) BeginCast(board);
    }

    /// <summary>손패 조각의 코팅·씰링 설명을 포인터 좌우에 펼쳐 표시하거나 지운다. 조각 이름은 표시하지 않는다.</summary>
    public void ShowPieceTooltip(SpellGame.Card card, Vector2 screenPosition)
    {
        ShowTextTooltip(null, screenPosition);
        ShowTraitTooltip(card, screenPosition);
    }

    /// <summary>포인터가 가리킨 도안의 주문 효과와 완성 상태를 표시하고, 가리킨 조각이 있으면 코팅·씰링 설명을 좌우에 펼친다.</summary>
    public void ShowPatternTooltip(int spell, Vector2 screenPosition, SpellGame.Card card = null)
    {
        if (spell < 0) { ShowTextTooltip(null, screenPosition); ShowTraitTooltip(null, screenPosition); return; }
        bool complete = _game.IsComplete(spell);
        string detail;
        string formula = BuildFormula(spell, complete, out detail);
        ShowTextTooltip($"{SpellGame.PatternName(spell)} · {(complete ? "완성" : RemainingText(spell))}\n{formula}" + (string.IsNullOrEmpty(detail) ? "" : "\n" + detail), screenPosition);
        ShowTraitTooltip(card, screenPosition);
    }

    /// <summary>지정한 설명을 화면 좌표 근처에 표시하거나 지운다. 드래그·조준 중에는 표시하지 않는다.</summary>
    public void ShowTextTooltip(string message, Vector2 screenPosition)
    {
        if (message == null || !CanShowTooltip) { _combatScreen.Tooltip.Hide(); return; }
        _combatScreen.Tooltip.Show(message, screenPosition);
    }

    private bool CanShowTooltip { get { return CanInteract && _dragGhost == null && _pendingCast < 0 && !_castDragging && !_boardGraphic.IsDraggingPattern; } }

    /// <summary>문구 설명과 좌우 코팅·씰링 설명을 모두 숨긴다.</summary>
    private void HideTooltips()
    {
        _combatScreen.Tooltip.Hide();
        if (_coatingTooltip != null) { _coatingTooltip.gameObject.SetActive(false); _sealTooltip.gameObject.SetActive(false); }
    }

    /// <summary>카드의 코팅(또는 고유)을 포인터 왼쪽, 씰링과 이전 효과를 포인터 오른쪽에 펼쳐 표시한다. 해당 내용이 없는 쪽 패널은 숨긴다.</summary>
    private void ShowTraitTooltip(SpellGame.Card card, Vector2 screenPosition)
    {
        if (card == null || !CanShowTooltip || !HasTraits(card))
        {
            if (_coatingTooltip != null) { _coatingTooltip.gameObject.SetActive(false); _sealTooltip.gameObject.SetActive(false); }
            return;
        }
        if (_coatingTooltip == null) CreateTraitTooltips();
        _coatingTooltip.SetAsLastSibling(); _sealTooltip.SetAsLastSibling();
        string left = "", right = "";
        Color leftColor = _textColor, rightColor = _textColor;
        if (card.IsUnique) { left = $"<b>고유</b>\n{UniqueDescription(card)}"; leftColor = _subColor; }
        else if (card.Coating != SpellGame.Coating.None) { left = $"<b><color=#{ColorUtility.ToHtmlStringRGB(CoatingColor(card.Coating))}>코팅 · {CoatingName(card.Coating)}</color></b>\n{CoatingDescription(card.Coating)}"; leftColor = CoatingColor(card.Coating); }
        List<string> lines = new List<string>();
        foreach (SpellGame.Seal seal in Enum.GetValues(typeof(SpellGame.Seal)))
        {
            int count = seal == SpellGame.Seal.None ? 0 : SpellGame.SealCount(card, seal);
            if (count == 0) continue;
            if (lines.Count == 0) rightColor = SealColor(seal);
            lines.Add($"<b><color=#{ColorUtility.ToHtmlStringRGB(SealColor(seal))}>● 씰링 · {SealName(seal)}{(count > 1 ? $" ×{count}" : "")}</color></b>\n{SealDescription(seal)}");
        }
        foreach (SpellGame.Effect effect in new[] { SpellGame.Effect.Period, SpellGame.Effect.Resonance })
            if (SpellGame.EffectCount(card, effect) > 0) lines.Add($"<b>{EffectName(effect)}</b>\n{EffectDescription(effect)}");
        right = string.Join("\n\n", lines);
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPosition, null, out local);
        Rect area = _root.rect;
        Vector2 pointer = new Vector2(local.x - area.xMin, area.yMax - local.y);
        PlaceTraitPanel(_coatingTooltip, _coatingTooltipText, _coatingTooltipBar, left, leftColor, pointer.x - TRAIT_POINTER_GAP - TRAIT_WIDTH, pointer.y + TRAIT_POINTER_GAP);
        PlaceTraitPanel(_sealTooltip, _sealTooltipText, _sealTooltipBar, right, rightColor, pointer.x + TRAIT_POINTER_GAP, pointer.y + TRAIT_POINTER_GAP);
    }

    /// <summary>설명 패널 하나의 문구·강조색·크기를 정하고 화면 안쪽으로 맞춰 놓는다. 문구가 비어 있으면 숨긴다.</summary>
    private void PlaceTraitPanel(RectTransform panel, UnityEngine.UI.Text text, UnityEngine.UI.Image bar, string value, Color accent, float x, float y)
    {
        panel.gameObject.SetActive(value.Length > 0);
        if (value.Length == 0) return;
        text.text = value; bar.color = accent;
        float height = text.preferredHeight + TRAIT_PADDING * 2 + TRAIT_BAR;
        panel.sizeDelta = new Vector2(TRAIT_WIDTH, height);
        panel.anchoredPosition = new Vector2(Mathf.Clamp(x, 0, 1280 - TRAIT_WIDTH), -Mathf.Clamp(y, 0, 720 - height));
    }

    /// <summary>좌우 설명 패널을 처음 한 번 만든다. 입력은 가로채지 않는다.</summary>
    private void CreateTraitTooltips()
    {
        _coatingTooltip = CreateTraitPanel("Coating Tooltip", out _coatingTooltipText, out _coatingTooltipBar);
        _sealTooltip = CreateTraitPanel("Seal Tooltip", out _sealTooltipText, out _sealTooltipBar);
    }

    /// <summary>위쪽 강조 막대와 여러 줄 설명 문구를 가진 설명 패널을 만든다.</summary>
    private RectTransform CreateTraitPanel(string name, out UnityEngine.UI.Text text, out UnityEngine.UI.Image bar)
    {
        RectTransform panel = Panel(_root, 0, 0, TRAIT_WIDTH, 100, new Color(0.06f, 0.07f, 0.1f, 0.96f));
        panel.name = name; panel.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        RectTransform barRect = Element("Accent", panel, 0, 0, TRAIT_WIDTH, TRAIT_BAR);
        barRect.anchorMax = new Vector2(1, 1); barRect.sizeDelta = new Vector2(0, TRAIT_BAR);
        bar = barRect.gameObject.AddComponent<UnityEngine.UI.Image>(); bar.raycastTarget = false;
        RectTransform textRect = Element("Text", panel, TRAIT_PADDING, TRAIT_BAR + TRAIT_PADDING, TRAIT_WIDTH - TRAIT_PADDING * 2, 10);
        text = textRect.gameObject.AddComponent<UnityEngine.UI.Text>();
        text.font = _font; text.fontSize = 16; text.lineSpacing = 1.1f; text.alignment = TextAnchor.UpperLeft; text.color = new Color(0.92f, 0.94f, 0.96f);
        text.supportRichText = true; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
        panel.gameObject.SetActive(false);
        return panel;
    }

    /// <summary>단일 대상 주문을 놓은 화면 좌표의 살아 있는 적에게 시전한다.</summary>
    public void DropCast(int board, PointerEventData eventData)
    {
        HideAim();
        bool wasDragging = _castDragging;
        _castDragging = false; _hoverEnemy = -1;
        if (!CanInteract || !SpellGame.NeedsTarget(board) || !wasDragging) return;
        int target = EnemyAt(eventData.position, eventData.pressEventCamera);
        if (target >= 0) { CastSpell(board, target); return; }
        _pendingCast = -1;
        _message = "살아 있는 적 위에 놓아 주세요";
        Render();
    }

    /// <summary>화면 좌표 아래의 살아 있는 적 번호를 반환한다. 없으면 -1이다.</summary>
    private int EnemyAt(Vector2 screenPosition, Camera eventCamera)
    {
        for (int i = 0; i < _game.Enemies.Count && i < _combatScreen.Enemies.Length; i++)
            if (_game.Enemies[i].Health > 0 && _combatScreen.Enemies[i].Contains(screenPosition, eventCamera)) return i;
        return -1;
    }

    /// <summary>단일 대상 주문 드래그의 조준 상태를 시작한다.</summary>
    public void BeginCastDrag(int board)
    {
        if (!CanInteract || !SpellGame.NeedsTarget(board)) return;
        HideTooltips();
        ClearSelection();
        _castDragging = true; _castDragBoard = board; _hoverEnemy = -1;
        _hintText = "살아 있는 적에게 놓아 시전하세요";
        RefreshPatternControls(); RefreshPreviewBar(); RefreshEnemies();
    }

    /// <summary>시전 드래그 중 조준선을 그리고 포인터 아래 적의 예상 피해를 갱신한다.</summary>
    public void PreviewCast(int board, PointerEventData eventData)
    {
        if (!CanInteract || !SpellGame.NeedsTarget(board)) return;
        DrawAim(board, eventData.position);
        UpdateHoverEnemy(board, EnemyAt(eventData.position, eventData.pressEventCamera));
    }

    /// <summary>시전 버튼 중심에서 포인터까지 직선 조준선을 그린다.</summary>
    private void DrawAim(int board, Vector2 pointer)
    {
        Vector3[] corners = new Vector3[4];
        _combatScreen.CastButtons[board].Rect.GetWorldCorners(corners);
        Vector2 start = ToLayer(RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) / 2));
        Vector2 end = ToLayer(pointer);
        SetSegment(_combatScreen.AimLine, start, end);
        if (_aimElbow != null) _aimElbow.gameObject.SetActive(false);
    }

    /// <summary>선분 이미지를 두 점 사이에 놓는다. 좌표는 끌기 층의 왼쪽 위 기준이다.</summary>
    private static void SetSegment(RectTransform line, Vector2 from, Vector2 to)
    {
        Vector2 difference = to - from;
        line.gameObject.SetActive(difference.sqrMagnitude > 1f);
        line.anchoredPosition = from;
        line.sizeDelta = new Vector2(difference.magnitude, line.sizeDelta.y);
        line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg);
    }

    /// <summary>조준선을 숨긴다.</summary>
    private void HideAim()
    {
        _combatScreen.AimLine.gameObject.SetActive(false);
        if (_aimElbow != null) _aimElbow.gameObject.SetActive(false);
    }

    /// <summary>가리킨 적이 바뀌면 예상 피해 표시와 안내 문구를 갱신한다.</summary>
    private void UpdateHoverEnemy(int board, int target)
    {
        if (target >= 0)
        {
            SpellGame.Enemy enemy = _game.Enemies[target];
            int damage = _game.GetSpellDamage(board);
            _hintText = $"{enemy.Name} · 피해 {damage} · 체력 {enemy.Health} → {Math.Max(0, enemy.Health - damage)}";
        }
        else _hintText = _castDragging ? "살아 있는 적에게 놓아 시전하세요" : "적을 클릭해 시전 · 우클릭·Esc 취소";
        if (target == _hoverEnemy) return;
        _hoverEnemy = target;
        RefreshEnemies();
    }

    /// <summary>클릭 시전의 대상 선택 중 가리킨 적의 예상 피해를 표시한다.</summary>
    public void PreviewPendingCast(PointerEventData eventData)
    {
        if (_pendingCast >= 0) UpdateHoverEnemy(_pendingCast, EnemyAt(eventData.position, eventData.enterEventCamera));
    }

    /// <summary>대상 포인터가 벗어나면 예상 피해 표시를 지운다.</summary>
    public void ClearPendingPreview()
    {
        if (_pendingCast >= 0 && !_castDragging) UpdateHoverEnemy(_pendingCast, -1);
    }

    /// <summary>새 게임에서 도안 배치를 기본 위치로 되돌린다.</summary>
    private void ResetPatterns()
    {
        _feedbackUntil = 0;
        _showHelp = false;
        _showPauseMenu = false;
        _onboardingComplete = false;
        _onboardingPlacementExplained = true;
        _onboardingStep = ""; _onboardingActivity = Time.unscaledTime;
        _pendingCast = -1;
        _layout.Reset();
    }

    /// <summary>새로 얻은 도안을 빈 공간에 놓고 공간이 없으면 미배치 상태로 둔다.</summary>
    private void EnsurePatterns()
    {
        _layout.EnsurePlaced(_game.Spells);
        if (_game.CraftCount != 0) return;
        foreach (int spell in _game.Spells)
        {
            if (_layout.IsPlaced(spell)) continue;
            _layout.Reset(); _layout.EnsurePlaced(_game.Spells); break;
        }
    }

    /// <summary>도안을 새 위치·회전으로 옮기고 결과를 화면에 반영한다. 불가하면 원래 위치·방향을 유지하고 이유를 표시한다.</summary>
    public void MovePattern(int spell, Vector2Int origin, int rotation)
    {
        if (!CanMovePattern(spell)) { _message = "고정된 조각이 있는 도안은 이동할 수 없습니다"; Render(); return; }
        string issue = _layout.MoveIssue(spell, origin, rotation, _game.Spells);
        if (issue == null) { _layout.Place(spell, origin, rotation); _message = ""; }
        else _message = issue + " · 원래 자리로 돌아갑니다";
        Render();
    }

    /// <summary>도안에 고정된 이전 턴 조각이 없는지 확인한다.</summary>
    public bool CanMovePattern(int spell)
    {
        foreach (SpellGame.Placement piece in _game.Boards[spell].Pieces) if (!_game.CanMovePiece(piece)) return false;
        return true;
    }

    /// <summary>도안별 외곽선을 구분할 색을 반환한다.</summary>
    public Color PatternColor(int spell)
    {
        return _game.IsSealed(spell) ? _dangerColor : _patternColorValues[spell];
    }

    /// <summary>보상에서 빠진 이전 효과의 표시 색을 반환한다. 효과가 없으면 글자색을 반환한다.</summary>
    public Color EffectColor(SpellGame.Effect effect)
    {
        switch (effect)
        {
            case SpellGame.Effect.Period: return _periodColor;
            case SpellGame.Effect.Resonance: return _resonanceColor;
            default: return _textColor;
        }
    }

    /// <summary>코팅의 표시 색을 반환한다. 2D UI는 이 색으로, 3D 조각은 코팅 Material로 구분한다.</summary>
    public Color CoatingColor(SpellGame.Coating coating)
    {
        switch (coating)
        {
            case SpellGame.Coating.Guard: return _guardColor;
            case SpellGame.Coating.Amplify: return _flameColor;
            case SpellGame.Coating.Cog: return _cogColor;
            default: return _textColor;
        }
    }

    /// <summary>씰링 동그라미의 표시 색을 반환한다.</summary>
    public Color SealColor(SpellGame.Seal seal)
    {
        switch (seal)
        {
            case SpellGame.Seal.Recall: return _afterimageColor;
            case SpellGame.Seal.Shard: return _sparkColor;
            case SpellGame.Seal.Refill: return _scribeColor;
            case SpellGame.Seal.Overpaint: return _overpaintColor;
            default: return _textColor;
        }
    }

    /// <summary>카드를 대표할 강조색을 코팅, 첫 씰링, 이전 효과 순으로 고른다. 아무것도 없으면 글자색이다.</summary>
    public Color AccentColor(SpellGame.Card card)
    {
        if (card.Coating != SpellGame.Coating.None) return CoatingColor(card.Coating);
        List<SpellGame.SealSpot> seals = SpellGame.SealSpots(card, 0);
        if (seals.Count > 0) return SealColor(seals[0].Seal);
        if (SpellGame.EffectCount(card, SpellGame.Effect.Period) > 0) return EffectColor(SpellGame.Effect.Period);
        return SpellGame.EffectCount(card, SpellGame.Effect.Resonance) > 0 ? EffectColor(SpellGame.Effect.Resonance) : _textColor;
    }

    /// <summary>2D 도형을 칠할 색을 반환한다. 코팅이 있으면 코팅 색, 이전 효과가 있으면 효과 색, 없으면 흰색이다. 씰링은 색 대신 동그라미로 표시한다.</summary>
    public Color ShapeTint(SpellGame.Card card)
    {
        if (card.Coating != SpellGame.Coating.None) return CoatingColor(card.Coating);
        if (card.Effect != SpellGame.Effect.None) return EffectColor(card.Effect);
        return Color.white;
    }

    /// <summary>카드에 코팅, 씰링, 이전 효과, 고유 성질 중 하나라도 있는지 반환한다.</summary>
    public static bool HasTraits(SpellGame.Card card)
    {
        return card.IsUnique || card.Coating != SpellGame.Coating.None || SpellGame.SealSpots(card, 0).Count > 0
            || SpellGame.EffectCount(card, SpellGame.Effect.Period) > 0 || SpellGame.EffectCount(card, SpellGame.Effect.Resonance) > 0;
    }

    /// <summary>보상에서 빠진 이전 효과 이름을 반환한다. 없으면 빈 문자열이다.</summary>
    private static string EffectName(SpellGame.Effect effect)
    {
        switch (effect)
        {
            case SpellGame.Effect.Period: return "마침표";
            case SpellGame.Effect.Resonance: return "공명";
            default: return "";
        }
    }

    /// <summary>코팅 이름을 반환한다. 없으면 빈 문자열이다.</summary>
    public static string CoatingName(SpellGame.Coating coating)
    {
        switch (coating)
        {
            case SpellGame.Coating.Guard: return "수호";
            case SpellGame.Coating.Amplify: return "증폭";
            case SpellGame.Coating.Cog: return "톱니";
            default: return "";
        }
    }

    /// <summary>씰링 이름을 반환한다. 없으면 빈 문자열이다.</summary>
    public static string SealName(SpellGame.Seal seal)
    {
        switch (seal)
        {
            case SpellGame.Seal.Recall: return "회수";
            case SpellGame.Seal.Shard: return "파편";
            case SpellGame.Seal.Refill: return "보충";
            case SpellGame.Seal.Overpaint: return "덧칠";
            default: return "";
        }
    }

    /// <summary>코팅 하나의 효과 설명을 반환한다.</summary>
    public static string CoatingDescription(SpellGame.Coating coating)
    {
        switch (coating)
        {
            case SpellGame.Coating.Guard: return "시전 시 방어 +3";
            case SpellGame.Coating.Amplify: return "피해 +2 · 다타 주문은 타격마다 +1";
            case SpellGame.Coating.Cog: return "변을 맞댄 조각 1장당 피해 +1 (최대 +3)";
            default: return "";
        }
    }

    /// <summary>씰링 하나의 효과 설명을 반환한다. 같은 씰링이 여러 개면 개수만큼 발동한다.</summary>
    public static string SealDescription(SpellGame.Seal seal)
    {
        switch (seal)
        {
            case SpellGame.Seal.Recall: return "시전 후 회수 씰링을 뺀 복사본 1장을 손패로 가져옴 (이번 전투 동안)";
            case SpellGame.Seal.Shard: return $"시전 시 무작위 적에게 {SpellGame.SHARD_DAMAGE} 피해";
            case SpellGame.Seal.Refill: return "시전 시 조각 1장 뽑기";
            case SpellGame.Seal.Overpaint: return "조각 전체를 놓인 조각 위에 한 겹 겹쳐 놓을 수 있음 · 수치 +2";
            default: return "";
        }
    }

    /// <summary>보상에서 빠진 이전 효과의 설명을 반환한다.</summary>
    public static string EffectDescription(SpellGame.Effect effect)
    {
        switch (effect)
        {
            case SpellGame.Effect.Period: return "도안을 완성시킨 마지막 조각이면 수치 +3";
            case SpellGame.Effect.Resonance: return "3장 이상 함께 소모되면 수치 +3";
            default: return "";
        }
    }

    /// <summary>고유 조각의 설명을 반환한다.</summary>
    public static string UniqueDescription(SpellGame.Card card)
    {
        return card.IsUnique ? "놓은 칸의 빈 구역 모양으로 변함 (1칸 이하) · 씰링·코팅·붙이기·쪼개기 불가" : "";
    }

    /// <summary>카드의 고유·코팅·씰링·이전 효과를 한 줄로 요약한다. 씰링은 종류별 개수로 묶는다. 아무것도 없으면 빈 문자열이다.</summary>
    public static string TraitText(SpellGame.Card card)
    {
        List<string> parts = new List<string>();
        if (card.IsUnique) parts.Add("고유");
        if (card.Coating != SpellGame.Coating.None) parts.Add("코팅 " + CoatingName(card.Coating));
        List<string> seals = new List<string>();
        foreach (SpellGame.Seal seal in Enum.GetValues(typeof(SpellGame.Seal)))
        {
            int count = seal == SpellGame.Seal.None ? 0 : SpellGame.SealCount(card, seal);
            if (count > 0) seals.Add(count > 1 ? $"{SealName(seal)}×{count}" : SealName(seal));
        }
        if (seals.Count > 0) parts.Add("씰링 " + string.Join(", ", seals));
        foreach (SpellGame.Effect effect in new[] { SpellGame.Effect.Period, SpellGame.Effect.Resonance })
            if (SpellGame.EffectCount(card, effect) > 0) parts.Add(EffectName(effect));
        return string.Join(" · ", parts);
    }

    /// <summary>조각 이름 없이 고유·코팅·씰링의 종류와 효과 설명을 줄 단위로 반환한다. 아무 성질도 없으면 "일반 조각"이다.</summary>
    public static string TraitDescriptionText(SpellGame.Card card)
    {
        List<string> lines = new List<string>();
        if (card.IsUnique) lines.Add("고유 · " + UniqueDescription(card));
        if (card.Coating != SpellGame.Coating.None) lines.Add($"코팅 {CoatingName(card.Coating)} · {CoatingDescription(card.Coating)}");
        foreach (SpellGame.Seal seal in Enum.GetValues(typeof(SpellGame.Seal)))
        {
            int count = seal == SpellGame.Seal.None ? 0 : SpellGame.SealCount(card, seal);
            if (count > 0) lines.Add($"씰링 {SealName(seal)}{(count > 1 ? $" ×{count}" : "")} · {SealDescription(seal)}");
        }
        return lines.Count > 0 ? string.Join("\n", lines) : "일반 조각";
    }

    /// <summary>더미·개발자 목록에 쓸 도형과 성질 요약을 두 줄로 반환한다.</summary>
    private string CardName(SpellGame.Card card)
    {
        string traits = TraitText(card);
        return (_game.IsCursed(card) ? "[저주] " : "") + DisplayName(card) + "\n" + (traits.Length > 0 ? traits : "일반");
    }

    /// <summary>목록에 쓸 도형 이름을 반환한다. 접착 카드는 두 원본 도형을 함께 적는다.</summary>
    private static string DisplayName(SpellGame.Card card)
    {
        if (card.Glued != null) return "접착 " + SpellGame.ShapeName(card.Glued.First.Shape) + " + " + SpellGame.ShapeName(card.Glued.Second.Shape);
        return SpellGame.ShapeName(card.Shape);
    }

    /// <summary>랜덤 보상 조각과 아직 없는 도안을 보상 화면에 표시한다.</summary>
    private void RefreshRewards()
    {
        IReadOnlyList<SpellGame.Reward> rewards = _game.Rewards;
        SpellRewardCardView[] cards = _rewardScreen.ShowCards(rewards.Count);
        for (int i = 0; i < rewards.Count; i++)
        {
            SpellGame.Reward reward = rewards[i];
            if (reward.IsPattern) cards[i].BindPattern(i, $"도안: {SpellGame.PatternName(reward.Pattern)}\n{SpellGame.PatternDescription(reward.Pattern)}", reward.Pattern, PatternColor(reward.Pattern), ChooseReward);
            else cards[i].BindPiece(i, TraitDescriptionText(reward.Piece), reward.Piece, HasTraits(reward.Piece) ? AccentColor(reward.Piece) : _subColor, ShapeTint(reward.Piece), SealColor, ChooseReward);
        }
    }

    /// <summary>배치한 조각을 이동 또는 회수 대상으로 선택한다.</summary>
    public void SelectPiece(int board, SpellGame.Placement piece)
    {
        if (!_game.CanMovePiece(piece)) { _message = "이전 턴에 놓은 조각은 이동할 수 없습니다"; Render(); return; }
        _pendingCast = -1; _selectedPiece = piece; _selectedCard = piece.Card; _selectedBoard = board; _message = ""; SetNotice("판을 클릭해 이동하거나 손패로 드래그하세요", _textColor);
        Vector2Int anchor;
        _layout.PieceToGrid(board, piece, out anchor, out _rotation);
        Render();
    }

    /// <summary>격자 앵커 칸과 현재 화면 회전(_rotation)을 지정 도안의 기준 칸·회전으로 바꾼다.</summary>
    private void ToLocalPlacement(int board, Vector2Int anchor, out int x, out int y, out int rotation)
    {
        Vector2Int local;
        _layout.GridToLocalPlacement(board, _selectedCard, anchor, _rotation, out local, out rotation);
        x = local.x; y = local.y;
    }

    /// <summary>격자 앵커 칸에 선택한 카드를 놓거나 기존 조각을 이동한다.</summary>
    public void ClickGrid(int board, Vector2Int anchor)
    {
        if (_selectedCard == null) { _message = "먼저 손패 또는 배치된 조각을 선택하세요"; Render(); return; }
        int x, y, rotation;
        ToLocalPlacement(board, anchor, out x, out y, out rotation);
        SpellGame.Placement ignored = _selectedBoard == board ? _selectedPiece : null;
        bool completes = CanPreviewGrid(board, anchor) && _game.WouldComplete(board, _selectedCard, x, y, rotation, ignored);
        bool success = _selectedPiece == null ? _game.Place(board, _selectedCard, x, y, rotation) : _game.Move(_selectedBoard, _selectedPiece, board, x, y, rotation);
        if (success) { ClearSelection(); _message = ""; if (completes) SetNotice($"{SpellGame.PatternName(board)} 완성 · 시전 버튼을 누르세요", PatternColor(board)); } else _message = PlacementIssue(board, anchor) ?? "이 위치에는 놓을 수 없습니다";
        Render();
    }

    /// <summary>선택 카드를 격자 앵커 칸에 현재 화면 회전으로 놓을 수 있는지 반환한다.</summary>
    public bool CanPreviewGrid(int board, Vector2Int anchor)
    {
        if (_selectedCard == null) return false;
        int x, y, rotation;
        ToLocalPlacement(board, anchor, out x, out y, out rotation);
        return _game.CanPlace(board, _selectedCard, x, y, rotation, _selectedBoard == board ? _selectedPiece : null);
    }

    /// <summary>현재 선택 조각을 격자 앵커 칸에 놓을 수 없는 이유를 반환한다.</summary>
    private string PlacementIssue(int board, Vector2Int anchor)
    {
        if (_selectedCard == null) return "먼저 조각을 선택하세요";
        int x, y, rotation;
        ToLocalPlacement(board, anchor, out x, out y, out rotation);
        return _game.PlacementIssue(board, _selectedCard, x, y, rotation, _selectedBoard == board ? _selectedPiece : null);
    }

    /// <summary>포인터가 가리킨 격자 앵커 칸의 배치 가능 상태와 도안 완성 여부를 커서 안내로 쓴다. board가 음수면 기본 안내를 쓴다.</summary>
    public void SetPreviewHint(int board, Vector2Int anchor)
    {
        string hint = "R / 우클릭 회전  ·  손패에 놓으면 취소";
        if (board >= 0 && _selectedCard != null)
        {
            int x, y, rotation;
            ToLocalPlacement(board, anchor, out x, out y, out rotation);
            bool valid = CanPreviewGrid(board, anchor);
            bool complete = valid && _game.WouldComplete(board, _selectedCard, x, y, rotation, _selectedBoard == board ? _selectedPiece : null);
            hint = complete ? $"놓으면 {SpellGame.PatternName(board)} 완성" : valid ? $"배치 가능  ·  {RemainingText(board)}" : PlacementIssue(board, anchor);
        }
        _hintText = hint;
    }

    /// <summary>경고가 아닌 진행 안내를 다음 화면 갱신 때 지정 강조색의 토스트로 표시하도록 예약한다.</summary>
    private void SetNotice(string notice, Color color)
    {
        _notice = notice; _noticeColor = color;
    }

    /// <summary>도안을 들고 있을 때 놓을 수 있는지와 불가 이유를 커서 안내로 쓴다.</summary>
    public void SetPatternHint(string issue)
    {
        _hintText = issue == null ? "놓을 수 있습니다 · R / 우클릭 회전" : issue;
    }

    /// <summary>선택한 조각이 도안 바깥에 놓일 수 없음을 커서 안내로 쓴다.</summary>
    public void SetOutsidePreviewHint()
    {
        if (_selectedCard != null) _hintText = "도안의 색 테두리 안에 놓아 주세요";
    }

    /// <summary>손패 카드 드래그를 시작하고 포인터를 따라갈 도형을 표시한다.</summary>
    public void BeginCardDrag(SpellGame.Card card, Vector2 position)
    {

        if (CanDragGlueCard)
        {
            string issue = _game.GlueCardIssue(card);
            if (issue == null && _glueCards[0] != null && _glueCards[1] != null && _glueCards[0] != card && _glueCards[1] != card)
                issue = "두 장만 선택할 수 있습니다. 기존 선택을 해제하세요";
            if (issue != null) { _glueMessage = issue; RefreshGlue(); return; }
            _glueDragCard = card;
            _glueDragRotation = _glueCards[0] == card ? _glueRotations[0] : _glueCards[1] == card ? _glueRotations[1] : 0;
            CreateModalDragGhost(card, position); RefreshGlue(); return;
        }
        if (CanDragHammerCard)
        {
            _hammerDragCard = card;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            CreateModalDragGhost(card, position); return;
        }
        if (!CanInteract) return;
        ShowPieceTooltip(null, position);
        _pendingCast = -1;
        if (_selectedCard != card) _rotation = 0;
        _selectedCard = card; _selectedPiece = null; _selectedBoard = -1;
        ShowDragGhost(card, position);
    }

    /// <summary>격자의 배치 조각을 선택하고 드래그 표시를 시작한다.</summary>
    public void BeginPieceDrag(int board, SpellGame.Placement piece, Vector2 position)
    {
        if (!_game.CanMovePiece(piece)) { _message = "이전 턴에 놓은 조각은 이동할 수 없습니다"; Render(); return; }
        _pendingCast = -1; _selectedCard = piece.Card; _selectedPiece = piece; _selectedBoard = board;
        Vector2Int anchor;
        _layout.PieceToGrid(board, piece, out anchor, out _rotation);
        ShowDragGhost(piece.Card, position);
    }

    /// <summary>미리 만들어 둔 조각 고스트를 카드 모양·회전으로 켠다.</summary>
    private void ShowDragGhost(SpellGame.Card card, Vector2 position)
    {
        RectTransform ghost = _combatScreen.PieceGhost;
        float cell = _boardGraphic.CellSize;
        ghost.sizeDelta = SpellPiece3D.CellBounds(SpellGame.CardCells(card, 0)) * cell;
        ghost.localRotation = Quaternion.Euler(0, 0, -90 * _rotation);
        _combatScreen.PieceGhostShape.SetCard(card, isCursed: _game.IsCursed(card), sealColor: SealColor);
        Color color = ShapeTint(card);
        _combatScreen.PieceGhostShape.color = new Color(color.r, color.g, color.b, 0.85f);
        ghost.gameObject.SetActive(true);
        _dragGhost = ghost;
        MoveCardDrag(position);
    }

    /// <summary>조각 고스트를 숨긴다.</summary>
    private void HideDragGhost()
    {
        if (_dragGhost != null) _dragGhost.gameObject.SetActive(false);
        _dragGhost = null;
    }

    /// <summary>배치 조각을 손패로 회수하거나 유효한 격자 위치로 이동한다.</summary>
    public void EndPieceDrag(PointerEventData eventData, bool hasDestination, int board, Vector2Int anchor)
    {
        if (_selectedPiece == null || !CanInteract) return;
        HideDragGhost();
        bool onHand = RectTransformUtility.RectangleContainsScreenPoint(_combatScreen.HandDropArea, eventData.position, eventData.pressEventCamera);
        if (onHand)
        {
            if (_game.Recall(_selectedBoard, _selectedPiece)) { ClearSelection(); _message = ""; SetNotice("조각을 손패로 되돌렸습니다", _textColor); }
            else { ClearSelection(); _message = "손패가 가득 차 회수할 수 없습니다"; }
            Render();
        }
        else if (hasDestination) ClickGrid(board, anchor);
        else { ClearSelection(); _message = "도안이나 손패 위에 놓아 주세요"; Render(); }
    }

    /// <summary>화면 포인터 좌표를 미리 보기의 실제 부모 평면으로 변환해 드래그 중인 도형을 이동한다.</summary>
    public void MoveCardDrag(Vector2 position)
    {
        if (_dragGhost == null) return;
        RectTransform parent = (RectTransform)_dragGhost.parent;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, position, null, out Vector3 worldPosition))
            _dragGhost.position = worldPosition;
    }

    /// <summary>드래그 중인 카드를 격자 위에 미리 표시한다.</summary>
    public void PreviewCardDrag(PointerEventData eventData)
    {
        if (CanDragHammerCard || CanDragGlueCard) return;
        _boardGraphic.PreviewCard(eventData);
    }

    /// <summary>드롭 지점이 제작판이면 배치하고, 아니면 선택을 유지한다.</summary>
    public void EndCardDrag(PointerEventData eventData)
    {

        if (CanDragGlueCard)
        {
            SpellGame.Card card = _glueDragCard;
            int rotation = _glueDragRotation;
            _glueDragCard = null;
            if (_dragGhost != null) { _dragGhost.gameObject.SetActive(false); Destroy(_dragGhost.gameObject); }
            _dragGhost = null;
            Vector2Int position;
            if (card != null && _glueGraphic.TryDropPosition(eventData, out position))
            {
                int slot = _glueCards[0] == card ? 0 : _glueCards[1] == card ? 1 : -1;
                if (slot < 0)
                {
                    SelectGlueCard(card);
                    slot = _glueCards[0] == card ? 0 : _glueCards[1] == card ? 1 : -1;
                }
                if (slot >= 0) { _glueSelected = slot; _glueRotations[slot] = rotation; MoveGluePart(slot, position); }
            }
            RefreshGlue(); return;
        }
        if (CanDragHammerCard)
        {
            SpellGame.Card card = _hammerDragCard;
            _hammerDragCard = null;
            if (_dragGhost != null) Destroy(_dragGhost.gameObject);
            _dragGhost = null;
            Vector2 point;
            if (card != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, eventData.position, null, out point)
                && Vector2.Distance(point, new Vector2(_root.rect.xMin + 560, _root.rect.yMax - 340)) <= 210)
                SelectHammerCard(card);
            return;
        }
        if (_selectedCard == null || !CanInteract) return;
        HideDragGhost();
        if (_boardGraphic.TryDrop(eventData)) return;
        _message = "도안의 색 테두리 안에 놓아 주세요"; Render();
    }

    /// <summary>도안 이동 핸들에서 드래그를 시작한다. 핸들은 도안의 왼쪽 위 칸을 잡은 것으로 본다.</summary>
    public void BeginPatternDrag(PointerEventData eventData, int pattern)
    {
        if (!CanInteract) return;
        ShowTextTooltip(null, eventData.position);
        _boardGraphic.BeginPatternDrag(pattern, Vector2Int.zero);
        _boardGraphic.PreviewPattern(eventData);
    }

    /// <summary>이동 핸들로 끄는 도안의 고스트와 스냅 미리 보기를 갱신한다.</summary>
    public void PreviewPatternDrag(PointerEventData eventData)
    {
        if (CanInteract) _boardGraphic.PreviewPattern(eventData);
    }

    /// <summary>이동 핸들로 끈 도안을 놓아 위치·회전을 확정하거나 원래대로 둔다.</summary>
    public void EndPatternDrag(PointerEventData eventData)
    {
        if (CanInteract) _boardGraphic.EndPatternDrag(eventData);
        else _boardGraphic.CancelDrag();
    }
    /// <summary>선택한 전투 진입 방식으로 새 게임을 시작하고 도안과 안내 상태를 준비한다.</summary>
    private void StartRun(bool withSealer, bool withThief = false, bool withCurser = false)
    {
        ResetTheft(); _isSealerRun = withSealer; _isThiefRun = withThief; _isCurserRun = withCurser;
        _game.StartGame(false, withSealer, withThief, withCurser); ResetPatterns(); ClearSelection();
        if (withSealer || withThief || withCurser) _onboardingComplete = true;
        _message = _game.SealNotice; _feedbackUntil = 0; Render();
    }

    private bool CanAdvanceTheft
    {
        get { return CanInteract && _pendingCast < 0 && Application.isFocused && Time.timeScale > 0
            && !(ONBOARDING_ENABLED && !_onboardingComplete && !_onboardingPlacementExplained && _game.CraftCount > 0); }
    }

    /// <summary>절도 대상과 손 UI를 정리하고 다음 주기를 처음부터 시작한다.</summary>
    private void ClearTheftApproach()
    {
        _game.CancelTheft();
        _theftApproaching = false; _theftElapsed = 0;
        if (_theftHand != null) { _theftHand.gameObject.SetActive(false); Destroy(_theftHand.gameObject); }
        _theftHand = null;
    }

    /// <summary>새 게임의 절도 주기와 전투 식별 상태를 초기화한다.</summary>
    private void ResetTheft()
    {
        ClearTheftApproach(); _theftBattle = -1; _theftWasInteractive = false;
    }

    /// <summary>현재 목표 카드의 점유 삼각형 중심을 합산해 공유 격자상의 실제 화면 좌표를 반환한다.</summary>
    private Vector2 TheftTargetPosition(int board, SpellGame.Placement piece)
    {
        Vector2 center = Vector2.zero; int count = 0;
        foreach (SpellGame.ShapeCell cell in _layout.PlacementGridCells(board, piece))
            for (int side = 0; side < 4; side++)
            {
                if ((cell.Mask & (1 << side)) == 0) continue;
                center += new Vector2(cell.X + (side == 1 ? 5f / 6 : side == 3 ? 1f / 6 : 0.5f), cell.Y + (side == 2 ? 5f / 6 : side == 0 ? 1f / 6 : 0.5f)); count++;
            }
        Vector2 screen = _table.GridToScreen(center / count);
        Vector2 local = ToLayer(screen);
        return new Vector2(local.x - _root.rect.xMin, _root.rect.yMax - local.y);
    }

    /// <summary>모달과 포커스 상태에 따라 절도 주기·접근 이동을 멈추거나 이어가고 도착 시 모델에 절도를 요청한다.</summary>
    private void UpdateTheft()
    {
        if (_game.CurrentPhase != SpellGame.Phase.Combat || !_game.HasLivingThief)
        {
            if (_theftApproaching || _game.TheftTarget != null) ClearTheftApproach();
            _theftElapsed = 0; _theftWasInteractive = false; return;
        }
        if (_theftBattle != _game.Battle)
        {
            ClearTheftApproach(); _theftBattle = _game.Battle; _theftWasInteractive = false;
        }
        int board; SpellGame.Placement piece;
        if (_theftApproaching && !_game.TryGetTheftPlacement(out board, out piece)) ClearTheftApproach();
        bool interactive = CanAdvanceTheft;
        if (_theftHand != null) _theftHand.gameObject.SetActive(interactive);
        if (!interactive) { _theftWasInteractive = false; return; }
        if (!_theftWasInteractive) { _theftWasInteractive = true; return; }
        float delta = Time.unscaledDeltaTime;
        if (!_theftApproaching)
        {
            _theftElapsed = Mathf.Min(_theftElapsed + delta, Mathf.Max(0.1f, _theftIntervalSeconds));
            if (_theftElapsed < Mathf.Max(0.1f, _theftIntervalSeconds) || !_game.BeginTheft()) return;
            if (!_game.TryGetTheftPlacement(out board, out piece)) { ClearTheftApproach(); return; }
            Vector2 target = TheftTargetPosition(board, piece);
            _theftPosition = new Vector2(1180, 300); int choices = 0;
            foreach (Vector2 start in THEFT_START_POINTS)
                if (Vector2.Distance(start, target) >= THEFT_MIN_START_DISTANCE && _random.Next(++choices) == 0) _theftPosition = start;
            _theftApproaching = true; _theftElapsed = 0;
            RenderTheftHand(); return;
        }
        if (!_game.TryGetTheftPlacement(out board, out piece)) { ClearTheftApproach(); return; }
        Vector2 destination = TheftTargetPosition(board, piece);
        _theftPosition = Vector2.MoveTowards(_theftPosition, destination, Mathf.Max(1, _theftMoveSpeed) * delta);
        if (_theftHand == null) RenderTheftHand();
        _theftHand.anchoredPosition = new Vector2(_theftPosition.x, -_theftPosition.y);
        Vector2 direction = destination - _theftPosition;
        _theftHand.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg - 90);
        if (Vector2.Distance(_theftPosition, destination) > THEFT_ARRIVAL_RADIUS) return;
        SpellGame.Card targetCard = _game.TheftTarget;
        bool stolen = _game.ResolveTheft();
        ClearTheftApproach();
        if (!stolen) return;
        if (_selectedCard == targetCard) ClearSelection();
        _message = "도둑손이 조각을 훔쳤습니다 · 전투 종료 후 복구됩니다";
        ShowFeedback("조각을 빼앗겼습니다!", new Color(0.95f, 0.48f, 0.35f)); Render();
    }

    /// <summary>현재 절도 대상과 연결된 손 그래픽을 생성한다. UI 재생성 시에도 접근 위치를 유지한다.</summary>
    private void RenderTheftHand()
    {
        if (!_theftApproaching || _game.TheftTarget == null || _theftHand != null) return;
        _theftHand = Element("Thief Hand", _root, _theftPosition.x, _theftPosition.y, 96, 96);
        _theftHand.pivot = new Vector2(0.5f, 0.5f);
        _theftHand.gameObject.AddComponent<CanvasRenderer>();
        _theftHand.gameObject.AddComponent<SpellThiefHandGraphic>().Setup(this, _game.TheftTarget);
        _theftHand.gameObject.SetActive(CanAdvanceTheft);
    }

    /// <summary>클릭된 손의 대상이 현재 시도와 일치할 때만 절도를 저지하며 카드와 배치 비용을 유지한다.</summary>
    public void StopTheft(SpellGame.Card expectedTarget)
    {
        if (!_theftApproaching || !CanAdvanceTheft || _game.TheftTarget != expectedTarget) return;
        ClearTheftApproach();
        _message = "도둑손 저지!"; ShowFeedback(_message, new Color(0.4f, 0.9f, 0.8f)); Render();
    }

    /// <summary>봉인된 도안의 현재 격자 위치에 수직 막대기와 잠금 표시를 그린다. 포인터 입력은 통과시킨다.</summary>
    private void RenderSeals(RectTransform panel)
    {
        foreach (Transform child in panel) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (int spell in _game.Spells)
        {
            if (!_game.IsSealed(spell) || !_layout.IsPlaced(spell)) continue;
            Vector2 center = Vector2.zero; int count = 0;
            for (int y = 0; y < SpellPatternLayout.ROWS; y++) for (int x = 0; x < SpellPatternLayout.COLUMNS; x++)
                if (_layout.PlacedMask(spell, x, y) != 0) { center += new Vector2(x + 0.5f, y + 0.5f); count++; }
            Vector2 point = ToLayer(_table.GridToScreen(center / count));
            float px = point.x - _root.rect.xMin, py = _root.rect.yMax - point.y;
            RectTransform rod = Panel(panel, px - 3, py - 70, 6, 140, _dangerColor);
            rod.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            Label(panel, "봉인", px - 35, py - 95, 70, 25, 17);
        }
    }


    /// <summary>충돌하는 입력과 모달을 제외하고 망치 대상 선택 화면을 연다.</summary>
    private void BeginHammer()
    {
        if (!CanInteract || _pendingCast >= 0 || _dragGhost != null || _boardGraphic.IsDragging || _table.IsDragging) return;
        if (ONBOARDING_ENABLED && !_onboardingComplete && !_onboardingPlacementExplained && _game.CraftCount > 0) return;
        if (_game.HammerUsesLeft <= 0) { _message = "이번 턴의 망치를 이미 사용했습니다"; Render(); return; }
        ClearSelection(); Render();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _hammerTurn = _game.Turn; _hammerBattle = _game.Battle;
        _hammerPhase = HammerPhase.Selecting;
        _hammerMessage = _game.Hand.Count == 0 ? "손패가 비어 있습니다. Esc로 돌아가세요" : "분해할 조각 하나를 선택하세요";
        RenderHammer();
    }

    /// <summary>망치 화면과 선택 참조를 정리한다. 이미 확정한 타격 결과는 되돌리지 않는다.</summary>
    private void CloseHammer()
    {
        if (_hammerDragCard != null && _dragGhost != null) { _dragGhost.gameObject.SetActive(false); Destroy(_dragGhost.gameObject); _dragGhost = null; }
        _hammerDragCard = null;
        if (_hammerOverlay != null) { _hammerOverlay.gameObject.SetActive(false); Destroy(_hammerOverlay.gameObject); }
        _hammerOverlay = null; _hammerCard = null; _hammerPhase = HammerPhase.None;
        _hammerResultShapes = Array.Empty<SpellGame.Shape>();
        _hammerHint = null; _hammerMarker = null; _hammerGaugeFill = null;
        _onboardingActivity = Time.unscaledTime;
    }

    /// <summary>선택한 손패의 분해 조건을 안내하고 유효하면 타이밍 게이지를 시작한다.</summary>
    private void SelectHammerCard(SpellGame.Card card)
    {
        if (_hammerPhase != HammerPhase.Selecting && _hammerPhase != HammerPhase.Timing) return;
        string issue = _game.HammerIssue(card);
        _hammerCard = issue == null ? card : null;
        _hammerMessage = issue ?? "높은 게이지에서 Space를 누르세요";
        _hammerPhase = issue == null ? HammerPhase.Timing : HammerPhase.Selecting;
        _hammerTimingStart = Time.unscaledTime;
        RenderHammer();
    }

    /// <summary>현재 강도와 손패 한도로 발생 가능한 분해 결과별 확률을 안내한다.</summary>
    private string HammerOdds(float strength)
    {
        float chance = SpellGame.DecompositionChance(strength);
        return $"강도 {strength:P0}\n기본 분해 보장\n각 자식은 {chance:P0}로 추가 분해\n최대 {_game.HammerMaxPieces(_hammerCard)}장 · 혼합 결과 가능";
    }

    /// <summary>확정된 혼합 도형을 종류별 장수로 묶어 결과 안내를 반환한다.</summary>
    private string HammerResultMessage()
    {
        if (_hammerResultShapes.Length == 1) return "추가 분해 불가 · 원본 유지";
        string message = "분해 결과: ";
        for (int shape = 0; shape < 4; shape++)
        {
            int count = 0;
            foreach (SpellGame.Shape piece in _hammerResultShapes) if ((int)piece == shape) count++;
            if (count > 0) message += $"{SpellGame.ShapeName((SpellGame.Shape)shape)} {count}장  ";
        }
        return message;
    }

    /// <summary>타이밍 시작 시각과 왕복 시간으로 현재 0~1 강도를 계산한다.</summary>
    private float HammerStrength()
    {
        return Mathf.PingPong((Time.unscaledTime - _hammerTimingStart) / Mathf.Max(0.1f, _hammerGaugeSeconds), 1f);
    }

    /// <summary>기존 도형 그래픽으로 망치 대상이나 결과 아이콘을 중앙 좌표에 생성한다.</summary>
    private RectTransform HammerShape(RectTransform parent, SpellGame.Shape shape, float x, float y, float size)
    {
        RectTransform rect = Element("Hammer Piece", parent, x, y, shape == SpellGame.Shape.Rectangle ? size * 2 : size, size);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.gameObject.AddComponent<CanvasRenderer>();
        SpellShapeGraphic graphic = rect.gameObject.AddComponent<SpellShapeGraphic>();
        graphic.Shape = shape; graphic.raycastTarget = false; graphic.color = new Color(0.72f, 0.9f, 0.94f);
        return rect;
    }

    /// <summary>어두운 모달, 원형 나무판, 손패 선택과 오른쪽 강도 게이지를 생성한다.</summary>
    private void RenderHammer()
    {
        if (_hammerDragCard != null && _dragGhost != null) { _dragGhost.gameObject.SetActive(false); Destroy(_dragGhost.gameObject); _dragGhost = null; }
        _hammerDragCard = null;
        if (_hammerOverlay != null) { _hammerOverlay.gameObject.SetActive(false); Destroy(_hammerOverlay.gameObject); }
        _hammerOverlay = Panel(_root, 0, 0, 1280, 720, new Color(0.015f, 0.02f, 0.03f, 0.94f));
        Label(_hammerOverlay, "망치 작업대", 350, 35, 420, 42, 30);
        Label(_hammerOverlay, "일반·특수·접착 조각 분해 · 효과는 원본별 가장 큰 조각에 유지 · 배치 비용 없음", 140, 85, 1000, 32, 16);
        RectTransform board = Element("Hammer Workbench", _hammerOverlay, 350, 130, 420, 420);
        board.gameObject.AddComponent<CanvasRenderer>();
        board.gameObject.AddComponent<SpellHammerGraphic>().raycastTarget = false;
        Label(_hammerOverlay, _hammerPhase == HammerPhase.Selecting ? "조각을 클릭하거나 작업대로 드래그하세요" : "Space: 타격   ·   Esc: 타격 전 취소", 260, 550, 600, 32, 19);

        _hammerSource = _hammerCard == null ? null : HammerShape(_hammerOverlay, _hammerCard.Shape, 560, 340, 144);
        if (_hammerSource != null) _hammerSource.GetComponent<SpellShapeGraphic>().SetCard(_hammerCard, isCursed: _game.IsCursed(_hammerCard), sealColor: SealColor);
        Panel(_hammerOverlay, 886, 181, 66, 278, new Color(0.40f, 0.34f, 0.27f));
        HammerBand(0, 0.25f, new Color(0.28f, 0.30f, 0.33f));
        HammerBand(0.25f, 0.5f, new Color(0.25f, 0.54f, 0.45f));
        HammerBand(0.5f, 0.8f, new Color(0.74f, 0.55f, 0.22f));
        HammerBand(0.8f, 1, new Color(0.8f, 0.29f, 0.20f));
        _hammerGaugeFill = Panel(_hammerOverlay, 934, 450, 10, 0, new Color(1f, 0.9f, 0.6f)).GetComponent<UnityEngine.UI.Image>();
        _hammerMarker = Panel(_hammerOverlay, 880, 446, 80, 6, Color.white);
        Label(_hammerOverlay, "강한 타격", 856, 140, 130, 30, 17);
        Label(_hammerOverlay, "약한 타격", 856, 460, 130, 30, 17);
        Label(_hammerOverlay, "임시 확률 규칙\n기본 분해는 항상 성공\n강할수록 추가 분해 확률 증가\n최소 1/4 · 손패 7장 제한", 990, 210, 220, 150, 17, TextAnchor.UpperLeft);
        Label(_hammerOverlay, _hammerMessage, 800, 495, 440, 75, 17);
        RectTransform hint = Element("Hammer Preview", _hammerOverlay, 980, 360, 240, 120);
        _hammerHint = hint.gameObject.AddComponent<UnityEngine.UI.Text>();
        _hammerHint.font = _font; _hammerHint.fontSize = 18; _hammerHint.color = Color.white; _hammerHint.raycastTarget = false;

        _hammerResults = Element("Hammer Results", _hammerOverlay, 560, 340, 0, 0);
        _hammerResults.gameObject.SetActive(false);
        _hammerVisual = Element("Hammer", _hammerOverlay, 820, 0, 0, 0);
        Panel(_hammerVisual, -12, 25, 24, 150, new Color(0.45f, 0.25f, 0.12f));
        Panel(_hammerVisual, -87, -57, 174, 94, new Color(0.16f, 0.19f, 0.22f));
        Panel(_hammerVisual, -80, -50, 160, 80, new Color(0.56f, 0.64f, 0.68f));
        Panel(_hammerVisual, -80, -50, 160, 10, new Color(0.82f, 0.88f, 0.91f));
        _hammerVisual.gameObject.SetActive(false);
        RectTransform impact = Element("Hammer Impact", _hammerOverlay, 560, 340, 0, 0);
        _hammerImpact = impact.gameObject.AddComponent<CanvasGroup>();
        _hammerImpact.blocksRaycasts = false; _hammerImpact.alpha = 0;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI / 6;
            RectTransform ray = Panel(impact, 0, 0, 25 + _hammerStrength * 45, 4, new Color(1f, 0.82f, 0.42f));
            ray.pivot = new Vector2(0, 0.5f);
            ray.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (95 + _hammerStrength * 45);
            ray.localRotation = Quaternion.Euler(0, 0, i * 30);
        }
        if (_hammerPhase == HammerPhase.Selecting || _hammerPhase == HammerPhase.Timing)
        {
            for (int i = 0; i < _game.Hand.Count; i++)
            {
                SpellGame.Card card = _game.Hand[i];
                RectTransform panel = Panel(_hammerOverlay, 80 + i * 160, 590, 148, 115, CardColor(card, card == _hammerCard));
                UnityEngine.UI.Button button = panel.gameObject.AddComponent<UnityEngine.UI.Button>();
                UnityEngine.UI.Navigation navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
                button.onClick.AddListener(() => SelectHammerCard(card));
                panel.gameObject.AddComponent<SpellCardDrag>().Setup(this, card);
                SpellShapeGraphic shape = HammerShape(panel, card.Shape, 74, 32, 54).GetComponent<SpellShapeGraphic>();
                shape.SetCard(card, isCursed: _game.IsCursed(card), sealColor: SealColor); shape.color = ShapeTint(card);
                Label(panel, CardName(card), 2, 65, 144, 48, 12);
            }
        }
        else
        {
            _hammerVisual.gameObject.SetActive(true);
            foreach (SpellGame.Shape shape in _hammerResultShapes) HammerShape(_hammerResults, shape, 0, 0, 70);
        }
    }

    /// <summary>강도 구간의 색을 오른쪽 게이지의 하한부터 상한까지 표시한다.</summary>
    private void HammerBand(float low, float high, Color color)
    {
        Panel(_hammerOverlay, 894, 190 + 260 * (1 - high), 32, 260 * (high - low), color);
    }

    /// <summary>Space 순간의 강도를 기록해 모델에 한 번만 적용하고 타격 연출을 시작한다.</summary>
    private void StrikeHammer()
    {
        _hammerStrength = HammerStrength();
        if (!_game.TryHammer(_hammerCard, _hammerStrength, out _hammerResultShapes))
        {
            _hammerMessage = _game.HammerIssue(_hammerCard); _hammerCard = null; _hammerPhase = HammerPhase.Selecting; RenderHammer(); return;
        }
        _hammerStrikeStart = Time.unscaledTime;
        _hammerPhase = HammerPhase.Striking;
        _hammerMessage = HammerResultMessage();
        RenderHammer();
    }

    /// <summary>상태 유효성, 취소와 타이밍 입력을 처리하고 타격·충격·분해 결과를 시간에 맞춰 표시한다.</summary>
    private void UpdateHammer()
    {
        if (_game.CurrentPhase != SpellGame.Phase.Combat || _game.Turn != _hammerTurn || _game.Battle != _hammerBattle)
        {
            CloseHammer(); Render(); return;
        }
        bool beforeStrike = _hammerPhase == HammerPhase.Selecting || _hammerPhase == HammerPhase.Timing;
        if (beforeStrike && Keyboard.current != null
            && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame))
        {
            CloseHammer(); Render(); return;
        }
        if (_hammerPhase == HammerPhase.Timing)
        {
            if (_game.HammerIssue(_hammerCard) != null) { SelectHammerCard(_hammerCard); return; }
            float strength = HammerStrength();
            _hammerMarker.anchoredPosition = new Vector2(880, -(446 - strength * 260));
            _hammerGaugeFill.rectTransform.anchoredPosition = new Vector2(934, -(450 - strength * 260));
            _hammerGaugeFill.rectTransform.sizeDelta = new Vector2(10, strength * 260);
            _hammerHint.text = HammerOdds(strength);
            if (_hammerDragCard == null && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) StrikeHammer();
            return;
        }
        if (beforeStrike) return;
        float elapsed = Time.unscaledTime - _hammerStrikeStart;
        float strikeSeconds = Mathf.Max(0.1f, _hammerStrikeSeconds);
        float progress = Mathf.Clamp01(elapsed / strikeSeconds);
        _hammerVisual.anchoredPosition = Vector2.Lerp(new Vector2(820, 30), new Vector2(560, -335), progress * progress);
        _hammerVisual.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-45, 0, progress));
        _hammerMarker.anchoredPosition = new Vector2(880, -(446 - _hammerStrength * 260));
        _hammerGaugeFill.rectTransform.anchoredPosition = new Vector2(934, -(450 - _hammerStrength * 260));
        _hammerGaugeFill.rectTransform.sizeDelta = new Vector2(10, _hammerStrength * 260);
        _hammerHint.text = $"확정 강도 {_hammerStrength:P0}\n{_hammerMessage}";
        if (elapsed < strikeSeconds) return;
        _hammerPhase = HammerPhase.Result;
        float resultTime = elapsed - strikeSeconds;
        _hammerVisual.gameObject.SetActive(resultTime < 0.12f);
        _hammerImpact.alpha = Mathf.Clamp01(1 - resultTime / 0.4f) * (0.45f + _hammerStrength * 0.55f);
        if (_hammerSource != null) _hammerSource.gameObject.SetActive(_hammerResultShapes.Length == 1);
        _hammerResults.gameObject.SetActive(_hammerResultShapes.Length > 1);
        int columns = _hammerResultShapes.Length > 4 ? 4 : 2;
        int rows = (_hammerResultShapes.Length + columns - 1) / columns;
        float spread = Mathf.SmoothStep(0, 1, Mathf.Clamp01(resultTime / 0.3f));
        for (int i = 0; i < _hammerResults.childCount; i++)
        {
            RectTransform piece = (RectTransform)_hammerResults.GetChild(i);
            piece.anchoredPosition = new Vector2((i % columns - (columns - 1) / 2f) * 85, -(i / columns - (rows - 1) / 2f) * 85) * spread;
            piece.localRotation = Quaternion.Euler(0, 0, i * (360f / _hammerResultShapes.Length) * spread);
        }
        float shake = Mathf.Sin(resultTime * 90) * Mathf.Max(0, 1 - resultTime / 0.25f) * (2 + _hammerStrength * 6);
        _hammerResults.anchoredPosition = new Vector2(560 + shake, -340);
        if (resultTime >= Mathf.Max(0.2f, _hammerResultSeconds))
        {
            _message = _hammerMessage; CloseHammer(); Render();
        }
    }

    /// <summary>일반 카드 수와 모달·드래그 상태를 확인하고 접착 작업을 시작한다.</summary>
    private void BeginGlue()
    {
        if (!CanInteract || _pendingCast >= 0 || _dragGhost != null || _boardGraphic.IsDragging || _table.IsDragging) return;
        if (ONBOARDING_ENABLED && !_onboardingComplete && !_onboardingPlacementExplained && _game.CraftCount > 0) return;
        int count = 0;
        foreach (SpellGame.Card card in _game.Hand) if (_game.GlueCardIssue(card) == null) count++;
        if (_game.GlueUsesLeft == 0 || count < 2)
        {
            _message = _game.GlueUsesLeft == 0 ? "이번 턴의 접착을 이미 사용했습니다" : "접착 가능한 조각이 두 장 이상 필요합니다";
            Render(); return;
        }
        ClearSelection(); Render();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _glueTurn = _game.Turn; _glueBattle = _game.Battle;
        RenderGlue();
    }

    /// <summary>접착 미리 보기와 선택을 삭제한다. 확인 전에는 카드나 사용 횟수를 변경하지 않는다.</summary>
    private void CloseGlue()
    {
        if (_glueDragCard != null && _dragGhost != null) { _dragGhost.gameObject.SetActive(false); Destroy(_dragGhost.gameObject); _dragGhost = null; }
        _glueDragCard = null;
        _glueDragRotation = 0;
        if (_glueOverlay != null) { _glueOverlay.gameObject.SetActive(false); Destroy(_glueOverlay.gameObject); }
        _glueOverlay = null; _glueGraphic = null; _glueHint = null; _glueConfirm = null; _glueCoatingChoice = null;
        for (int i = 0; i < 2; i++) { _glueCards[i] = null; _gluePositions[i] = new Vector2Int(2 + i * 2, 1); _glueRotations[i] = 0; }
        _glueSelected = 0; _glueMessage = ""; _onboardingActivity = Time.unscaledTime;
    }

    /// <summary>손패에서 접착할 서로 다른 일반 카드 두 장을 선택하거나 선택을 해제한다.</summary>
    private void SelectGlueCard(SpellGame.Card card)
    {
        string issue = _game.GlueCardIssue(card);
        if (issue != null) { _glueMessage = issue; RefreshGlue(); return; }
        for (int i = 0; i < 2; i++)
            if (_glueCards[i] == card) { _glueCards[i] = null; _glueMessage = ""; RenderGlue(); return; }
        int slot = _glueCards[0] == null ? 0 : _glueCards[1] == null ? 1 : -1;
        if (slot < 0) { _glueMessage = "두 장만 선택할 수 있습니다. 기존 선택을 해제하세요"; RefreshGlue(); return; }
        _glueCards[slot] = card; _glueSelected = slot;
        _gluePositions[slot] = new Vector2Int(2 + slot * 2, 1); _glueRotations[slot] = 0;
        _glueMessage = ""; RenderGlue();
    }

    /// <summary>접착 미리 보기의 지정 원본 카드를 반환한다.</summary>
    public SpellGame.Card GlueCard(int index) { return _glueCards[index]; }

    /// <summary>지정 접착 작업 조각이 현재 미해결 저주 대상인지 반환한다.</summary>
    public bool IsGluePartCursed(int index) { return _game.IsCursed(_glueCards[index]); }

    /// <summary>작업 공간에서 지정 조각의 스냅 좌표를 반환한다.</summary>
    public Vector2Int GluePosition(int index) { return _gluePositions[index]; }

    /// <summary>작업 공간에서 지정 조각의 회전 방향을 반환한다.</summary>
    public int GlueRotation(int index) { return _glueRotations[index]; }

    public int SelectedGluePart { get { return _glueSelected; } }

    /// <summary>회전과 이동의 대상이 될 작업 공간 조각을 선택한다.</summary>
    public void SelectGluePart(int index)
    {
        if (_glueOverlay == null || _glueCards[index] == null) return;
        _glueSelected = index; _glueMessage = ""; RefreshGlue();
    }

    /// <summary>회전한 조각 전체가 작업 공간 안에 남도록 이동 좌표를 제한하고 접촉 상태를 갱신한다.</summary>
    public void MoveGluePart(int index, Vector2Int position)
    {
        if (_glueOverlay == null || _glueCards[index] == null) return;
        int width = SpellGame.ShapeWidth(_glueCards[index], _glueRotations[index]);
        int height = SpellGame.ShapeHeight(_glueCards[index], _glueRotations[index]);
        _gluePositions[index] = new Vector2Int(Mathf.Clamp(position.x, 0, 8 - width), Mathf.Clamp(position.y, 0, 4 - height));
        _glueMessage = ""; RefreshGlue();
    }

    /// <summary>작업 공간 범위와 모델의 실제 겹침·변 접촉을 검사해 확인 불가 사유를 반환한다.</summary>
    public string GluePlacementIssue()
    {
        if (_glueCards[0] == null || _glueCards[1] == null) return "접착할 조각 2개를 선택하세요";
        for (int i = 0; i < 2; i++)
        {
            Vector2Int pos = _gluePositions[i];
            if (pos.x < 0 || pos.y < 0 || pos.x + SpellGame.ShapeWidth(_glueCards[i], _glueRotations[i]) > 8
                || pos.y + SpellGame.ShapeHeight(_glueCards[i], _glueRotations[i]) > 4) return "조각을 작업 공간 안으로 옮기세요";
        }
        Vector2Int relative = _gluePositions[1] - _gluePositions[0];
        return _game.GlueIssue(_glueCards[0], _glueRotations[0], _glueCards[1], _glueRotations[1], relative.x, relative.y);
    }

    /// <summary>접착 상태 색상과 확인 가능 여부만 갱신해 진행 중 드래그 객체를 유지한다.</summary>
    private void RefreshGlue()
    {
        if (_glueOverlay == null) return;
        string issue = GluePlacementIssue();
        _glueHint.text = (_glueMessage.Length > 0 ? _glueMessage : issue ?? "접착 가능 · 확인하면 카드 한 장으로 합쳐집니다")
            + $"\n선택 조각 {_glueSelected + 1} · R / 우클릭 회전";
        _glueHint.color = issue == null ? new Color(0.55f, 1f, 0.7f) : new Color(1f, 0.55f, 0.5f);
        _glueConfirm.interactable = issue == null && !_glueGraphic.IsDragging && _glueDragCard == null;
        _glueGraphic.SetVerticesDirty();
    }

    /// <summary>두 카드의 유효성과 접촉을 다시 확인한다. 코팅이 서로 다르면 유지할 코팅 선택 창을 띄우고, 아니면 바로 접착을 확정한다.</summary>
    private void ConfirmGlue()
    {
        if (_glueOverlay == null || _glueCoatingChoice != null || _glueGraphic.IsDragging || _glueDragCard != null || GluePlacementIssue() != null) return;
        if (SpellGame.NeedsCoatingChoice(_glueCards[0], _glueCards[1])) { ShowGlueCoatingChoice(); return; }
        FinishGlue(SpellGame.Coating.None);
    }

    /// <summary>고른 코팅으로 접착을 한 번 확정하고 전투 화면으로 복귀한다. 실패하면 작업대에 남아 이유를 표시한다.</summary>
    private void FinishGlue(SpellGame.Coating keptCoating)
    {
        if (_glueOverlay == null) return;
        HideGlueCoatingChoice();
        Vector2Int relative = _gluePositions[1] - _gluePositions[0];
        if (!_game.TryGlue(_glueCards[0], _glueRotations[0], _glueCards[1], _glueRotations[1], relative.x, relative.y, keptCoating))
        {
            _glueMessage = "카드 상태가 바뀌어 접착하지 못했습니다"; RefreshGlue(); return;
        }
        _message = "접착 완료 · 전체 모양을 한 장으로 배치합니다";
        CloseGlue(); Render();
    }

    /// <summary>두 조각의 코팅 중 붙인 조각에 남길 하나를 고르는 창을 접착 작업대 위에 띄운다. 취소하면 접착하지 않고 작업대로 돌아간다.</summary>
    private void ShowGlueCoatingChoice()
    {
        HideGlueCoatingChoice();
        _glueCoatingChoice = Panel(_glueOverlay, 0, 0, 1280, 720, new Color(0, 0, 0, 0.62f));
        RectTransform box = Panel(_glueCoatingChoice, 330, 190, 620, 340, new Color(0.09f, 0.11f, 0.14f, 0.98f));
        Label(box, "두 조각의 코팅이 다릅니다\n붙인 조각에 남길 코팅을 하나 고르세요", 20, 18, 580, 70, 22);
        for (int i = 0; i < 2; i++)
        {
            SpellGame.Coating coating = _glueCards[i].Coating;
            RectTransform option = Button(box, $"{CoatingName(coating)} 코팅\n{CoatingDescription(coating)}", 30 + i * 290, 110, 270, 130,
                () => { if (_glueCoatingChoice != null) FinishGlue(coating); }, CoatingColor(coating) * 0.55f, 19);
            DisableNavigation(option);
        }
        DisableNavigation(Button(box, "취소 · Esc", 230, 265, 160, 50, HideGlueCoatingChoice, null, 18));
    }

    /// <summary>코팅 선택 창을 닫는다. 접착 작업대와 카드 상태는 그대로 둔다.</summary>
    private void HideGlueCoatingChoice()
    {
        if (_glueCoatingChoice != null) { _glueCoatingChoice.gameObject.SetActive(false); Destroy(_glueCoatingChoice.gameObject); }
        _glueCoatingChoice = null;
    }

    /// <summary>버튼이 Space·방향키 탐색으로 눌리지 않게 탐색을 끈다.</summary>
    private static void DisableNavigation(RectTransform buttonRect)
    {
        UnityEngine.UI.Button button = buttonRect.GetComponent<UnityEngine.UI.Button>();
        UnityEngine.UI.Navigation navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
    }

    /// <summary>접착 모달의 턴·카드 유효성을 확인하고 취소와 선택 조각의 회전을 처리한다.</summary>
    private void UpdateGlue()
    {
        bool invalid = _game.CurrentPhase != SpellGame.Phase.Combat || _game.Turn != _glueTurn || _game.Battle != _glueBattle;
        foreach (SpellGame.Card card in _glueCards) if (card != null && _game.GlueCardIssue(card) != null) invalid = true;
        bool escape = Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.qKey.wasPressedThisFrame);
        if (!invalid && _glueCoatingChoice != null) { if (escape) HideGlueCoatingChoice(); return; }
        if (invalid || escape) { CloseGlue(); Render(); return; }
        bool rotate = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
        if (rotate) RotateGluePart();
    }

    /// <summary>선택한 접착 원본을 한 번 회전하고 전체 점유·접촉 미리 보기를 갱신한다.</summary>
    private void RotateGluePart()
    {
        if (_glueOverlay == null) return;
        if (_glueDragCard != null)
        {
            _glueDragRotation = (_glueDragRotation + 1) % 4;
            _dragGhost.sizeDelta = new Vector2(72 * SpellGame.ShapeWidth(_glueDragCard, _glueDragRotation), 72 * SpellGame.ShapeHeight(_glueDragCard, _glueDragRotation));
            _dragGhost.GetComponent<SpellShapeGraphic>().SetCard(_glueDragCard, _glueDragRotation, _game.IsCursed(_glueDragCard), SealColor);
            return;
        }
        int part = _glueGraphic.IsDragging ? _glueGraphic.DragPart : _glueSelected;
        if (_glueCards[part] == null) return;
        int previousHeight = SpellGame.ShapeHeight(_glueCards[part], _glueRotations[part]);
        _glueRotations[part] = (_glueRotations[part] + 1) % 4;
        if (_glueGraphic.IsDragging) _glueGraphic.RotateDragOffset(previousHeight);
        else MoveGluePart(part, _gluePositions[part]);
    }

    /// <summary>접착 모달 전용 버튼을 만들고 오래된 화면 콜백과 Space 제출을 방지한다.</summary>
    private UnityEngine.UI.Button GlueButton(RectTransform overlay, string caption, float x, float y, float width, float height, Action action)
    {
        RectTransform rect = Panel(overlay, x, y, width, height, new Color(0.23f, 0.32f, 0.37f));
        UnityEngine.UI.Button button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        UnityEngine.UI.Navigation navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
        button.onClick.AddListener(() => { if (isActiveAndEnabled && _glueOverlay != null && _glueOverlay == overlay) action(); });
        Label(rect, caption, 4, 2, width - 8, height - 4, 18);
        return button;
    }

    /// <summary>전투와 분리된 접착 작업 격자, 손패 선택 및 확인·취소 UI를 생성한다.</summary>
    private void RenderGlue()
    {
        if (_glueOverlay != null) { _glueOverlay.gameObject.SetActive(false); Destroy(_glueOverlay.gameObject); }
        _glueOverlay = Panel(_root, 0, 0, 1280, 720, new Color(0.025f, 0.035f, 0.04f, 0.96f));
        Label(_glueOverlay, "접착 작업대", 350, 25, 580, 45, 30);
        Label(_glueOverlay, "일반·특수 조각 2장 선택 → 드래그·R로 변 맞추기 → 접착 확인", 140, 85, 1000, 35, 19);
        RectTransform board = Element("Glue Workbench", _glueOverlay, 200, 170, 576, 288);
        board.gameObject.AddComponent<CanvasRenderer>();
        _glueGraphic = board.gameObject.AddComponent<SpellGlueGraphic>(); _glueGraphic.Setup(this);
        RectTransform hint = Element("Glue Status", _glueOverlay, 825, 175, 400, 110);
        _glueHint = hint.gameObject.AddComponent<UnityEngine.UI.Text>();
        _glueHint.font = _font; _glueHint.fontSize = 20; _glueHint.color = Color.white; _glueHint.raycastTarget = false;
        GlueButton(_glueOverlay, "회전 · R", 850, 295, 280, 45, RotateGluePart);
        _glueConfirm = GlueButton(_glueOverlay, "접착 확인", 850, 360, 280, 55, ConfirmGlue);
        GlueButton(_glueOverlay, "취소 · Esc", 850, 435, 280, 45, () => { CloseGlue(); Render(); });
        GlueButton(_glueOverlay, "선택 조각 중앙으로", 850, 505, 280, 40, () => MoveGluePart(_glueSelected, new Vector2Int(3, 1)));
        Label(_glueOverlay, "카드 2장 → 1장 · 현재 전투만 유지\n드래그 또는 빈 칸 클릭으로 이동 · 손패 재클릭으로 선택 해제", 100, 490, 660, 65, 17);
        for (int i = 0; i < _game.Hand.Count; i++)
        {
            SpellGame.Card card = _game.Hand[i];
            bool selected = _glueCards[0] == card || _glueCards[1] == card;
            UnityEngine.UI.Button button = GlueButton(_glueOverlay, "", 65 + i * 165, 585, 155, 120, () => SelectGlueCard(card));
            button.GetComponent<UnityEngine.UI.Image>().color = CardColor(card, selected);
            button.gameObject.AddComponent<SpellCardDrag>().Setup(this, card);
            RectTransform iconRect = Element("Glue Hand Shape", (RectTransform)button.transform, 5, 3, 145, 64);
            iconRect.gameObject.AddComponent<CanvasRenderer>();
            SpellShapeGraphic icon = iconRect.gameObject.AddComponent<SpellShapeGraphic>(); icon.SetCard(card, isCursed: _game.IsCursed(card), sealColor: SealColor); icon.color = ShapeTint(card); icon.raycastTarget = false;
            Label((RectTransform)button.transform, CardName(card), 3, 70, 149, 45, 13);
        }
        RefreshGlue();
    }


    public bool CanDragHammerCard { get { return isActiveAndEnabled && (_hammerPhase == HammerPhase.Selecting || _hammerPhase == HammerPhase.Timing); } }
    public bool CanDragGlueCard { get { return isActiveAndEnabled && _glueOverlay != null; } }

    /// <summary>모달 손패에서 끄는 카드 전체 모양을 포인터 위치에 표시한다.</summary>
    private void CreateModalDragGhost(SpellGame.Card card, Vector2 position)
    {
        int rotation = CanDragGlueCard ? _glueDragRotation : CanDragHammerCard ? 0 : _rotation;
        _dragGhost = Element("Drag Preview", _root, 0, 0, 72 * SpellGame.ShapeWidth(card, rotation), 72 * SpellGame.ShapeHeight(card, rotation));
        _dragGhost.pivot = new Vector2(0.5f, 0.5f);
        _dragGhost.gameObject.AddComponent<CanvasRenderer>();
        SpellShapeGraphic icon = _dragGhost.gameObject.AddComponent<SpellShapeGraphic>();
        icon.raycastTarget = false; icon.SetCard(card, rotation, _game.IsCursed(card), SealColor); icon.color = ShapeTint(card);
        MoveCardDrag(position);
    }
    /// <summary>선택 여부와 카드 효과에 맞춘 모달 손패 배경색을 반환한다.</summary>
    private Color CardColor(SpellGame.Card card, bool selected)
    {
        return selected ? new Color(0.65f, 0.57f, 0.3f) : HasTraits(card) ? AccentColor(card) * 0.65f : _dimColor;
    }

    /// <summary>기존 UI 위에 봉인 안내 레이어와 TestScene 전용 적 진입 버튼을 연결한다.</summary>
    private void SetupFeatureControls()
    {
        _sealLayer = Element("Sealed Patterns", _root, 0, 0, 1280, 720);
        _testControls = Element("Enemy Encounters", _root, 0, 0, 1280, 720);
        Button(_testControls, "형상 봉인자", 120, 620, 260, 50, () => StartRun(true), null, 18);
        Button(_testControls, "도둑손", 510, 620, 260, 50, () => StartRun(false, true), null, 18);
        Button(_testControls, "조각 저주술사", 900, 620, 260, 50, () => StartRun(false, false, true), null, 18);
    }

    /// <summary>단계에 따라 기능 버튼·봉인·저주 안내와 적 진입 버튼을 갱신한다.</summary>
    private void RefreshFeatureControls()
    {
        bool combat = _game.CurrentPhase == SpellGame.Phase.Combat;
        _sealLayer.gameObject.SetActive(combat);
        _testControls.gameObject.SetActive(_game.CurrentPhase == SpellGame.Phase.Title && SceneManager.GetActiveScene().path == "Assets/Scenes/TestScene.unity");
        if (!combat) { ClearTheftApproach(); return; }
        RenderSeals(_sealLayer); RenderTheftHand();
        if (_game.Curse != null && !_game.Curse.IsResolved)
            Label(_sealLayer, $"[저주] 카드 #{_game.Curse.Target.Id} · 시전/망치 분해로 해제 · 미해결 HP -{_game.Curse.Amount}", 180, 20, 920, 35, 18);
    }

    /// <summary>현재 저주 대상의 실제 3D 조각 색상을 갱신한다.</summary>
    private void RefreshCurseTint()
    {
        _table.RefreshCurseTint();
    }

}

