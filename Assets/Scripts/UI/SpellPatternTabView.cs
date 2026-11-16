using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>도안 위에 붙는 이름 탭(이동 핸들·이름·더보기 버튼)을 표시한다.</summary>
public sealed class SpellPatternTabView : MonoBehaviour
{
    [Header("구성")]
    [SerializeField] private RectTransform _rect;
    [SerializeField] private Image _ring;
    [SerializeField] private Text _name;
    [SerializeField] private SpellPatternDrag _drag;
    [SerializeField] private SpellButtonView _more;

    public RectTransform Rect { get { return _rect; } }

    /// <summary>이동할 도안과 더보기 메뉴 동작을 설정한다.</summary>
    public void Setup(SpellGameUI ui, int spell, Action onMore)
    {
        _drag.Setup(ui, spell);
        _more.SetLabel("×");
        _more.SetAction(onMore);
    }

    /// <summary>도안 이름·색과 위치·너비를 갱신한다. position은 부모 기준 왼쪽 위 좌표다.</summary>
    public void Bind(string displayName, Color color, Vector2 position, float width)
    {
        _name.text = displayName; _name.color = color; _ring.color = color;
        _rect.anchoredPosition = position;
        _rect.sizeDelta = new Vector2(width, _rect.sizeDelta.y);
    }
}
