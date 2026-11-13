using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>전투 사이 도안 장착 화면. 보유 도안을 격자에 놓고 돌리며, 격자에 놓인 도안만 다음 전투에 장착한다.</summary>
public sealed class SpellLoadoutScreen : MonoBehaviour
{
    private const float STORAGE_SCALE = 0.62f;
    private const float STORAGE_GAP = 12f;
    private const string DEFAULT_HINT = "도안을 격자로 드래그하거나 클릭 후 칸 클릭으로 놓기 · R / 우클릭 회전 · 방향키로 격자 이동 · 보관함으로 드래그·클릭해 장착 해제";
    // 누른 뒤 이 픽셀 이상 움직이면 클릭이 아니라 드래그로 본다.
    private const float DRAG_THRESHOLD = 8f;

    [Header("구성")]
    [SerializeField] private SpellLoadoutGrid _grid;
    [SerializeField] private RectTransform _storage;
    [SerializeField] private Button _storageButton;
    [SerializeField] private SpellRewardCardView _storageTemplate;
    [SerializeField] private Text _storageEmpty;
    [SerializeField] private SpellButtonView _start;
    [SerializeField] private Text _hint;

    private readonly List<SpellRewardCardView> _cards = new List<SpellRewardCardView>();
    private readonly List<int> _cardSpells = new List<int>();
    private readonly List<RaycastResult> _raycasts = new List<RaycastResult>();
    private SpellPatternLayout _layout;
    private IReadOnlyList<int> _owned;
    private Func<int, Color> _colorOf;
    private int _held = -1;
    private int _heldRotation;
    // 드래그 상태: 누른 도안(_pressSpell), 누른 위치, 격자에서 집었는지와 원래 위치·방향, 드래그 중 여부, 마지막 드래그 종료 프레임이다.
    private int _pressSpell = -1;
    private Vector2 _pressPosition;
    private bool _pressFromGrid;
    private Vector2Int _dragOrigin;
    private int _dragOriginRotation;
    private bool _isDragging;
    private int _dragEndFrame = -10;

    /// <summary>도안 배치, 보유 목록, 도안 색과 전투 시작 동작을 연결한다.</summary>
    public void Setup(SpellPatternLayout layout, IReadOnlyList<int> owned, Func<int, Color> colorOf, Action onStart)
    {
        _layout = layout; _owned = owned; _colorOf = colorOf;
        _grid.Setup(layout, owned, colorOf, ClickCell);
        _start.SetAction(() => { DropHeld(); onStart(); });
        _storageButton.onClick.AddListener(DropHeld);
    }

