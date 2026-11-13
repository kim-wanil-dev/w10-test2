using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>첫 실행 온보딩. 첫 전투(B1a~B5b)와 첫 보상(R1~R4b)을 행동 하나씩 짧은 문구로 강제 안내한다. 안내 창은 한 단계 안에서 움직이지 않는다.
/// 단계는 게임 상태에서 매 프레임 판정하고, 현재 단계의 강조 영역만 밝게 남긴 채 나머지를 가린다.
/// 마우스를 직접 읽는 전투 판·보상 화면은 Allows… 함수로 입력을 거르고, 그 밖의 버튼은 가림막이 클릭을 막는다.
/// 문구는 Resources/SpellData/onboarding.json에서 읽는다.</summary>
public sealed class SpellOnboarding : MonoBehaviour
{
    public const string DONE_KEY = "SpellOnboardingDone";
    private const string TEXT_PATH = "SpellData/onboarding";
    private const float HOLE_PAD = 10f;
    private const float POPUP_WIDTH = 600f;
    private const float POPUP_PAD = 26f;
    private const float POPUP_GAP = 18f;
    private const float IDLE_SECONDS = 3f;
    private const float GHOST_SECONDS = 1.6f;
    private static readonly Color DIM_COLOR = new Color(0f, 0f, 0f, 0.78f);
    private static readonly Color PANEL_COLOR = new Color(0.08f, 0.1f, 0.14f, 0.98f);
    private static readonly Color ACCENT_COLOR = new Color(0.98f, 0.8f, 0.36f);
    private static readonly Color TEXT_COLOR = new Color(0.94f, 0.92f, 0.87f);
    private static readonly Color SUB_COLOR = new Color(0.68f, 0.7f, 0.76f);

    // 안내 창은 한 단계에 한두 줄만 보여 주고 행동마다 다음 단계로 넘긴다. 순서를 바꾸면 IsBattleStep·헤더 판정(R1 이후는 보상)도 맞춰야 한다.
    private enum Step { None, B1a, B1b, B2a, B2b, B3a, B3b, B4a, B4b, B4c, B5a, B5b, BattleFree, R1, R2a, R2b, R3a, R3b, R4a, R4b }
    // 확인 버튼으로 넘기는 설명 단계다.
    private static readonly HashSet<Step> CONFIRM_STEPS = new HashSet<Step> { Step.B4b, Step.B5a, Step.B5b, Step.R2a, Step.R3a, Step.R3b };
    // 안내 창이 자리를 정하기 전에 강조 영역(떨어지는 조각 등)이 자리 잡기를 기다리는 시간이다.
    private const float POPUP_SETTLE_SECONDS = 0.35f;

    private sealed class TextData
    {
        [JsonProperty("ui")] public Dictionary<string, string> Ui { get; set; } = new Dictionary<string, string>();
        [JsonProperty("steps")] public Dictionary<string, StepText> Steps { get; set; } = new Dictionary<string, StepText>();
    }

    private sealed class StepText
    {
        [JsonProperty("title")] public string Title { get; set; } = "";
        [JsonProperty("body")] public List<string> Body { get; set; } = new List<string>();
    }

    private SpellGame _game;
    private SpellTable3D _table;
    private SpellCombatScreen _combat;
    private SpellLoadoutScreen _loadout;
    private Func<bool> _isModalOpen;
    private Func<int> _pendingCast;
    private Action _onChanged;
    private Font _font;
    private TextData _text;

    private bool _active;
    private bool _emberCast, _barrierCast, _wasReward;
    private readonly HashSet<Step> _confirmed = new HashSet<Step>();
    private bool _popupPlaced, _hasPopupPosition;
    private float _stepStart, _stableSince;
    private Vector4 _holeSignature;
    private int _ember = -1, _barrier = -1;
    private Step _step = Step.None;
    private float _idleSince;
    private Vector2 _lastPointer;

    private RectTransform _root, _popup, _skipModal, _ghost;
    private Text _header, _title, _body, _ghostText;
    private Button _confirm;
    private CanvasGroup _ghostGroup, _popupGroup;
    private readonly List<Image> _dims = new List<Image>();
    private readonly List<Image> _frames = new List<Image>();
    private readonly List<Rect> _holes = new List<Rect>();
    private Vector2 _ghostFrom, _ghostTo;
    private bool _hasGhost;

    /// <summary>온보딩이 진행 중이고 지금 단계가 입력을 제한하는지다.</summary>
    private bool IsGuiding { get { return _active && _step != Step.None && _step != Step.BattleFree; } }

