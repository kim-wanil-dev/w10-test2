using UnityEngine;
using UnityEngine.UI;

/// <summary>도안 설명 박스 옆에 키워드 태그(한 방·순수·모자이크)의 발동 조건 설명을 따로 띄우는 작은 패널이다. 화면에 하나만 두고 코드로 만든다.</summary>
public sealed class SpellTagTooltip : MonoBehaviour
{
    private const float WIDTH = 250f;
    private const float PADDING = 10f;
    private const float BAR = 4f;
    private const float GAP = 8f;
    // 전투 도안 설명 툴팁 글자와 비슷한 크기다.
    private const int FONT_SIZE = 14;
    private static readonly Color BACKGROUND_COLOR = new Color(0.06f, 0.07f, 0.1f, 0.96f);
    private static readonly Color BAR_COLOR = new Color(0.98f, 0.8f, 0.36f);
    private static SpellTagTooltip _instance;

    private RectTransform _rect;
    private RectTransform _parent;
    private Text _text;

    /// <summary>parent 아래에 태그 설명 패널을 하나 만든다. 이미 있으면 새로 만들지 않는다.</summary>
    public static void Create(RectTransform parent, Font font)
    {
        if (_instance != null) return;
        GameObject panel = new GameObject("Tag Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _instance = panel.AddComponent<SpellTagTooltip>();
        _instance.Build(parent, font);
    }

    /// <summary>text를 beside 박스의 오른쪽(공간이 없으면 왼쪽)에 위쪽을 맞춰 표시한다. text가 비었거나 beside가 없으면 숨긴다.</summary>
    public static void Show(string text, RectTransform beside)
    {
        if (_instance == null) return;
        if (string.IsNullOrEmpty(text) || beside == null || !beside.gameObject.activeInHierarchy) { Hide(); return; }
        _instance.Place(text, beside);
    }

    /// <summary>태그 설명 패널을 숨긴다.</summary>
    public static void Hide()
    {
        if (_instance != null) _instance.gameObject.SetActive(false);
    }

    /// <summary>배경, 위쪽 강조 막대, 여러 줄 문구를 구성하고 숨겨 둔다. 입력은 받지 않는다.</summary>
    private void Build(RectTransform parent, Font font)
    {
        _parent = parent; _rect = (RectTransform)transform;
        _rect.SetParent(parent, false);
        _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f); _rect.pivot = new Vector2(0, 1);
        Image background = GetComponent<Image>(); background.color = BACKGROUND_COLOR; background.raycastTarget = false;
        RectTransform bar = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(_rect, false);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(0.5f, 1); bar.sizeDelta = new Vector2(0, BAR); bar.anchoredPosition = Vector2.zero;
        Image barImage = bar.GetComponent<Image>(); barImage.color = BAR_COLOR; barImage.raycastTarget = false;
        RectTransform textRect = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<RectTransform>();
        textRect.SetParent(_rect, false);
        textRect.anchorMin = new Vector2(0, 1); textRect.anchorMax = new Vector2(0, 1); textRect.pivot = new Vector2(0, 1);
        textRect.anchoredPosition = new Vector2(PADDING, -(BAR + PADDING)); textRect.sizeDelta = new Vector2(WIDTH - PADDING * 2, 10);
        _text = textRect.GetComponent<Text>();
        _text.font = font; _text.fontSize = FONT_SIZE; _text.lineSpacing = 1.1f; _text.alignment = TextAnchor.UpperLeft; _text.color = new Color(0.92f, 0.94f, 0.96f);
        _text.supportRichText = true; _text.raycastTarget = false; _text.horizontalOverflow = HorizontalWrapMode.Wrap; _text.verticalOverflow = VerticalWrapMode.Overflow;
        gameObject.SetActive(false);
    }

    /// <summary>문구에 맞춰 높이를 정하고 beside 박스 옆, 부모 영역 안쪽에 놓는다.</summary>
    private void Place(string text, RectTransform beside)
    {
        _text.text = text;
        float height = _text.preferredHeight + BAR + PADDING * 2;
        _rect.sizeDelta = new Vector2(WIDTH, height);
        Vector3[] corners = new Vector3[4];
        beside.GetWorldCorners(corners);
        Vector2 min = _parent.InverseTransformPoint(corners[0]), max = _parent.InverseTransformPoint(corners[2]);
        Rect area = _parent.rect;
        float x = max.x + GAP;
        if (x + WIDTH > area.xMax) x = min.x - GAP - WIDTH;
        x = Mathf.Clamp(x, area.xMin, area.xMax - WIDTH);
        float y = Mathf.Clamp(max.y, area.yMin + height, area.yMax);
        _rect.localPosition = new Vector3(x, y, 0);
        gameObject.SetActive(true);
        _rect.SetAsLastSibling();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
