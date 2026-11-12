using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>C안 전투 화면의 하위 뷰 참조를 보관한다. 게임 판단과 값 갱신은 SpellGameUI가 맡는다.</summary>
public sealed class SpellCombatScreen : MonoBehaviour
{
    [Header("상단 바")]
    [SerializeField] private Text _battle;
    [SerializeField] private Image[] _progressDots;
    [SerializeField] private SpellButtonView _help;
    [SerializeField] private SpellButtonView _menu;

    [Header("작업대")]
    [SerializeField] private SpellPlayerStatusView _playerStatus;
    [SerializeField] private SpellPlacementView _placement;
    [SerializeField] private SpellBoardGraphic _board;
    [SerializeField] private SpellPatternTabView[] _tabs;
    [SerializeField] private SpellCastButtonView[] _castButtons;
    [SerializeField] private SpellPreviewBarView _preview;

    [Header("전장")]
    [SerializeField] private SpellEnemyView[] _enemies;

    [Header("손패 줄")]
    [SerializeField] private SpellPileView _drawPile;
    [SerializeField] private SpellPileView _discardPile;
    [SerializeField] private SpellHandCardView[] _hand;
    [SerializeField] private RectTransform _handDropArea;
    [SerializeField] private Text _boardCount;
    [SerializeField] private SpellButtonView _endTurn;

    [Header("겹쳐 뜨는 요소")]
    [SerializeField] private SpellMoreMenuView _moreMenu;
    [SerializeField] private SpellToastView _toast;
    [SerializeField] private SpellTooltipView _tooltip;
    [SerializeField] private RectTransform _dragLayer;
    [SerializeField] private RectTransform _pieceGhost;
    [SerializeField] private SpellShapeGraphic _pieceGhostShape;
    [SerializeField] private RectTransform _aimLine;
    [SerializeField] private RectTransform _cursorHint;
    [SerializeField] private Text _cursorHintText;

    public Text Battle { get { return _battle; } }
    public Image[] ProgressDots { get { return _progressDots; } }
    public SpellButtonView Help { get { return _help; } }
    public SpellButtonView Menu { get { return _menu; } }
    public SpellPlayerStatusView PlayerStatus { get { return _playerStatus; } }
    public SpellPlacementView Placement { get { return _placement; } }
    public SpellBoardGraphic Board { get { return _board; } }
    public SpellPatternTabView[] Tabs { get { return _tabs; } }
    public SpellCastButtonView[] CastButtons { get { return _castButtons; } }
    public SpellPreviewBarView Preview { get { return _preview; } }
    public SpellEnemyView[] Enemies { get { return _enemies; } }
    public SpellPileView DrawPile { get { return _drawPile; } }
    public SpellPileView DiscardPile { get { return _discardPile; } }
    public SpellHandCardView[] Hand { get { return _hand; } }
    public RectTransform HandDropArea { get { return _handDropArea; } }
    public Text BoardCount { get { return _boardCount; } }
    public SpellButtonView EndTurn { get { return _endTurn; } }
    public SpellMoreMenuView MoreMenu { get { return _moreMenu; } }
    public SpellToastView Toast { get { return _toast; } }
    public SpellTooltipView Tooltip { get { return _tooltip; } }
    public RectTransform DragLayer { get { return _dragLayer; } }
    public RectTransform PieceGhost { get { return _pieceGhost; } }
    public SpellShapeGraphic PieceGhostShape { get { return _pieceGhostShape; } }
    public RectTransform AimLine { get { return _aimLine; } }
    public RectTransform CursorHint { get { return _cursorHint; } }
    public Text CursorHintText { get { return _cursorHintText; } }

    /// <summary>도안 탭과 시전 버튼이 도안 수보다 적으면 마지막 요소를 같은 부모에 복제해 count개로 늘린다. Prefab 자산은 바꾸지 않는다.</summary>
    public void EnsurePatternCount(int count)
    {
        _tabs = Grow(_tabs, count);
        _castButtons = Grow(_castButtons, count);
    }

    /// <summary>뷰 배열이 count보다 짧으면 마지막 뷰를 복제해 덧붙인 새 배열을 반환한다. 충분하면 원래 배열을 반환한다.</summary>
    private static T[] Grow<T>(T[] views, int count) where T : Component
    {
        if (views.Length >= count) return views;
        T[] result = new T[count];
        Array.Copy(views, result, views.Length);
        T template = views[views.Length - 1];
        for (int i = views.Length; i < count; i++)
        {
            result[i] = Instantiate(template, template.transform.parent);
            result[i].name = $"{template.name} ({i})";
        }
        return result;
    }
}
