using UnityEngine;
using UnityEngine.UI;

/// <summary>포인터 근처에 짧은 설명을 띄운다. 입력을 가로채지 않고 화면 밖으로 나가지 않게 위치를 조정한다.</summary>
public sealed class SpellTooltipView : MonoBehaviour
{
    private const float PADDING = 10f;
    private const float POINTER_GAP = 18f;

    [Header("구성")]
    [SerializeField] private RectTransform _rect;
    [SerializeField] private RectTransform _area;
    [SerializeField] private Text _label;

    public RectTransform Box { get { return _rect; } }

    /// <summary>설명을 화면 좌표 근처에 표시한다. text가 비어 있으면 숨긴다.</summary>
    public void Show(string text, Vector2 screenPosition)
    {
        if (string.IsNullOrEmpty(text)) { Hide(); return; }
        _label.text = text;
        float height = _label.preferredHeight + PADDING * 2;
        _rect.sizeDelta = new Vector2(_rect.sizeDelta.x, height);
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenPosition, null, out local)) return;
        Rect area = _area.rect;
        float x = Mathf.Clamp(local.x - area.xMin, 0, area.width - _rect.sizeDelta.x);
        float y = Mathf.Clamp(area.yMax - local.y - height - POINTER_GAP, 0, area.height - height);
        _rect.anchoredPosition = new Vector2(x, -y);
        gameObject.SetActive(true);
    }

    /// <summary>설명을 숨긴다.</summary>
    public void Hide() { gameObject.SetActive(false); }
}
