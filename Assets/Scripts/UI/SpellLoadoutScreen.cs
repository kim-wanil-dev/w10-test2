using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>전투 사이 보상·도안 배치 통합 화면. 위쪽에는 보상 후보 3개, 아래쪽 왼쪽에는 전투에 쓰는 내 격자, 오른쪽에는 4×4 보관함 격자를 둔다.
/// 보상 도안은 격자·보관함에, 보상 조각은 내 격자 어디든 시험으로 놓아 보고 결정한다. 보관함 도안은 버릴 수 있다.</summary>
public sealed class SpellLoadoutScreen : MonoBehaviour
{
    // 누른 뒤 이 픽셀 이상 움직이면 클릭이 아니라 드래그로 본다.
    private const float DRAG_THRESHOLD = 8f;
    private const float CELL = 100f;
    private const float GRID_SIZE = CELL * 4;
    private const float GRID_X = 330f;
    private const float STORAGE_X = 1190f;
    private const float FRAME_PAD = 22f;
    private const float LOWER_WITH_REWARDS = 495f;
    private const float LOWER_WITHOUT_REWARDS = 250f;
    private const float CARD_TOP = 200f;
    private const float CARD_SCALE = 0.72f;
    private const float CARD_SPACING = 380f;
    private const string DEFAULT_HINT = "도안을 끌어 격자·보관함 사이로 옮기기 · 클릭하면 들고 다시 클릭해 놓기 · R / 우클릭 회전 · 버리기 칸에 놓으면 도안 버리기";
    private const string REWARD_HINT = "보상 카드를 끌어 놓아 보고 결정하세요 · 도안은 격자·보관함에, 조각은 내 격자 어디에나 · 카드를 클릭하면 빈 곳에 자동으로 놓기";
    private static readonly Color PANEL_COLOR = new Color(0.07f, 0.08f, 0.11f, 0.96f);
    private static readonly Color ACCENT_COLOR = new Color(0.98f, 0.8f, 0.36f);
    private static readonly Color DANGER_COLOR = new Color(0.88f, 0.38f, 0.35f);
    private static readonly Color TEXT_COLOR = new Color(0.94f, 0.92f, 0.87f);

    [Header("구성")]
    [SerializeField] private SpellLoadoutGrid _grid;
    [SerializeField] private RectTransform _storage;
    [SerializeField] private Button _storageButton;
    [SerializeField] private SpellRewardCardView _storageTemplate;
    [SerializeField] private Text _storageEmpty;
    [SerializeField] private SpellButtonView _start;
    [SerializeField] private Text _hint;

    private enum Source { None, Grid, Storage, RewardPattern, RewardPiece, TrialPiece }

    // 보관함 배치다. SpellPatternLayout 생성은 도안 데이터를 읽으므로 MonoBehaviour 생성자가 아닌 Setup에서 만든다.
    private SpellPatternLayout _storageLayout;
    private readonly List<int> _shown = new List<int>();
    private readonly List<SpellRewardCardView> _cards = new List<SpellRewardCardView>();
    private readonly List<int> _cardIndices = new List<int>();
    private readonly List<Button> _tabs = new List<Button>();
    private readonly List<SpellGame.RewardKind> _tabKinds = new List<SpellGame.RewardKind>();
    private readonly List<RaycastResult> _raycasts = new List<RaycastResult>();

    private SpellGame _game;
    private SpellPatternLayout _layout;
    private Func<int, Color> _colorOf;
    private Func<SpellGame.Card, Color> _pieceTint;
    private Func<SpellGame.Card, Color> _pieceAccent;
    private Func<SpellGame.Card, string> _pieceText;
    private Func<SpellGame.Seal, Color> _sealColor;
    private Action _onChanged;
    private Action _onContinue;
    private Font _font;
    private bool _isBuilt;

    private SpellLoadoutGrid _storageGrid;
    private RectTransform _gridFrame, _storageFrame, _tabRow, _decide, _trash, _confirm, _ghost;
    private Text _title, _subtitle, _gridLabel, _storageLabel, _trashLabel, _confirmText;
    private Image _trashImage;
    private SpellShapeGraphic _ghostShape;
    private SpellGame.RewardKind _tab = SpellGame.RewardKind.Pattern;
    // 탭을 마지막으로 정한 전투 번호다. 새 보상 단계가 열리면 도안 보상 탭부터 보여 주는 데 쓴다.
    private int _tabBattle = -1;

    // 시험 배치: 보상 도안(격자나 보관함에 놓임) 또는 보상 조각(내 격자에 놓임) 하나만 둔다.
    private int _trialPattern = -1, _trialPatternIndex = -1;
    private SpellGame.Card _trialPiece;
    private int _trialPieceIndex = -1, _trialPieceRotation;
    private Vector2Int _trialPieceAnchor;

    // 누름·들기 상태다. 끌면 놓을 때 내려놓고, 클릭으로 들면 다음 클릭에 내려놓는다.
    private Source _pressSource, _heldSource;
    private int _pressSpell = -1, _pressIndex = -1;
    private Vector2 _pressPosition;
    private bool _pressed, _holding, _clickHold;
    private int _heldSpell = -1, _heldIndex = -1, _heldRotation;
    private SpellGame.Card _heldPiece;
    private Vector2Int _origin;
    private int _originRotation;
    private int _pendingDiscard = -1;