    /// <summary>온보딩 화면을 만들고 게임·전투 판·보상 화면을 연결한다. isModalOpen이 true면(일시정지·도움말 등) 안내를 잠시 숨긴다.</summary>
    public static SpellOnboarding Create(SpellGame game, SpellTable3D table, SpellCombatScreen combat, SpellLoadoutScreen loadout, Font font, Func<bool> isModalOpen, Func<int> pendingCast, Action onChanged)
    {
        GameObject obj = new GameObject("Onboarding", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
        CanvasScaler scaler = obj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        SpellOnboarding onboarding = obj.AddComponent<SpellOnboarding>();
        onboarding._game = game; onboarding._table = table; onboarding._combat = combat; onboarding._loadout = loadout; onboarding._font = font;
        onboarding._isModalOpen = isModalOpen; onboarding._pendingCast = pendingCast; onboarding._onChanged = onChanged;
        onboarding.Build();
        loadout.PressFilter = onboarding.AllowsPress;
        return onboarding;
    }

    /// <summary>저장된 완료 기록이 없으면 true다. 새 게임을 온보딩 판으로 시작할지 정하는 데 쓴다.</summary>
    public static bool ShouldRun { get { return !PlayerPrefs.HasKey(DONE_KEY); } }

    /// <summary>완료 기록을 지워 다음 새 게임에서 다시 온보딩을 진행하게 한다.</summary>
    public static void ResetProgress() { PlayerPrefs.DeleteKey(DONE_KEY); PlayerPrefs.Save(); }

    /// <summary>새 게임 시작 시 호출한다. 온보딩 판이면 처음 단계부터 진행하고, 아니면 끈다.</summary>
    public void Begin(bool active)
    {
        _active = active;
        _emberCast = _barrierCast = _wasReward = false;
        _confirmed.Clear(); _popupPlaced = _hasPopupPosition = false;
        _ember = _barrier = -1;
        for (int i = 0; i < SpellGame.PatternCount; i++)
        {
            if (SpellGame.PatternId(i) == "ember") _ember = i;
            else if (SpellGame.PatternId(i) == "barrier") _barrier = i;
        }
        if (_ember < 0 || _barrier < 0) _active = false;
        _step = Step.None; _idleSince = Time.unscaledTime;
        CloseSkip();
        Refresh();
    }

    /// <summary>주문 시전이 성공했을 때 호출한다. 불씨·방벽 시전 여부로 전투 단계를 넘긴다.</summary>
    public void NotifyCast(int spell)
    {
        if (!_active || _game.Battle != 1 || _game.Turn != 1) return;
        if (spell == _ember) _emberCast = true;
        else if (spell == _barrier && _emberCast) _barrierCast = true;
    }

    // ───────────── 입력 거르기 ─────────────

    /// <summary>마우스를 직접 읽는 화면에서 이 화면 좌표의 누르기를 받아도 되는지다. 안내 중에는 강조 영역 안만 허용한다.</summary>
    public bool AllowsPress(Vector2 screen)
    {
        if (!IsGuiding || !_root.gameObject.activeSelf) return true;
        // 확인 단계는 강조 영역을 보여 주기만 하고 조작은 확인 버튼으로만 받는다.
        if (IsConfirmStep) return false;
        Vector2 local = ToLocal(screen);
        foreach (Rect hole in _holes) if (hole.Contains(local)) return true;
        return false;
    }

    /// <summary>이 조각을 집어도 되는지다.</summary>
    public bool AllowsPick(SpellGame.Card card)
    {
        if (!IsBattleStep) return true;
        SpellGame.Shape? shape = AllowedShape();
        return shape.HasValue && card != null && card.Shape == shape.Value;
    }

    /// <summary>집은 조각을 이 도안에 놓아도 되는지다.</summary>
    public bool AllowsPlace(int spell)
    {
        if (!IsBattleStep) return true;
        if (_step == Step.B1a || _step == Step.B1b || _step == Step.B4a) return spell == _ember;
        if (_step == Step.B3a) return spell == _barrier;
        return false;
    }

    /// <summary>이 도안을 시전해도 되는지다.</summary>
    public bool AllowsCast(int spell)
    {
        if (!IsBattleStep) return true;
        return ((_step == Step.B2a || _step == Step.B2b) && spell == _ember) || (_step == Step.B3b && spell == _barrier);
    }

    /// <summary>턴을 종료해도 되는지다. 턴 종료를 안내하는 단계에서만 허용한다.</summary>
    public bool AllowsEndTurn { get { return !IsBattleStep || _step == Step.B4c; } }

    /// <summary>판 위 도안을 옮기거나 도안 메뉴를 열어도 되는지다.</summary>
    public bool AllowsPatternEdit { get { return !IsBattleStep; } }

    private bool IsBattleStep { get { return _active && _step >= Step.B1a && _step <= Step.B5b; } }

    /// <summary>지금 집을 수 있는 조각 모양이다. 불씨는 정사각형을 먼저 놓아야 반 칸 삼각형 자리가 남으므로 순서를 정한다.</summary>
    private SpellGame.Shape? AllowedShape()
    {
        switch (_step)
        {
            case Step.B1a: case Step.B3a: return SpellGame.Shape.Diamond;
            case Step.B1b: case Step.B4a: return SpellGame.Shape.QuarterTriangle;
            default: return null;
        }
    }

    // ───────────── 단계 판정 ─────────────

    /// <summary>게임 상태로 지금 단계를 정한다. 보상까지 끝나면 완료를 저장하고 끈다.</summary>
    private Step Evaluate()
    {
        if (!_active || _game.Battle != 1) { if (_active && _wasReward) Finish(); return Step.None; }
        if (_game.CurrentPhase == SpellGame.Phase.Combat)
        {
            if (_game.Turn == 1)
            {
                if (!_emberCast)
                {
                    if (_game.IsComplete(_ember)) return _pendingCast() == _ember ? Step.B2b : Step.B2a;
                    foreach (SpellGame.Placement piece in _game.Boards[_ember].Pieces) if (piece.Card.Shape == SpellGame.Shape.Diamond) return Step.B1b;
                    return Step.B1a;
                }
                if (!_barrierCast) return _game.IsComplete(_barrier) ? Step.B3b : Step.B3a;
                if (_game.Boards[_ember].Pieces.Count == 0) return Step.B4a;
                return _confirmed.Contains(Step.B4b) ? Step.B4c : Step.B4b;
            }
            if (_game.Boards[_ember].Pieces.Count == 0) return Step.BattleFree;
            if (!_confirmed.Contains(Step.B5a)) return Step.B5a;
            return _confirmed.Contains(Step.B5b) ? Step.BattleFree : Step.B5b;
        }
        if (_game.CurrentPhase == SpellGame.Phase.Reward)
        {
            _wasReward = true;
            if (_game.ActiveReward == SpellGame.RewardKind.Seal) return Step.None;
            if (_game.HasPatternReward && _loadout.Tab == SpellGame.RewardKind.Pattern)
                return !_loadout.HasTrialPattern ? Step.R1 : _confirmed.Contains(Step.R2a) ? Step.R2b : Step.R2a;
            if (_game.HasPieceReward && _loadout.Tab == SpellGame.RewardKind.Piece)
            {
                if (!_confirmed.Contains(Step.R3a)) return Step.R3a;
                if (!_confirmed.Contains(Step.R3b)) return Step.R3b;
                return _loadout.HasTrialPiece ? Step.R4b : Step.R4a;
            }
            return Step.None;
        }
        if (_wasReward) Finish();
        return Step.None;
    }

    /// <summary>온보딩을 끝내고 완료를 저장한다.</summary>
    private void Finish()
    {
        _active = false; _step = Step.None;
        PlayerPrefs.SetInt(DONE_KEY, 1); PlayerPrefs.Save();
        Refresh();
    }

    void LateUpdate()
    {
        if (_root == null) return;
        Step step = Evaluate();
        if (step != _step) { _step = step; _idleSince = _stepStart = Time.unscaledTime; _popupPlaced = false; Refresh(); }
        bool visible = IsGuiding && !_isModalOpen();
        if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        if (!visible) { _holes.Clear(); return; }
        TrackIdle();
        Layout();
    }

    // ───────────── 화면 ─────────────

    /// <summary>단계가 바뀌면 문구와 확인 버튼을 갱신한다.</summary>
    private void Refresh()
    {
        if (_root == null) return;
        if (!IsGuiding) { _root.gameObject.SetActive(false); return; }
        string key = _step.ToString();
        StepText text = _text.Steps.TryGetValue(key, out StepText found) ? found : new StepText { Title = key };
        _header.text = Ui(_step >= Step.R1 ? "rewardHeader" : "battleHeader");
        _title.text = text.Title;
        _body.text = Bold(string.Join("\n", text.Body));
        _confirm.gameObject.SetActive(IsConfirmStep);
        CloseSkip();
    }

    private bool IsConfirmStep { get { return CONFIRM_STEPS.Contains(_step); } }

    /// <summary>강조 영역을 다시 구하고 가림막·테두리·안내 창·손가락 위치를 맞춘다.</summary>
    private void Layout()
    {
        _holes.Clear(); _hasGhost = false;
        CollectHoles();
        Vector2 size = _root.rect.size;
        // 강조 영역 경계로 화면을 나눠 영역 밖 칸만 어둡게 덮는다.
        List<float> xs = new List<float> { 0, size.x }, ys = new List<float> { 0, size.y };
        foreach (Rect hole in _holes) { xs.Add(Mathf.Clamp(hole.xMin, 0, size.x)); xs.Add(Mathf.Clamp(hole.xMax, 0, size.x)); ys.Add(Mathf.Clamp(hole.yMin, 0, size.y)); ys.Add(Mathf.Clamp(hole.yMax, 0, size.y)); }
        xs.Sort(); ys.Sort();
        int used = 0;
        for (int y = 0; y < ys.Count - 1; y++) for (int x = 0; x < xs.Count - 1; x++)
        {
            float w = xs[x + 1] - xs[x], h = ys[y + 1] - ys[y];
            if (w <= 0.5f || h <= 0.5f) continue;
            Vector2 center = new Vector2(xs[x] + w / 2, ys[y] + h / 2);
            bool exposed = false; foreach (Rect hole in _holes) if (hole.Contains(center)) { exposed = true; break; }
            if (exposed) continue;
            Image dim = Pooled(_dims, used++, "Dim", DIM_COLOR, true);
            Place(dim.rectTransform, new Rect(xs[x], ys[y], w, h));
        }
        for (int i = used; i < _dims.Count; i++) _dims[i].gameObject.SetActive(false);
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
        int frames = 0;
        foreach (Rect hole in _holes)
        {
            Rect[] bars = { new Rect(hole.xMin - 3, hole.yMin - 3, hole.width + 6, 3), new Rect(hole.xMin - 3, hole.yMax, hole.width + 6, 3), new Rect(hole.xMin - 3, hole.yMin, 3, hole.height), new Rect(hole.xMax, hole.yMin, 3, hole.height) };
            foreach (Rect bar in bars)
            {
                Image frame = Pooled(_frames, frames++, "Frame", ACCENT_COLOR, false);
                frame.color = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, pulse);
                Place(frame.rectTransform, bar);
            }
        }
        for (int i = frames; i < _frames.Count; i++) _frames[i].gameObject.SetActive(false);
        LayoutPopup(size);
        _popupGroup.blocksRaycasts = _popupGroup.alpha > 0;
        UpdateGhost();
        _popup.SetAsLastSibling(); _ghost.SetAsLastSibling(); _skipModal.SetAsLastSibling();
    }

