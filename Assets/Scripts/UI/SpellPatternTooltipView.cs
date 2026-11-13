using UnityEngine;
using UnityEngine.UI;

/// <summary>전투 중 도안을 가리켰을 때 도안 이름·효과·예상 수치를 카드 모양 패널로 보여 준다. 봉인·저주 상태면 패널 위쪽 띠와 테두리 색으로 상태를 알린다. 입력은 받지 않고 코드로 만든다.</summary>
public sealed class SpellPatternTooltipView : MonoBehaviour
{
    private const float WIDTH = 290f;
    private const float BORDER = 2f;
    private const float PADDING = 14f;
    private const float ACCENT = 4f;
    private const float POINTER_GAP = 18f;
    private const float SECTION_GAP = 8f;
    private static readonly Color BACKGROUND = new Color(0.07f, 0.075f, 0.1f, 0.97f);
    private static readonly Color FRAME = new Color(0.22f, 0.23f, 0.28f, 1f);
    private static readonly Color DIVIDER = new Color(1f, 1f, 1f, 0.09f);
    private static readonly Color KEYWORD = new Color(0.98f, 0.8f, 0.36f);

    /// <summary>패널에 표시할 내용이다. Status가 비어 있지 않으면 위쪽 상태 띠를 StatusColor로 그리고, Disabled면 본문을 흐리게 한다.</summary>
    public struct Content
    {
        public string Name;
        public string KindLabel;
        public string Effect;
        public string Keyword;
        public string ValueLabel;
        public string Value;
        public string Extras;
        public Color Accent;
        public Color ValueColor;
        public string Status;
        public string StatusDetail;
        public Color StatusColor;
        public bool Disabled;
    }

    private RectTransform _rect, _parent, _body, _banner, _accent, _divider;
    private Image _frame, _background, _bannerImage, _accentImage;
    private CanvasGroup _bodyGroup;
    private Text _status, _name, _kind, _effect, _valueLabel, _value, _extras;
    private Color _textColor, _subColor;

    public RectTransform Box { get { return _rect; } }