    /// <summary>게임 상태, 내 격자 배치, 색·설명 함수와 화면 갱신·진행 동작을 연결하고 화면 요소를 코드로 배치한다.</summary>
    public void Setup(SpellGame game, SpellPatternLayout layout, Func<int, Color> colorOf, Func<SpellGame.Card, Color> pieceTint, Func<SpellGame.Card, Color> pieceAccent,
        Func<SpellGame.Card, string> pieceText, Func<SpellGame.Seal, Color> sealColor, Action onChanged, Action onContinue)
    {
        _game = game; _layout = layout; _colorOf = colorOf; _pieceTint = pieceTint; _pieceAccent = pieceAccent; _pieceText = pieceText; _sealColor = sealColor;
        _onChanged = onChanged; _onContinue = onContinue;
        if (_storageLayout == null) _storageLayout = new SpellPatternLayout();
        Build();
    }

    /// <summary>새 게임을 시작할 때 보관함 배치와 시험 배치를 비운다.</summary>
    public void ResetStorage()
    {
        if (_storageLayout == null) return;
        _storageLayout.Reset();
        for (int i = 0; i < SpellGame.PatternCount; i++) _storageLayout.Unplace(i);
        _trialPattern = _trialPatternIndex = -1; _trialPiece = null; _trialPieceIndex = -1;
        _tabBattle = -1;
        CancelHold();
    }

    void OnDisable()
    {
        CancelHold();
        _pressed = false;
        CloseConfirm();
        HideGhost();
    }

    /// <summary>계속 버튼 문구를 바꾼다.</summary>
    public void SetContinueLabel(string label) { _start.SetLabel(label); }

    /// <summary>화면 아래 안내 문구를 바꾼다.</summary>
    public void ShowHint(string text) { _hint.text = text; }