    /// <summary>지금 단계의 강조 영역과 손가락 안내의 시작·끝을 모은다.</summary>
    private void CollectHoles()
    {
        switch (_step)
        {
            case Step.B1a: case Step.B1b: case Step.B4a:
                Ghost(AddCard(AllowedShape().Value), AddPattern(_ember));
                break;
            case Step.B3a:
                Ghost(AddCard(SpellGame.Shape.Diamond), AddPattern(_barrier));
                break;
            case Step.B2a:
            {
                Rect cast = AddRect(_combat.CastButtons[_ember].Rect);
                Ghost(cast, AddEnemies());
                break;
            }
            case Step.B2b:
            {
                Rect enemy = AddEnemies();
                Ghost(enemy, enemy);
                break;
            }
            case Step.B3b:
            {
                Rect cast = AddRect(_combat.CastButtons[_barrier].Rect);
                Ghost(cast, cast);
                break;
            }
            case Step.B4b:
                foreach (SpellGame.Placement piece in _game.Boards[_ember].Pieces)
                    if (_table.TryPieceScreenRect(piece.Card, out Rect placed)) AddScreen(placed);
                break;
            case Step.B4c:
            {
                Rect end = AddRect(_combat.EndTurn.transform as RectTransform);
                Ghost(end, end);
                break;
            }
            case Step.B5a:
                foreach (SpellGame.Placement piece in _game.Boards[_ember].Pieces)
                    if (piece.Turn < _game.Turn && _table.TryPieceScreenRect(piece.Card, out Rect locked)) AddScreen(locked);
                break;
            case Step.B5b:
                AddRect(_combat.Tabs[_ember].Rect);
                break;
            case Step.R1: case Step.R4a:
            {
                Rect first = AddRewardCards(3);
                Ghost(first, AddRect(_loadout.GridFrame));
                break;
            }
            case Step.R2a:
                AddRect(_loadout.GridFrame); AddRect(_loadout.StorageFrame);
                break;
            case Step.R2b: case Step.R4b:
            {
                AddRect(_loadout.GridFrame);
                if (_step == Step.R2b) AddRect(_loadout.StorageFrame);
                Rect decide = AddRect(_loadout.DecideButton);
                Ghost(decide, decide);
                break;
            }
            case Step.R3a:
                if (_loadout.TryCardRect(0, out RectTransform seal)) AddRect(seal);
                break;
            case Step.R3b:
                if (_loadout.TryCardRect(1, out RectTransform coating)) AddRect(coating);
                break;
        }
    }