    /// <summary>parent 아래에 패널을 만들고 숨겨 둔다. textColor·subColor는 다른 UI와 같은 본문·보조 글자색이다.</summary>
    public static SpellPatternTooltipView Create(RectTransform parent, Font font, Color textColor, Color subColor)
    {
        GameObject root = new GameObject("Pattern Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        SpellPatternTooltipView view = root.AddComponent<SpellPatternTooltipView>();
        view.Build(parent, font, textColor, subColor);
        return view;
    }

    /// <summary>내용을 채우고 높이를 맞춘 뒤 포인터 아래쪽(공간이 없으면 위쪽)에 화면 안으로 맞춰 띄운다.</summary>
    public void Show(Content content, Vector2 screenPosition)
    {
        Layout(content);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screenPosition, null, out Vector2 local)) return;
        Rect area = _parent.rect;
        float height = _rect.sizeDelta.y;
        float x = Mathf.Clamp(local.x + POINTER_GAP, area.xMin, area.xMax - WIDTH);
        float top = local.y - POINTER_GAP;
        if (top - height < area.yMin) top = local.y + POINTER_GAP + height;
        top = Mathf.Clamp(top, area.yMin + height, area.yMax);
        _rect.localPosition = new Vector3(x, top, 0);
        gameObject.SetActive(true);
        _rect.SetAsLastSibling();
    }

    /// <summary>패널을 숨긴다.</summary>
    public void Hide() { gameObject.SetActive(false); }

    /// <summary>테두리·배경·상태 띠·강조 막대·글자 요소를 만든다.</summary>
    private void Build(RectTransform parent, Font font, Color textColor, Color subColor)
    {
        _parent = parent; _textColor = textColor; _subColor = subColor;
        _rect = (RectTransform)transform;
        _rect.SetParent(parent, false);
        _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f); _rect.pivot = new Vector2(0, 1);
        _frame = GetComponent<Image>(); _frame.color = FRAME; _frame.raycastTarget = false;

        RectTransform inner = Child("Background", _rect, typeof(Image));
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one; inner.offsetMin = new Vector2(BORDER, BORDER); inner.offsetMax = new Vector2(-BORDER, -BORDER);
        _background = inner.GetComponent<Image>(); _background.color = BACKGROUND; _background.raycastTarget = false;

        _banner = TopChild("Status", inner, typeof(Image));
        _bannerImage = _banner.GetComponent<Image>(); _bannerImage.raycastTarget = false;
        _status = Label(_banner, font, 14, TextAnchor.UpperLeft, Color.white);

        _accent = TopChild("Accent", inner, typeof(Image));
        _accentImage = _accent.GetComponent<Image>(); _accentImage.raycastTarget = false;

        _body = TopChild("Body", inner, typeof(CanvasGroup));
        _bodyGroup = _body.GetComponent<CanvasGroup>(); _bodyGroup.blocksRaycasts = false; _bodyGroup.interactable = false;
        _name = Label(_body, font, 20, TextAnchor.UpperLeft, textColor); _name.fontStyle = FontStyle.Bold;
        _kind = Label(_body, font, 13, TextAnchor.UpperRight, subColor);
        _effect = Label(_body, font, 15, TextAnchor.UpperLeft, textColor);
        _divider = TopChild("Divider", _body, typeof(Image));
        Image divider = _divider.GetComponent<Image>(); divider.color = DIVIDER; divider.raycastTarget = false;
        _valueLabel = Label(_body, font, 13, TextAnchor.LowerLeft, subColor);
        _value = Label(_body, font, 26, TextAnchor.LowerRight, textColor); _value.fontStyle = FontStyle.Bold;
        _extras = Label(_body, font, 13, TextAnchor.UpperLeft, subColor);
        gameObject.SetActive(false);
    }

    /// <summary>내용에 맞춰 각 요소의 위치를 위에서부터 차례로 정하고 패널 높이를 정한다.</summary>
    private void Layout(Content c)
    {
        float inner = WIDTH - BORDER * 2, textWidth = inner - PADDING * 2, y = 0;
        bool hasStatus = !string.IsNullOrEmpty(c.Status);
        _banner.gameObject.SetActive(hasStatus);
        if (hasStatus)
        {
            _status.text = $"<b>{c.Status}</b>" + (string.IsNullOrEmpty(c.StatusDetail) ? "" : $"\n<size=12>{c.StatusDetail}</size>");
            float height = Place(_status, PADDING, 10, textWidth) + 20;
            SetTop(_banner, 0, inner, height);
            _bannerImage.color = new Color(c.StatusColor.r * 0.55f, c.StatusColor.g * 0.55f, c.StatusColor.b * 0.55f, 1f);
            y = height;
        }
        // 봉인·저주 상태면 테두리와 배경 전체를 상태 색으로 물들여 한눈에 알 수 있게 한다.
        _frame.color = hasStatus ? c.StatusColor : FRAME;
        Color tint = hasStatus ? Color.Lerp(BACKGROUND, c.StatusColor, 0.16f) : BACKGROUND;
        tint.a = 1f; _background.color = tint;
        _accentImage.color = hasStatus ? c.StatusColor : c.Accent;
        SetTop(_accent, y, inner, ACCENT);
        y += ACCENT;

        float body = PADDING;
        _name.text = c.Name;
        _kind.text = c.KindLabel; _kind.color = Color.Lerp(c.Accent, _textColor, 0.35f);
        float kindWidth = Mathf.Min(_kind.preferredWidth + 2, textWidth * 0.4f);
        float nameHeight = Place(_name, PADDING, body, textWidth - kindWidth - 6);
        Place(_kind, PADDING + textWidth - kindWidth, body + 5, kindWidth);
        body += nameHeight + 4;

        _effect.text = string.IsNullOrEmpty(c.Keyword) ? c.Effect : $"{c.Effect}\n<color=#{ColorUtility.ToHtmlStringRGB(KEYWORD)}>{c.Keyword}</color>";
        body += Place(_effect, PADDING, body, textWidth) + SECTION_GAP + 2;

        bool hasValue = !string.IsNullOrEmpty(c.Value) || !string.IsNullOrEmpty(c.ValueLabel);
        _divider.gameObject.SetActive(hasValue || !string.IsNullOrEmpty(c.Extras));
        SetTop(_divider, body, textWidth, 1); _divider.anchoredPosition = new Vector2(PADDING, -body);
        body += 1 + SECTION_GAP;

        _valueLabel.gameObject.SetActive(hasValue); _value.gameObject.SetActive(hasValue);
        if (hasValue)
        {
            _value.text = c.Value ?? ""; _value.color = c.ValueColor;
            _valueLabel.text = c.ValueLabel ?? "";
            float valueHeight = Mathf.Max(_value.preferredHeight, 30);
            SetBox(_value, PADDING, body, textWidth, valueHeight);
            SetBox(_valueLabel, PADDING, body, textWidth, valueHeight - 4);
            body += valueHeight;
        }
        _extras.gameObject.SetActive(!string.IsNullOrEmpty(c.Extras));
        if (_extras.gameObject.activeSelf)
        {
            _extras.text = c.Extras;
            body += 2 + Place(_extras, PADDING, body + 2, textWidth);
        }
        body += PADDING;
        SetTop(_body, y, inner, body);
        _bodyGroup.alpha = c.Disabled ? 0.45f : 1f;
        _rect.sizeDelta = new Vector2(WIDTH, y + body + BORDER * 2);
    }

    /// <summary>글자를 부모 왼쪽 위 기준 (x, y)에 너비 width로 놓고 그 높이를 반환한다.</summary>
    private static float Place(Text text, float x, float y, float width)
    {
        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(width, 10);
        float height = text.preferredHeight;
        SetBox(text, x, y, width, height);
        return height;
    }

    /// <summary>글자 영역을 부모 왼쪽 위 기준 위치와 크기로 정한다.</summary>
    private static void SetBox(Text text, float x, float y, float width, float height)
    {
        RectTransform rect = text.rectTransform;
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>부모 왼쪽 위 기준으로 y 위치에 너비·높이를 정한다.</summary>
    private static void SetTop(RectTransform rect, float y, float width, float height)
    {
        rect.anchoredPosition = new Vector2(0, -y); rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>왼쪽 위를 기준점으로 쓰는 자식 요소를 만든다.</summary>
    private static RectTransform TopChild(string name, RectTransform parent, System.Type component)
    {
        RectTransform rect = Child(name, parent, component);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        return rect;
    }

    /// <summary>지정 구성 요소를 가진 자식 요소를 만든다.</summary>
    private static RectTransform Child(string name, RectTransform parent, System.Type component)
    {
        GameObject go = component == typeof(Image) || component == typeof(Text)
            ? new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), component)
            : new GameObject(name, typeof(RectTransform), component);
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>여러 줄 글자 요소를 만든다.</summary>
    private static Text Label(RectTransform parent, Font font, int size, TextAnchor alignment, Color color)
    {
        Text text = TopChild("Text", parent, typeof(Text)).GetComponent<Text>();
        text.font = font; text.fontSize = size; text.alignment = alignment; text.color = color; text.lineSpacing = 1.1f;
        text.supportRichText = true; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}