    void Update()
    {
        if (!_isBuilt || _game == null) return;
        Keyboard keyboard = Keyboard.current; Mouse mouse = Mouse.current;
        if (_confirm != null && _confirm.gameObject.activeSelf)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CloseConfirm();
            return;
        }
        if (mouse == null) return;
        Vector2 pointer = mouse.position.ReadValue();
        bool rotate = (keyboard != null && keyboard.rKey.wasPressedThisFrame) || mouse.rightButton.wasPressedThisFrame;
        if (_holding)
        {
            if (rotate) RotateHeld();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { CancelHold(); Redraw(); return; }
            UpdateHover(pointer);
            if (_clickHold ? mouse.leftButton.wasPressedThisFrame : mouse.leftButton.wasReleasedThisFrame) Drop(pointer);
            return;
        }
        // 들고 있지 않을 때 R은 시험으로 놓은 조각을 제자리에서 돌린다.
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame && _trialPiece != null) RotateTrialPiece();
        if (mouse.leftButton.wasPressedThisFrame) BeginPress(pointer);
        if (_pressed && mouse.leftButton.isPressed && (pointer - _pressPosition).sqrMagnitude >= DRAG_THRESHOLD * DRAG_THRESHOLD) { StartHold(false); UpdateHover(pointer); return; }
        if (_pressed && mouse.leftButton.wasReleasedThisFrame) ReleaseClick();
    }

    // ───────────── 입력 ─────────────

    /// <summary>누른 위치의 시험 조각, 격자 도안, 보관함 도안, 보상 카드를 들기 후보로 기억한다.</summary>
    private void BeginPress(Vector2 pointer)
    {
        _pressed = false; _pressSource = Source.None; _pressSpell = _pressIndex = -1;
        if (_grid.TryScreenPoint(pointer, out Vector2Int cell, out int sector))
        {
            if (_grid.PieceCovers(cell, sector)) _pressSource = Source.TrialPiece;
            else if (_layout.TryFindOwner(cell.x, cell.y, sector, _shown, out int spell)) { _pressSource = Source.Grid; _pressSpell = spell; }
        }
        else if (_storageGrid.TryScreenPoint(pointer, out cell, out sector))
        {
            if (_storageLayout.TryFindOwner(cell.x, cell.y, sector, _shown, out int spell)) { _pressSource = Source.Storage; _pressSpell = spell; }
        }
        else if (EventSystem.current != null)
        {
            PointerEventData data = new PointerEventData(EventSystem.current) { position = pointer };
            _raycasts.Clear(); EventSystem.current.RaycastAll(data, _raycasts);
            foreach (RaycastResult result in _raycasts)
            {
                SpellRewardCardView card = result.gameObject.GetComponentInParent<SpellRewardCardView>();
                int index = card == null ? -1 : _cards.IndexOf(card);
                if (index < 0 || index >= _cardIndices.Count || !card.gameObject.activeInHierarchy) continue;
                _pressSource = _tab == SpellGame.RewardKind.Pattern ? Source.RewardPattern : Source.RewardPiece;
                _pressIndex = _cardIndices[index];
                if (_pressSource == Source.RewardPattern) _pressSpell = _game.PatternRewards[_pressIndex].Pattern;
                break;
            }
        }
        if (_pressSource == Source.None) return;
        _pressed = true; _pressPosition = pointer;
    }

    /// <summary>끌지 않고 뗐을 때: 보상 카드는 빈 곳에 자동 시험 배치하고, 격자·보관함 도안과 시험 조각은 클릭으로 든다.</summary>
    private void ReleaseClick()
    {
        _pressed = false;
        if (_pressSource == Source.RewardPattern || _pressSource == Source.RewardPiece) { AutoTrial(_pressSource, _pressIndex); return; }
        StartHold(true);
    }

    /// <summary>누른 대상을 든다. 격자·보관함 도안은 원래 자리를 기억하고 판에서 뺀다. 보상 카드는 기존 시험 배치를 지우고 새로 든다.</summary>
    private void StartHold(bool clickHold)
    {
        _pressed = false; _holding = true; _clickHold = clickHold; _heldSource = _pressSource;
        _heldSpell = -1; _heldPiece = null; _heldIndex = -1;
        switch (_pressSource)
        {
            case Source.Grid:
                _heldSpell = _pressSpell; _origin = _layout.Origin(_heldSpell); _originRotation = _heldRotation = _layout.Rotation(_heldSpell); _layout.Unplace(_heldSpell); break;
            case Source.Storage:
                _heldSpell = _pressSpell; _origin = _storageLayout.Origin(_heldSpell); _originRotation = _heldRotation = _storageLayout.Rotation(_heldSpell); _storageLayout.Unplace(_heldSpell); break;
            case Source.RewardPattern:
                ClearTrial(); _heldSpell = _pressSpell; _heldIndex = _pressIndex; _heldRotation = 0; break;
            case Source.RewardPiece:
                ClearTrial(); _heldPiece = _game.Rewards[_pressIndex].Piece; _heldIndex = _pressIndex; _heldRotation = 0; break;
            case Source.TrialPiece:
                _heldPiece = _trialPiece; _heldIndex = _trialPieceIndex; _heldRotation = _originRotation = _trialPieceRotation; _origin = _trialPieceAnchor; break;
        }
        ShowHint(_heldPiece != null ? "내 격자 어디에나 놓아 보세요 · R / 우클릭 회전 · Esc 취소" : "격자나 보관함에 놓으세요 · R / 우클릭 회전 · 버리기 칸에 놓으면 버리기 · Esc 취소");
        Redraw();
    }

    /// <summary>든 도안·조각을 시계 방향으로 돌린다.</summary>
    private void RotateHeld()
    {
        _heldRotation = (_heldRotation + 1) % 4;
        Redraw();
    }

    /// <summary>들고 있는 대상의 놓기 미리 보기와 포인터를 따라가는 고스트, 버리기 칸 강조를 갱신한다.</summary>
    private void UpdateHover(Vector2 pointer)
    {
        bool overGrid = _grid.TryScreenPoint(pointer, out _, out _);
        bool overStorage = _heldSpell >= 0 && _storageGrid.TryScreenPoint(pointer, out _, out _);
        _grid.HoverAt(pointer);
        _storageGrid.HoverAt(_heldSpell >= 0 ? pointer : new Vector2(-99999, -99999));
        bool overTrash = RectTransformUtility.RectangleContainsScreenPoint(_trash, pointer, EventCamera());
        _trashImage.color = overTrash ? new Color(DANGER_COLOR.r, DANGER_COLOR.g, DANGER_COLOR.b, 0.55f) : new Color(0.25f, 0.1f, 0.1f, 0.6f);
        if (overGrid || overStorage) { HideGhost(); return; }
        ShowGhost(pointer);
    }

    /// <summary>놓은 위치에 따라 격자·보관함 배치, 조각 시험 배치, 버리기, 원위치를 처리한다.</summary>
    private void Drop(Vector2 pointer)
    {
        _holding = false; HideGhost();
        Camera camera = EventCamera();
        bool overTrash = RectTransformUtility.RectangleContainsScreenPoint(_trash, pointer, camera);
        string issue = null;
        if (_heldSpell >= 0)
        {
            int spell = _heldSpell;
            bool isReward = _heldSource == Source.RewardPattern || spell == _trialPattern;
            if (_grid.TryScreenPoint(pointer, out Vector2Int cell, out _))
            {
                _grid.SetHeld(spell, _heldRotation);
                Vector2Int origin = _grid.HeldOrigin(cell);
                issue = _layout.MoveIssue(spell, origin, _heldRotation, _shown);
                if (issue == null) { PlaceHeldPattern(_layout, spell, origin); return; }
            }
            else if (_storageGrid.TryScreenPoint(pointer, out cell, out _))
            {
                _storageGrid.SetHeld(spell, _heldRotation);
                Vector2Int origin = _storageGrid.HeldOrigin(cell);
                issue = _storageLayout.MoveIssue(spell, origin, _heldRotation, _shown);
                if (issue == null) { PlaceHeldPattern(_storageLayout, spell, origin); return; }
            }
            else if (overTrash)
            {
                if (isReward) { if (spell == _trialPattern) _trialPattern = _trialPatternIndex = -1; FinishHold(); return; }
                RestoreHeldPattern(); FinishHold(); OpenConfirm(spell); return;
            }
            RestoreHeldPattern(); FinishHold();
            if (issue != null) ShowHint(issue);
            return;
        }
        if (_heldPiece != null)
        {
            if (_grid.TryScreenPoint(pointer, out Vector2Int cell, out _))
            {
                _grid.SetHeldPiece(_heldPiece, _heldRotation, _pieceTint(_heldPiece));
                Vector2Int anchor = _grid.HeldPieceAnchor(cell);
                if (SpellLoadoutGrid.PieceFits(_heldPiece, anchor, _heldRotation))
                {
                    _trialPiece = _heldPiece; _trialPieceIndex = _heldIndex; _trialPieceAnchor = anchor; _trialPieceRotation = _heldRotation;
                    FinishHold(); return;
                }
                issue = "조각이 격자 밖으로 나갑니다";
            }
            else if (overTrash) { if (_heldSource == Source.TrialPiece) { _trialPiece = null; _trialPieceIndex = -1; } FinishHold(); return; }
            // 시험 조각을 격자 밖에 놓으면 원래 자리에 그대로 둔다.
            FinishHold();
            if (issue != null) ShowHint(issue);
        }
        else FinishHold();
    }

    /// <summary>든 도안을 지정 판(내 격자 또는 보관함)에 놓고 다른 판에서는 뺀다. 보상 카드에서 든 도안이면 시험 배치로 기록한다.</summary>
    private void PlaceHeldPattern(SpellPatternLayout target, int spell, Vector2Int origin)
    {
        (target == _layout ? _storageLayout : _layout).Unplace(spell);
        target.Place(spell, origin, _heldRotation);
        if (_heldSource == Source.RewardPattern) { _trialPattern = spell; _trialPatternIndex = _heldIndex; }
        FinishHold();
    }

    /// <summary>격자·보관함에서 든 도안을 원래 자리·방향으로 되돌린다. 보상 카드에서 든 도안은 그냥 내려놓는다.</summary>
    private void RestoreHeldPattern()
    {
        if (_heldSource == Source.Grid) _layout.Place(_heldSpell, _origin, _originRotation);
        else if (_heldSource == Source.Storage) _storageLayout.Place(_heldSpell, _origin, _originRotation);
    }

    /// <summary>들기를 끝내고 화면을 다시 그린다.</summary>
    private void FinishHold()
    {
        _holding = false; _heldSpell = -1; _heldPiece = null; _heldIndex = -1; _heldSource = Source.None;
        Redraw();
    }

    /// <summary>들고 있던 대상을 원래 자리로 되돌리고 들기를 끝낸다.</summary>
    private void CancelHold()
    {
        if (!_holding) return;
        if (_heldSpell >= 0) RestoreHeldPattern();
        _holding = false; _heldSpell = -1; _heldPiece = null; _heldIndex = -1; _heldSource = Source.None;
        HideGhost();
    }

    // ───────────── 시험 배치·결정 ─────────────

    /// <summary>보상 카드를 클릭하면 빈 곳에 자동으로 시험 배치한다. 도안은 내 격자, 없으면 보관함, 조각은 내 격자의 첫 빈 위치다.</summary>
    private void AutoTrial(Source source, int index)
    {
        ClearTrial();
        if (source == Source.RewardPattern)
        {
            int spell = _game.PatternRewards[index].Pattern;
            RebuildShown(spell);
            if (_layout.TryAutoPlace(spell, _shown) || _storageLayout.TryAutoPlace(spell, _shown)) { _trialPattern = spell; _trialPatternIndex = index; }
            else ShowHint("놓을 자리가 없습니다 · 격자나 보관함의 도안을 버리기 칸에 끌어 자리를 만드세요");
        }
        else
        {
            SpellGame.Card card = _game.Rewards[index].Piece;
            for (int y = 0; y < SpellPatternLayout.ROWS && _trialPiece == null; y++)
                for (int x = 0; x < SpellPatternLayout.COLUMNS && _trialPiece == null; x++)
                    if (SpellLoadoutGrid.PieceFits(card, new Vector2Int(x, y), 0)) { _trialPiece = card; _trialPieceIndex = index; _trialPieceAnchor = new Vector2Int(x, y); _trialPieceRotation = 0; }
        }
        Redraw();
    }

    /// <summary>시험으로 놓은 조각을 제자리에서 돌린다. 격자 밖으로 나가면 돌리지 않는다.</summary>
    private void RotateTrialPiece()
    {
        int rotation = (_trialPieceRotation + 1) % 4;
        if (!SpellLoadoutGrid.PieceFits(_trialPiece, _trialPieceAnchor, rotation)) { ShowHint("돌리면 격자 밖으로 나갑니다"); return; }
        _trialPieceRotation = rotation; Redraw();
    }

    /// <summary>시험 배치를 모두 지운다. 시험 도안은 격자·보관함에서 뺀다.</summary>
    private void ClearTrial()
    {
        if (_trialPattern >= 0) { _layout.Unplace(_trialPattern); _storageLayout.Unplace(_trialPattern); }
        _trialPattern = _trialPatternIndex = -1;
        _trialPiece = null; _trialPieceIndex = -1;
    }

    /// <summary>시험 배치한 보상을 수령한다. 도안은 놓아 둔 자리에 그대로 남는다.</summary>
    private void Decide()
    {
        if (_holding) return;
        bool claimed = false;
        if (_trialPattern >= 0) { claimed = _game.ClaimReward(SpellGame.RewardKind.Pattern, _trialPatternIndex); if (claimed) _trialPattern = _trialPatternIndex = -1; }
        else if (_trialPiece != null) { claimed = _game.ClaimReward(SpellGame.RewardKind.Piece, _trialPieceIndex); if (claimed) { _trialPiece = null; _trialPieceIndex = -1; } }
        if (!claimed) { ShowHint("먼저 보상 카드를 끌어 놓아 보세요"); return; }
        // 받을 보상이 더 없으면 도안 배치만 남은 화면은 건너뛰고 바로 다음 단계로 진행한다.
        if (AvailableKinds().Count == 0) { _onContinue?.Invoke(); return; }
        _onChanged?.Invoke();
    }

    /// <summary>시험 배치를 지우고 다음 단계로 진행한다.</summary>
    private void Continue()
    {
        CancelHold(); ClearTrial();
        _onContinue?.Invoke();
    }

    /// <summary>보상 종류 탭을 고른다. 인장은 인장 작업대를 연다.</summary>
    private void SelectTab(SpellGame.RewardKind kind)
    {
        if (_holding) return;
        if (kind == SpellGame.RewardKind.Seal)
        {
            ClearTrial();
            if (_game.OpenReward(SpellGame.RewardKind.Seal)) _onChanged?.Invoke();
            return;
        }
        if (_tab == kind) return;
        ClearTrial(); _tab = kind; Redraw();
    }

    // ───────────── 버리기 ─────────────

    /// <summary>도안 버리기 확인 창을 연다.</summary>
    private void OpenConfirm(int spell)
    {
        _pendingDiscard = spell;
        _confirmText.text = $"<b>{SpellGame.PatternName(spell)}</b> 도안을 버릴까요?\n<size=20>버린 도안은 되찾을 수 없습니다.</size>";
        _confirm.gameObject.SetActive(true); _confirm.SetAsLastSibling();
    }

    /// <summary>확인 창을 닫는다.</summary>
    private void CloseConfirm()
    {
        _pendingDiscard = -1;
        if (_confirm != null) _confirm.gameObject.SetActive(false);
    }

    /// <summary>확인한 도안을 격자·보관함에서 빼고 보유 목록에서 버린다.</summary>
    private void ConfirmDiscard()
    {
        int spell = _pendingDiscard; CloseConfirm();
        if (spell < 0) return;
        if (!_game.DiscardPattern(spell)) { ShowHint("지금은 도안을 버릴 수 없습니다"); return; }
        _layout.Unplace(spell); _storageLayout.Unplace(spell);
        _onChanged?.Invoke();
        ShowHint($"{SpellGame.PatternName(spell)} 도안을 버렸습니다");
    }

    // ───────────── 그리기 ─────────────

    /// <summary>게임 상태가 바뀌었을 때 호출한다. 보상 탭·카드, 두 격자, 버튼을 다시 그린다.</summary>
    public void Refresh()
    {
        if (!_isBuilt) return;
        CloseConfirm();
        Redraw();
        ShowHint(HasRewards() ? REWARD_HINT : DEFAULT_HINT);
    }

    /// <summary>보상 단계이고 아직 받을 보상이 남았는지 반환한다.</summary>
    private bool HasRewards() { return _game.CurrentPhase == SpellGame.Phase.Reward && AvailableKinds().Count > 0; }

    /// <summary>받을 수 있는 보상 종류 목록이다.</summary>
    private List<SpellGame.RewardKind> AvailableKinds()
    {
        List<SpellGame.RewardKind> kinds = new List<SpellGame.RewardKind>();
        if (_game.CurrentPhase != SpellGame.Phase.Reward) return kinds;
        // 탭 순서도 도안 보상이 먼저다.
        if (_game.HasPatternReward && _game.PatternRewards.Count > 0) kinds.Add(SpellGame.RewardKind.Pattern);
        if (_game.HasPieceReward && _game.Rewards.Count > 0) kinds.Add(SpellGame.RewardKind.Piece);
        if (_game.HasSealReward) kinds.Add(SpellGame.RewardKind.Seal);
        return kinds;
    }

    /// <summary>보유 도안과 시험 도안(추가로 extra)을 격자 판정·그리기 목록으로 다시 만든다.</summary>
    private void RebuildShown(int extra = -1)
    {
        _shown.Clear();
        _shown.AddRange(_game.OwnedSpells);
        if (_trialPattern >= 0 && !_shown.Contains(_trialPattern)) _shown.Add(_trialPattern);
        if (extra >= 0 && !_shown.Contains(extra)) _shown.Add(extra);
        if (_holding && _heldSpell >= 0 && !_shown.Contains(_heldSpell)) _shown.Add(_heldSpell);
    }

    /// <summary>현재 상태로 화면 전체를 다시 배치하고 그린다.</summary>
    private void Redraw()
    {
        List<SpellGame.RewardKind> kinds = AvailableKinds();
        // 새 보상 단계는 도안 보상을 먼저 보여 준다. 고른 탭이 수령 등으로 사라졌을 때도 도안 → 조각 순으로 고른다.
        bool newRewards = _game.CurrentPhase == SpellGame.Phase.Reward && _tabBattle != _game.Battle;
        if (newRewards) _tabBattle = _game.Battle;
        if (kinds.Count > 0 && (newRewards || !kinds.Contains(_tab) || _tab == SpellGame.RewardKind.Seal))
            _tab = kinds.Contains(SpellGame.RewardKind.Pattern) ? SpellGame.RewardKind.Pattern : kinds.Contains(SpellGame.RewardKind.Piece) ? SpellGame.RewardKind.Piece : _tab;
        // 받을 수 없게 된 보상의 시험 배치는 지운다.
        if (_trialPattern >= 0 && !kinds.Contains(SpellGame.RewardKind.Pattern)) ClearTrial();
        if (_trialPiece != null && !kinds.Contains(SpellGame.RewardKind.Piece)) ClearTrial();
        RebuildShown();
        int overflow = FillStorage();
        bool rewards = kinds.Count > 0 && _tab != SpellGame.RewardKind.Seal;
        float lower = kinds.Count > 0 ? LOWER_WITH_REWARDS : LOWER_WITHOUT_REWARDS;

        _title.text = kinds.Count > 0 ? "전투 보상" : "도안 배치";
        _subtitle.text = kinds.Count > 0 ? "보상을 끌어 놓아 보고 결정하세요 · 받지 않을 보상은 그대로 두고 진행" : "내 격자에 놓인 도안만 다음 전투에 장착됩니다";
        TopLeft((RectTransform)_title.transform, 0, kinds.Count > 0 ? 24 : 70, 1920, 64);
        TopLeft((RectTransform)_subtitle.transform, 0, kinds.Count > 0 ? 88 : 140, 1920, 36);

        RefreshTabs(kinds);
        RefreshCards(rewards);

        TopLeft((RectTransform)_grid.transform, GRID_X, lower, GRID_SIZE, GRID_SIZE);
        TopLeft((RectTransform)_storageGrid.transform, STORAGE_X, lower, GRID_SIZE, GRID_SIZE);
        TopLeft(_gridFrame, GRID_X - FRAME_PAD, lower - FRAME_PAD, GRID_SIZE + FRAME_PAD * 2, GRID_SIZE + FRAME_PAD * 2);
        TopLeft(_storageFrame, STORAGE_X - FRAME_PAD, lower - FRAME_PAD, GRID_SIZE + FRAME_PAD * 2, GRID_SIZE + FRAME_PAD * 2);
        TopLeft((RectTransform)_gridLabel.transform, GRID_X - FRAME_PAD, lower - FRAME_PAD - 44, GRID_SIZE + FRAME_PAD * 2, 40);
        TopLeft((RectTransform)_storageLabel.transform, STORAGE_X - FRAME_PAD, lower - FRAME_PAD - 44, GRID_SIZE + FRAME_PAD * 2, 40);
        _storageLabel.text = overflow > 0 ? $"보관함 · <color=#E0605A>자리가 없는 도안 {overflow}개</color>" : "보관함";
        float middle = (GRID_X + GRID_SIZE + STORAGE_X) / 2;
        TopLeft(_decide, middle - 130, lower + 40, 260, 76);
        TopLeft(_trash, middle - 110, lower + GRID_SIZE - 150, 220, 150);
        TopLeft((RectTransform)_hint.transform, 0, lower + GRID_SIZE + FRAME_PAD + 14, 1920, 36);

        bool hasTrial = _trialPattern >= 0 || _trialPiece != null;
        _decide.gameObject.SetActive(rewards && hasTrial && !_holding);

        foreach (SpellLoadoutGrid grid in new[] { _grid, _storageGrid }) { grid.SetTrial(_trialPattern); grid.SetHeld(_holding ? _heldSpell : -1, _heldRotation); }
        _grid.SetHeldPiece(_holding ? _heldPiece : null, _heldRotation, _heldPiece != null ? _pieceTint(_heldPiece) : Color.white);
        bool showTrialPiece = _trialPiece != null && !(_holding && _heldSource == Source.TrialPiece);
        _grid.SetPiece(showTrialPiece ? _trialPiece : null, _trialPieceAnchor, _trialPieceRotation, _trialPiece != null ? _pieceTint(_trialPiece) : Color.white);
    }

    /// <summary>어느 판에도 없는 보유 도안을 보관함 빈 곳에 넣고, 자리가 없어 넣지 못한 수를 반환한다.</summary>
    private int FillStorage()
    {
        int overflow = 0;
        foreach (int spell in _game.OwnedSpells)
        {
            if (_holding && spell == _heldSpell) continue;
            if (_layout.IsPlaced(spell)) { _storageLayout.Unplace(spell); continue; }
            if (_storageLayout.IsPlaced(spell)) continue;
            if (!_storageLayout.TryAutoPlace(spell, _shown)) overflow++;
        }
        return overflow;
    }

    /// <summary>받을 보상 종류가 둘 이상이면 종류 탭을 보여 준다. 인장 탭은 인장 작업대를 연다.</summary>
    private void RefreshTabs(List<SpellGame.RewardKind> kinds)
    {
        bool show = kinds.Count > 1 || kinds.Contains(SpellGame.RewardKind.Seal);
        _tabRow.gameObject.SetActive(show);
        if (!show) return;
        while (_tabs.Count < kinds.Count)
        {
            int slot = _tabs.Count;
            Button button = MakeButton(_tabRow, "Tab", "", PANEL_COLOR, 22, () => SelectTab(_tabKinds[slot]));
            _tabs.Add(button);
        }
        _tabKinds.Clear(); _tabKinds.AddRange(kinds);
        float width = 240, gap = 16, total = kinds.Count * width + (kinds.Count - 1) * gap;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool visible = i < kinds.Count;
            _tabs[i].gameObject.SetActive(visible);
            if (!visible) continue;
            SpellGame.RewardKind kind = kinds[i];
            bool active = kind == _tab;
            TopLeft((RectTransform)_tabs[i].transform, 960 - total / 2 + i * (width + gap), 0, width, 46);
            _tabs[i].GetComponent<Image>().color = active ? new Color(0.22f, 0.2f, 0.12f, 1f) : PANEL_COLOR;
            Text label = _tabs[i].GetComponentInChildren<Text>();
            label.text = kind == SpellGame.RewardKind.Piece ? "조각 보상" : kind == SpellGame.RewardKind.Pattern ? "도안 보상" : "인장 보상 · 작업대";
            label.color = active ? ACCENT_COLOR : TEXT_COLOR;
        }
    }

    /// <summary>현재 탭의 보상 후보를 위쪽에 가로로 놓는다. 시험 배치 중인 카드는 표시한다.</summary>
    private void RefreshCards(bool show)
    {
        _cardIndices.Clear();
        int count = 0;
        if (show) count = _tab == SpellGame.RewardKind.Pattern ? _game.PatternRewards.Count : _game.Rewards.Count;
        while (_cards.Count < count) _cards.Add(Instantiate(_storageTemplate, transform));
        Vector2 size = ((RectTransform)_storageTemplate.transform).sizeDelta;
        for (int i = 0; i < _cards.Count; i++)
        {
            bool visible = i < count;
            _cards[i].gameObject.SetActive(visible);
            if (!visible) continue;
            _cardIndices.Add(i);
            RectTransform rect = (RectTransform)_cards[i].transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1); rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = size; rect.localScale = Vector3.one * CARD_SCALE;
            rect.anchoredPosition = new Vector2((i - (count - 1) / 2f) * CARD_SPACING, -CARD_TOP);
            if (_tab == SpellGame.RewardKind.Pattern)
            {
                int spell = _game.PatternRewards[i].Pattern;
                bool trial = spell == _trialPattern;
                _cards[i].BindPattern(i, SpellGame.PatternTitle(spell), spell, _colorOf(spell), _ => { }, trial ? "새 도안 · ▶ 시험 배치 중" : "새 도안");
            }
            else
            {
                SpellGame.Card card = _game.Rewards[i].Piece;
                bool trial = card == _trialPiece;
                _cards[i].BindPiece(i, (trial ? "▶ 시험 배치 중\n" : "") + _pieceText(card), card, _pieceAccent(card), _pieceTint(card), _sealColor, _ => { });
            }
        }
    }

    /// <summary>포인터를 따라가는 반투명 모양을 띄운다. 격자 위에서는 격자 미리 보기가 대신한다.</summary>
    private void ShowGhost(Vector2 pointer)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, pointer, EventCamera(), out Vector2 local)) return;
        const float scale = CELL * 0.7f;
        if (_heldSpell >= 0)
        {
            _ghostShape.SetPattern(_heldSpell);
            _ghost.sizeDelta = (Vector2)SpellPatternLayout.BaseSize(_heldSpell) * scale;
            _ghost.localRotation = Quaternion.Euler(0, 0, -90 * _heldRotation);
            Color color = _colorOf(_heldSpell); color.a = 0.8f; _ghostShape.color = color;
        }
        else if (_heldPiece != null)
        {
            _ghostShape.SetCard(_heldPiece, _heldRotation, false, _sealColor);
            _ghost.sizeDelta = SpellPiece3D.CellBounds(SpellGame.CardCells(_heldPiece, _heldRotation)) * scale;
            _ghost.localRotation = Quaternion.identity;
            Color color = _pieceTint(_heldPiece); color.a = 0.85f; _ghostShape.color = color;
        }
        else { HideGhost(); return; }
        _ghost.localPosition = local;
        _ghost.gameObject.SetActive(true); _ghost.SetAsLastSibling();
    }

    /// <summary>고스트를 숨긴다.</summary>
    private void HideGhost() { if (_ghost != null) _ghost.gameObject.SetActive(false); }

    // ───────────── 화면 구성 ─────────────

    /// <summary>기존 장착 화면 요소를 재배치하고, 보관함 격자·탭·결정 버튼·버리기 칸·확인 창·고스트를 코드로 만든다.</summary>
    private void Build()
    {
        if (_isBuilt) return;
        _font = _hint.font;
        _title = transform.Find("Title").GetComponent<Text>();
        _subtitle = transform.Find("Subtitle").GetComponent<Text>();
        _gridFrame = (RectTransform)transform.Find("Grid Frame");
        Transform oldLabel = transform.Find("Storage Label");
        if (oldLabel != null) oldLabel.gameObject.SetActive(false);
        _storageButton.gameObject.SetActive(false);
        _storageEmpty.gameObject.SetActive(false);
        _title.supportRichText = true;

        _grid.SetCellSize(CELL);
        _grid.Setup(_layout, _shown, _colorOf, _ => { });
        _storageFrame = Instantiate(_gridFrame.gameObject, transform).GetComponent<RectTransform>(); _storageFrame.name = "Storage Frame";
        _storageGrid = Instantiate(_grid.gameObject, transform).GetComponent<SpellLoadoutGrid>(); _storageGrid.name = "Storage Grid";
        _storageGrid.SetCellSize(CELL);
        _storageGrid.Setup(_storageLayout, _shown, _colorOf, _ => { });
        _storageFrame.SetSiblingIndex(_gridFrame.GetSiblingIndex());
        for (int i = 0; i < SpellGame.PatternCount; i++) _storageLayout.Unplace(i);

        _gridLabel = MakeText(transform, "Grid Label", "내 격자 · 전투에 장착", 24, TextAnchor.MiddleCenter, TEXT_COLOR);
        _storageLabel = MakeText(transform, "Storage Grid Label", "보관함", 24, TextAnchor.MiddleCenter, TEXT_COLOR);

        _tabRow = new GameObject("Reward Tabs", typeof(RectTransform)).GetComponent<RectTransform>();
        _tabRow.SetParent(transform, false); TopLeft(_tabRow, 0, 136, 1920, 46);

        _decide = (RectTransform)MakeButton(transform, "Decide", "이 보상으로 결정", new Color(0.2f, 0.17f, 0.08f, 1f), 26, Decide).transform;
        _decide.GetComponentInChildren<Text>().color = ACCENT_COLOR;

        _trash = new GameObject("Trash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        _trash.SetParent(transform, false);
        _trashImage = _trash.GetComponent<Image>(); _trashImage.color = new Color(0.25f, 0.1f, 0.1f, 0.6f); _trashImage.raycastTarget = false;
        _trashLabel = MakeText(_trash, "Label", "버리기\n<size=17>도안을 끌어다 놓기</size>", 26, TextAnchor.MiddleCenter, DANGER_COLOR);
        Stretch((RectTransform)_trashLabel.transform);

        RectTransform start = (RectTransform)_start.transform;
        start.anchorMin = start.anchorMax = new Vector2(1, 0); start.pivot = new Vector2(1, 0); start.anchoredPosition = new Vector2(-70, 50);

        _ghost = new GameObject("Held Ghost", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        _ghost.SetParent(transform, false); _ghost.anchorMin = _ghost.anchorMax = _ghost.pivot = new Vector2(0.5f, 0.5f);
        _ghostShape = _ghost.gameObject.AddComponent<SpellShapeGraphic>(); _ghostShape.raycastTarget = false;
        _ghost.gameObject.SetActive(false);

        BuildConfirm();
        _start.SetAction(Continue);
        _isBuilt = true;
    }

    /// <summary>도안 버리기 확인 창을 만든다. 화면 전체를 어둡게 덮어 다른 입력을 막는다.</summary>
    private void BuildConfirm()
    {
        _confirm = new GameObject("Discard Confirm", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        _confirm.SetParent(transform, false); Stretch(_confirm);
        _confirm.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        RectTransform box = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        box.SetParent(_confirm, false); box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(620, 280);
        box.GetComponent<Image>().color = PANEL_COLOR;
        _confirmText = MakeText(box, "Text", "", 30, TextAnchor.MiddleCenter, TEXT_COLOR);
        RectTransform text = (RectTransform)_confirmText.transform; text.anchorMin = new Vector2(0, 0.4f); text.anchorMax = Vector2.one; text.offsetMin = new Vector2(20, 0); text.offsetMax = new Vector2(-20, -20);
        Button yes = MakeButton(box, "Discard", "버리기", new Color(0.35f, 0.12f, 0.12f, 1f), 26, ConfirmDiscard);
        Button no = MakeButton(box, "Cancel", "취소", new Color(0.16f, 0.18f, 0.24f, 1f), 26, CloseConfirm);
        foreach (var (button, x) in new[] { (yes, -130f), (no, 130f) })
        {
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0); rect.sizeDelta = new Vector2(220, 64); rect.anchoredPosition = new Vector2(x, 28);
        }
        _confirm.gameObject.SetActive(false);
    }

    /// <summary>부모 왼쪽 위 기준 위치·크기로 요소를 놓는다.</summary>
    private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>요소를 부모 크기에 꽉 채운다.</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    /// <summary>글자 요소를 만든다.</summary>
    private Text MakeText(Transform parent, string name, string value, int size, TextAnchor alignment, Color color)
    {
        Text text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = _font; text.fontSize = size; text.alignment = alignment; text.color = color; text.text = value;
        text.supportRichText = true; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    /// <summary>배경과 가운데 글자를 가진 버튼을 만든다.</summary>
    private Button MakeButton(Transform parent, string name, string label, Color color, int size, Action onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        Button button = go.GetComponent<Button>(); button.onClick.AddListener(() => onClick());
        Text text = MakeText(go.transform, "Label", label, size, TextAnchor.MiddleCenter, TEXT_COLOR);
        Stretch((RectTransform)text.transform);
        return button;
    }

    /// <summary>캔버스 렌더 방식에 맞춰 화면 좌표 변환에 쓸 카메라를 반환한다. Overlay면 null이다.</summary>
    private Camera EventCamera()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }
}