    /// <summary>살아 있는 적을 모두 강조 영역에 넣고 첫 적의 범위를 반환한다.</summary>
    private Rect AddEnemies()
    {
        Rect first = default; bool found = false;
        for (int i = 0; i < _game.Enemies.Count && i < _combat.Enemies.Length; i++)
        {
            if (_game.Enemies[i].Health <= 0) continue;
            Rect rect = AddRect(_combat.Enemies[i].transform as RectTransform);
            if (!found) { first = rect; found = true; }
        }
        return first;
    }

    /// <summary>보상 카드 앞 count장을 강조 영역에 넣고 첫 카드의 범위를 반환한다.</summary>
    private Rect AddRewardCards(int count)
    {
        Rect first = default; bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (!_loadout.TryCardRect(i, out RectTransform card)) continue;
            Rect rect = AddRect(card);
            if (!found) { first = rect; found = true; }
        }
        return first;
    }

    /// <summary>손패에서 지정 모양의 첫 조각을 강조 영역에 넣는다.</summary>
    private Rect AddCard(SpellGame.Shape shape)
    {
        foreach (SpellGame.Card card in _game.Hand)
            if (card.Shape == shape && _table.TryPieceScreenRect(card, out Rect screen)) return AddScreen(screen);
        return default;
    }

    /// <summary>판 위 도안 범위를 강조 영역에 넣는다.</summary>
    private Rect AddPattern(int spell)
    {
        return _table.TryPatternScreenRect(spell, out Rect screen) ? AddScreen(screen) : default;
    }

    /// <summary>화면 좌표 사각형을 안내 화면 좌표로 바꿔 여백을 붙여 강조 영역에 넣는다.</summary>
    private Rect AddScreen(Rect screen)
    {
        Vector2 min = ToLocal(screen.min), max = ToLocal(screen.max);
        Rect rect = Rect.MinMaxRect(min.x - HOLE_PAD, min.y - HOLE_PAD, max.x + HOLE_PAD, max.y + HOLE_PAD);
        _holes.Add(rect);
        return rect;
    }

    /// <summary>UI 요소의 화면 범위를 강조 영역에 넣는다.</summary>
    private Rect AddRect(RectTransform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return default;
        Vector3[] corners = new Vector3[4]; target.GetWorldCorners(corners);
        Canvas canvas = target.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return AddScreen(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
    }

    private void Ghost(Rect from, Rect to)
    {
        if (from.width <= 0 || to.width <= 0) return;
        _hasGhost = true; _ghostFrom = from.center; _ghostTo = to.center;
    }

    /// <summary>안내 창을 첫 강조 영역 가까이, 다른 강조 영역과 겹치지 않는 곳에 둔다.</summary>
    private void LayoutPopup(Vector2 size)
    {
        float inner = POPUP_WIDTH - POPUP_PAD * 2;
        _title.rectTransform.sizeDelta = new Vector2(inner - 120, 40);
        _body.rectTransform.sizeDelta = new Vector2(inner, 10);
        float bodyHeight = Mathf.Ceil(_body.preferredHeight);
        _body.rectTransform.sizeDelta = new Vector2(inner, bodyHeight);
        float height = POPUP_PAD + 24 + 8 + 40 + 12 + bodyHeight + (IsConfirmStep ? 20 + 52 : 0) + POPUP_PAD;
        _popup.sizeDelta = new Vector2(POPUP_WIDTH, height);
        // 한 단계 안에서는 안내 창을 움직이지 않는다. 보인 뒤에 자리를 옮기지 않도록, 자리를 정하기 전에는 창을 숨긴다.
        if (_popupPlaced) { _popupGroup.alpha = 1; return; }
        // 강조 영역이 움직이는 동안(조각 낙하, 화면 배치 변경)은 자리를 확정하지 않는다.
        Vector4 signature = Vector4.zero;
        foreach (Rect hole in _holes) signature += new Vector4(hole.xMin, hole.yMin, hole.xMax, hole.yMax);
        if ((signature - _holeSignature).sqrMagnitude > 4f) { _holeSignature = signature; _stableSince = Time.unscaledTime; }
        bool settled = Time.unscaledTime - Mathf.Max(_stepStart, _stableSince) >= POPUP_SETTLE_SECONDS;
        // 각 강조 영역과 전체 강조 범위의 위·아래·좌·우를 후보로 두고, 강조 영역을 가장 적게 가리면서 가까운 곳을 고른다.
        List<Rect> anchors = new List<Rect>(_holes);
        if (_holes.Count > 1)
        {
            Rect union = _holes[0];
            foreach (Rect hole in _holes) union = Rect.MinMaxRect(Mathf.Min(union.xMin, hole.xMin), Mathf.Min(union.yMin, hole.yMin), Mathf.Max(union.xMax, hole.xMax), Mathf.Max(union.yMax, hole.yMax));
            anchors.Insert(0, union);
        }
        if (anchors.Count == 0) anchors.Add(new Rect(size.x / 2, size.y / 2, 0, 0));
        Vector2 focus = anchors[0].center;
        List<Vector2> candidates = new List<Vector2>();
        foreach (Rect anchor in anchors)
        {
            candidates.Add(new Vector2(anchor.center.x - POPUP_WIDTH / 2, anchor.yMin - POPUP_GAP - height));
            candidates.Add(new Vector2(anchor.center.x - POPUP_WIDTH / 2, anchor.yMax + POPUP_GAP));
            candidates.Add(new Vector2(anchor.xMax + POPUP_GAP, anchor.center.y - height / 2));
            candidates.Add(new Vector2(anchor.xMin - POPUP_GAP - POPUP_WIDTH, anchor.center.y - height / 2));
        }
        // 이전 단계의 자리가 새 강조 영역을 가리지 않으면 그대로 두어 창이 덜 움직이게 한다.
        if (_hasPopupPosition) candidates.Insert(0, _popup.anchoredPosition);
        Vector2 best = candidates[0]; float bestScore = float.MaxValue;
        bool keepPrevious = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2 candidate = candidates[i];
            Vector2 position = new Vector2(Mathf.Clamp(candidate.x, 16, size.x - POPUP_WIDTH - 16), Mathf.Clamp(candidate.y, 16, size.y - height - 16));
            Rect popup = new Rect(position, new Vector2(POPUP_WIDTH, height));
            bool overlaps = false;
            float score = Vector2.Distance(popup.center, focus);
            foreach (Rect hole in _holes)
            {
                float w = Mathf.Min(popup.xMax, hole.xMax) - Mathf.Max(popup.xMin, hole.xMin), h = Mathf.Min(popup.yMax, hole.yMax) - Mathf.Max(popup.yMin, hole.yMin);
                if (w > 0 && h > 0) { score += 100000 + w * h; overlaps = true; }
            }
            if (i == 0 && _hasPopupPosition && !overlaps) { best = position; keepPrevious = true; break; }
            if (score < bestScore) { bestScore = score; best = position; }
        }
        // 이전 자리를 그대로 쓸 수 있으면 바로 확정해 창이 깜빡이지 않게 한다. 옮겨야 하면 강조 영역이 멈출 때까지 숨겼다가 새 자리에 띄운다.
        if (!keepPrevious && !settled) { _popupGroup.alpha = 0; return; }
        _popup.anchoredPosition = best;
        _hasPopupPosition = true; _popupPlaced = true;
        _popupGroup.alpha = 1;
    }

    /// <summary>마우스·키 입력이 멈춘 시간을 잰다.</summary>
    private void TrackIdle()
    {
        Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        bool input = (pointer - _lastPointer).sqrMagnitude > 1f
            || (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed))
            || (Keyboard.current != null && Keyboard.current.anyKey.isPressed);
        _lastPointer = pointer;
        if (input) _idleSince = Time.unscaledTime;
    }

    /// <summary>입력이 3초 이상 없으면 손가락이 시작점에서 끝점으로 끌기를 반복하고, 시작과 끝이 같으면 누르는 동작을 반복한다.</summary>
    private void UpdateGhost()
    {
        float idle = Time.unscaledTime - _idleSince;
        bool show = _hasGhost && idle >= IDLE_SECONDS;
        _ghostGroup.alpha = show ? 0.9f : 0f;
        if (!show) return;
        float t = Mathf.Repeat((idle - IDLE_SECONDS) / GHOST_SECONDS, 1f);
        bool press = (_ghostTo - _ghostFrom).sqrMagnitude < 4f;
        Vector2 point = press ? _ghostFrom : Vector2.Lerp(_ghostFrom, _ghostTo, Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 0.15f) / 0.65f)));
        _ghost.anchoredPosition = point;
        _ghost.localScale = Vector3.one * (press ? 1f - 0.2f * Mathf.Sin(t * Mathf.PI) : (t < 0.15f || t > 0.8f ? 0.9f : 1f));
        if (!press) _ghostGroup.alpha = 0.9f * Mathf.Clamp01(Mathf.Min(t / 0.1f, (1f - t) / 0.1f));
    }

    // ───────────── 건너뛰기 ─────────────

    private void OpenSkip() { _skipModal.gameObject.SetActive(true); _skipModal.SetAsLastSibling(); }

    private void CloseSkip() { if (_skipModal != null) _skipModal.gameObject.SetActive(false); }

    private void ConfirmSkip()
    {
        CloseSkip();
        _active = false; _step = Step.None;
        PlayerPrefs.SetInt(DONE_KEY, 1); PlayerPrefs.Save();
        Refresh(); _onChanged?.Invoke();
    }

    /// <summary>확인 단계의 확인 버튼이다.</summary>
    private void Confirm()
    {
        if (IsConfirmStep) _confirmed.Add(_step);
    }

    // ───────────── 만들기 ─────────────

    private void Build()
    {
        TextAsset asset = Resources.Load<TextAsset>(TEXT_PATH);
        try { _text = asset != null ? JsonConvert.DeserializeObject<TextData>(asset.text) : null; }
        catch (Exception exception) { Debug.LogError($"온보딩 문구를 읽지 못했습니다: {exception.Message}"); }
        if (_text == null) _text = new TextData();

        _root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
        _root.SetParent(transform, false);
        _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one; _root.pivot = Vector2.zero; _root.offsetMin = _root.offsetMax = Vector2.zero;

        _popup = Box(_root, "Popup", PANEL_COLOR);
        _popup.anchorMin = _popup.anchorMax = _popup.pivot = Vector2.zero;
        _popupGroup = _popup.gameObject.AddComponent<CanvasGroup>(); _popupGroup.alpha = 0;
        _header = MakeText(_popup, "Header", 18, ACCENT_COLOR, TextAnchor.UpperLeft);
        TopLeft(_header.rectTransform, POPUP_PAD, POPUP_PAD, 300, 24);
        _title = MakeText(_popup, "Title", 30, TEXT_COLOR, TextAnchor.UpperLeft); _title.fontStyle = FontStyle.Bold;
        TopLeft(_title.rectTransform, POPUP_PAD, POPUP_PAD + 32, POPUP_WIDTH - POPUP_PAD * 2 - 120, 40);
        _body = MakeText(_popup, "Body", 23, TEXT_COLOR, TextAnchor.UpperLeft);
        _body.lineSpacing = 1.15f; _body.horizontalOverflow = HorizontalWrapMode.Wrap; _body.verticalOverflow = VerticalWrapMode.Overflow;
        TopLeft(_body.rectTransform, POPUP_PAD, POPUP_PAD + 84, POPUP_WIDTH - POPUP_PAD * 2, 10);
        Button skip = MakeButton(_popup, "Skip", Ui("skip"), new Color(0, 0, 0, 0), 18, OpenSkip);
        skip.GetComponentInChildren<Text>().color = SUB_COLOR;
        RectTransform skipRect = (RectTransform)skip.transform;
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = new Vector2(1, 1);
        skipRect.anchoredPosition = new Vector2(-12, -12); skipRect.sizeDelta = new Vector2(110, 36);
        _confirm = MakeButton(_popup, "Confirm", Ui("confirm"), new Color(0.2f, 0.17f, 0.08f, 1f), 22, Confirm);
        _confirm.GetComponentInChildren<Text>().color = ACCENT_COLOR;
        RectTransform confirmRect = (RectTransform)_confirm.transform;
        confirmRect.anchorMin = confirmRect.anchorMax = confirmRect.pivot = new Vector2(1, 0);
        confirmRect.anchoredPosition = new Vector2(-POPUP_PAD, POPUP_PAD); confirmRect.sizeDelta = new Vector2(150, 52);

        _ghost = new GameObject("Ghost", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        _ghost.SetParent(_root, false);
        _ghost.anchorMin = _ghost.anchorMax = Vector2.zero; _ghost.pivot = new Vector2(0.35f, 0.9f); _ghost.sizeDelta = new Vector2(70, 70);
        _ghostGroup = _ghost.GetComponent<CanvasGroup>(); _ghostGroup.alpha = 0; _ghostGroup.blocksRaycasts = false; _ghostGroup.interactable = false;
        _ghostText = MakeText(_ghost, "Hand", 58, Color.white, TextAnchor.MiddleCenter); _ghostText.text = "☝";
        // 손가락 기호는 한글 글꼴에 없을 수 있어 기호 글꼴을 먼저 쓴다.
        _ghostText.font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Segoe UI Emoji", "Malgun Gothic" }, 64);
        _ghostText.raycastTarget = false;
        Outline outline = _ghostText.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0, 0, 0, 0.8f); outline.effectDistance = new Vector2(2, -2);
        Stretch(_ghostText.rectTransform);

        BuildSkipModal();
        _root.gameObject.SetActive(false);
    }

    private void BuildSkipModal()
    {
        _skipModal = new GameObject("Skip Modal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        _skipModal.SetParent(_root, false); Stretch(_skipModal);
        _skipModal.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
        RectTransform box = Box(_skipModal, "Box", PANEL_COLOR);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(640, 300); box.anchoredPosition = Vector2.zero;
        Text title = MakeText(box, "Title", 30, TEXT_COLOR, TextAnchor.UpperCenter); title.text = Ui("skipTitle"); title.fontStyle = FontStyle.Bold;
        TopLeft(title.rectTransform, 30, 30, 580, 44);
        Text body = MakeText(box, "Body", 21, SUB_COLOR, TextAnchor.UpperCenter); body.text = Ui("skipBody");
        TopLeft(body.rectTransform, 30, 88, 580, 100);
        Button yes = MakeButton(box, "Yes", Ui("skipYes"), new Color(0.35f, 0.12f, 0.12f, 1f), 22, ConfirmSkip);
        Button no = MakeButton(box, "No", Ui("skipNo"), new Color(0.16f, 0.18f, 0.24f, 1f), 22, CloseSkip);
        TopLeft((RectTransform)yes.transform, 120, 212, 180, 56);
        TopLeft((RectTransform)no.transform, 340, 212, 180, 56);
        _skipModal.gameObject.SetActive(false);
    }

    /// <summary>문구 데이터의 화면 문구를 반환한다. 없으면 키를 그대로 쓴다.</summary>
    public string Ui(string key) { return _text != null && _text.Ui.TryGetValue(key, out string value) ? value : key; }

    /// <summary>**굵게** 표기를 Unity 서식 태그로 바꾼다.</summary>
    private static string Bold(string text) { return Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>"); }

    private Vector2 ToLocal(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
        return local - _root.rect.min;
    }

    private Image Pooled(List<Image> pool, int index, string name, Color color, bool raycast)
    {
        while (pool.Count <= index)
        {
            Image image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(_root, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = Vector2.zero;
            image.color = color; image.raycastTarget = raycast;
            pool.Add(image);
        }
        Image result = pool[index];
        result.gameObject.SetActive(true);
        return result;
    }

    private static void Place(RectTransform rect, Rect area) { rect.anchoredPosition = area.position; rect.sizeDelta = area.size; }

    private static RectTransform Box(Transform parent, string name, Color color)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.GetComponent<Image>().color = color;
        Outline outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.6f); outline.effectDistance = new Vector2(2, -2);
        return rect;
    }

    private Text MakeText(Transform parent, string name, int size, Color color, TextAnchor anchor)
    {
        Text text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.font = _font; text.fontSize = size; text.color = color; text.alignment = anchor; text.supportRichText = true; text.raycastTarget = false;
        // 글꼴의 줄 높이가 칸보다 커도 잘리지 않게 넘쳐 그린다.
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private Button MakeButton(Transform parent, string name, string label, Color color, int size, Action onClick)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.GetComponent<Image>().color = color;
        Button button = rect.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());
        Text text = MakeText(rect, "Label", size, TEXT_COLOR, TextAnchor.MiddleCenter); text.text = label;
        Stretch(text.rectTransform);
        return button;
    }

    private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