    void OnDisable()
    {
        CancelDrag();
        _pressSpell = -1;
        DropHeld();
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            Vector2 direction = new Vector2((Keyboard.current.rightArrowKey.isPressed ? 1 : 0) - (Keyboard.current.leftArrowKey.isPressed ? 1 : 0),
                (Keyboard.current.upArrowKey.isPressed ? 1 : 0) - (Keyboard.current.downArrowKey.isPressed ? 1 : 0));
            if (direction.sqrMagnitude > 0) _grid.Pan(direction.normalized * (300f * Time.unscaledDeltaTime));
        }
        UpdateDrag();
        if (_held < 0) return;
        bool rotate = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
        if (rotate) { _heldRotation = (_heldRotation + 1) % 4; _grid.SetHeld(_held, _heldRotation); }
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { CancelDrag(); DropHeld(); }
    }

    /// <summary>보관함 카드나 격자 도안을 누른 채 움직이면 드래그로 들고, 놓은 곳에 따라 격자 배치·장착 해제·원위치를 처리한다.</summary>
    private void UpdateDrag()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 pointer = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame && !_isDragging && _held < 0) BeginPress(pointer);
        if (_pressSpell >= 0 && !_isDragging && mouse.leftButton.isPressed && (pointer - _pressPosition).sqrMagnitude >= DRAG_THRESHOLD * DRAG_THRESHOLD) StartDrag();
        if (_isDragging) _grid.HoverAt(pointer);
        if (!mouse.leftButton.wasReleasedThisFrame) return;
        if (_isDragging) FinishDrag(pointer);
        _pressSpell = -1;
    }

    /// <summary>누른 위치의 보관함 카드 또는 격자 도안을 드래그 후보로 기억한다.</summary>
    private void BeginPress(Vector2 pointer)
    {
        _pressSpell = -1; _pressPosition = pointer;
        if (_grid.TryScreenPoint(pointer, out Vector2Int cell, out int sector) && _layout.TryFindOwner(cell.x, cell.y, sector, _owned, out int spell))
        {
            _pressSpell = spell; _pressFromGrid = true; return;
        }
        if (EventSystem.current == null) return;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = pointer };
        _raycasts.Clear(); EventSystem.current.RaycastAll(data, _raycasts);
        foreach (RaycastResult result in _raycasts)
        {
            SpellRewardCardView card = result.gameObject.GetComponentInParent<SpellRewardCardView>();
            int index = card == null ? -1 : _cards.IndexOf(card);
            if (index < 0 || index >= _cardSpells.Count || !card.gameObject.activeSelf) continue;
            _pressSpell = _cardSpells[index]; _pressFromGrid = false; return;
        }
    }

    /// <summary>드래그 후보 도안을 든다. 격자에서 집은 도안은 원래 위치·방향을 기억하고 격자에서 뺀다.</summary>
    private void StartDrag()
    {
        _isDragging = true; _held = _pressSpell; _heldRotation = _layout.Rotation(_held);
        if (_pressFromGrid) { _dragOrigin = _layout.Origin(_held); _dragOriginRotation = _heldRotation; _layout.Unplace(_held); }
        Refresh(); ShowHint($"{SpellGame.PatternName(_held)}을(를) 격자에 놓으세요 · R / 우클릭 회전 · 보관함에 놓으면 장착 해제");
    }

    /// <summary>놓은 위치가 격자면 배치하고, 보관함이면 장착 해제하며, 그 밖이나 놓을 수 없는 칸이면 원래 자리로 되돌린다.</summary>
    private void FinishDrag(Vector2 pointer)
    {
        _isDragging = false; _dragEndFrame = Time.frameCount;
        int spell = _held; _held = -1;
        string issue = null;
        if (_grid.TryScreenPoint(pointer, out Vector2Int cell, out _))
        {
            // 격자는 아직 드래그한 도안·방향을 들고 있으므로 같은 기준으로 원점을 구한다.
            Vector2Int origin = _grid.HeldOrigin(cell);
            issue = _layout.MoveIssue(spell, origin, _heldRotation, _owned);
            if (issue == null) { _layout.Place(spell, origin, _heldRotation); Refresh(); return; }
        }
        else if (RectTransformUtility.RectangleContainsScreenPoint(_storage, pointer, EventCamera()) || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_storageButton.transform, pointer, EventCamera()))
        {
            Refresh(); return;
        }
        RestoreDragged(spell);
        Refresh();
        if (issue != null) ShowHint(issue);
    }

    /// <summary>드래그를 취소하고 격자에서 집은 도안은 원래 자리로 되돌린다.</summary>
    private void CancelDrag()
    {
        if (!_isDragging) return;
        _isDragging = false; _dragEndFrame = Time.frameCount;
        int spell = _held; _held = -1;
        RestoreDragged(spell);
        if (isActiveAndEnabled) Refresh();
    }

    /// <summary>격자에서 집은 도안이면 원래 위치·방향으로 다시 놓는다. 보관함에서 집은 도안은 보관함에 그대로 둔다.</summary>
    private void RestoreDragged(int spell)
    {
        if (spell >= 0 && _pressFromGrid) _layout.Place(spell, _dragOrigin, _dragOriginRotation);
    }

    /// <summary>드래그가 막 끝나 같은 입력의 클릭을 무시해야 하는지 반환한다.</summary>
    private bool IsDragClick() { return _isDragging || Time.frameCount - _dragEndFrame <= 1; }

    /// <summary>캔버스 렌더 방식에 맞춰 화면 좌표 변환에 쓸 카메라를 반환한다. Overlay면 null이다.</summary>
    private Camera EventCamera()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    /// <summary>보관함 카드와 격자를 현재 배치로 다시 그리고 안내를 기본 문구로 되돌린다.</summary>
    public void SetContinueLabel(string label) { _start.SetLabel(label); }

    public void Refresh()
    {
        List<int> stored = new List<int>();
        foreach (int spell in _owned) if (!_layout.IsPlaced(spell) && spell != _held) stored.Add(spell);
        while (_cards.Count < stored.Count) _cards.Add(Instantiate(_storageTemplate, _storage));
        _cardSpells.Clear(); _cardSpells.AddRange(stored);
        float height = ((RectTransform)_storageTemplate.transform).sizeDelta.y * STORAGE_SCALE;
        for (int i = 0; i < _cards.Count; i++)
        {
            bool show = i < stored.Count;
            _cards[i].gameObject.SetActive(show);
            if (!show) continue;
            int spell = stored[i];
            RectTransform rect = (RectTransform)_cards[i].transform;
            rect.localScale = Vector3.one * STORAGE_SCALE;
            rect.anchoredPosition = new Vector2(0, -height / 2 - i * (height + STORAGE_GAP));
            _cards[i].BindPattern(spell, SpellGame.PatternTitle(spell), spell, _colorOf(spell), PickStored, "보유 도안");
        }
        _storageEmpty.gameObject.SetActive(stored.Count == 0);
        _grid.SetHeld(_held, _heldRotation);
        ShowHint(DEFAULT_HINT);
    }

    /// <summary>장착 화면 아래 안내 문구를 바꾼다.</summary>
    public void ShowHint(string text) { _hint.text = text; }

    /// <summary>보관함의 도안을 현재 방향으로 든다. 이미 든 도안은 보관함으로 돌려놓는다.</summary>
    private void PickStored(int spell)
    {
        if (IsDragClick()) return;
        DropHeld();
        _held = spell; _heldRotation = _layout.Rotation(spell);
        Refresh(); ShowHint($"{SpellGame.PatternName(spell)}을(를) 놓을 칸을 클릭하세요 · R / 우클릭 회전");
    }

    /// <summary>든 도안이 있으면 그 칸에 놓고, 없으면 그 칸의 도안을 격자에서 들어 올린다.</summary>
    private void ClickCell(Vector2Int cell)
    {
        if (IsDragClick()) return;
        if (_held >= 0)
        {
            Vector2Int origin = _grid.HeldOrigin(cell);
            string issue = _layout.MoveIssue(_held, origin, _heldRotation, _owned);
            if (issue != null) { ShowHint(issue); return; }
            _layout.Place(_held, origin, _heldRotation); _held = -1; Refresh(); return;
        }
        for (int sector = 0; sector < 4; sector++)
        {
            if (!_layout.TryFindOwner(cell.x, cell.y, sector, _owned, out int spell)) continue;
            _held = spell; _heldRotation = _layout.Rotation(spell); _layout.Unplace(spell);
            Refresh(); ShowHint($"{SpellGame.PatternName(spell)}을(를) 놓을 칸을 클릭하세요 · 보관함을 클릭하면 장착 해제"); return;
        }
    }

    /// <summary>들고 있는 도안을 보관함에 내려놓는다.</summary>
    private void DropHeld()
    {
        if (_held < 0) return;
        _held = -1;
        if (isActiveAndEnabled) Refresh();
    }
}
