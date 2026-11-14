using UnityEngine;
using UnityEngine.UI;

/// <summary>화면 전체가 함께 쓰는 색·글꼴·둥근 판 그림 모음이다. 색은 기존 프리팹 화면이 쓰던 값이고,
/// 글꼴과 그림은 시작할 때 프리팹 화면에서 찾아 두어 코드로 만드는 요소도 같은 모양을 쓰게 한다.</summary>
public static class SpellStyle
{
    // 밤색 잉크 바탕, 남색 판, 금색 가는 테두리, 크림색 글자.
    public static readonly Color Ink = Hex("0F1426");
    public static readonly Color InkDeep = Hex("070A14");
    public static readonly Color Panel = Hex("131A2E");
    public static readonly Color PanelRaised = Hex("202946");
    public static readonly Color Button = Hex("2B3A6E");
    public static readonly Color Line = Hex("39446A");
    public static readonly Color Cream = Hex("F1EBDD");
    public static readonly Color Parchment = Hex("F6E7C1");
    public static readonly Color Soft = Hex("BDB4A0");
    public static readonly Color Muted = Hex("857F72");
    public static readonly Color Gold = Hex("D6B46E");
    public static readonly Color GoldBright = Hex("FACC5C");
    public static readonly Color Danger = Hex("E0605A");
    public static readonly Color Heal = Hex("6CCB7A");
    public static readonly Color Guard = Hex("8FB3D9");

    public static Font Serif { get; private set; }
    public static Font Sans { get; private set; }
    public static Font SansBold { get; private set; }
    public static Sprite Round8 { get; private set; }
    public static Sprite Round14 { get; private set; }
    public static Sprite Ring8 { get; private set; }
    public static Sprite Ring14 { get; private set; }
    public static Sprite FrameHairline { get; private set; }
    public static Sprite FrameGold { get; private set; }
    public static Sprite Pill { get; private set; }
    public static Sprite FadeV { get; private set; }
    public static Sprite FadeH { get; private set; }

    private static Material _glowMaterial;

    /// <summary>가산 혼합(빛 번짐)을 정점마다 고를 수 있는 UI 재질이다. 셰이더를 찾지 못하면 null이라 기본 재질로 그린다.</summary>
    public static Material GlowMaterial
    {
        get
        {
            if (_glowMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("SpellUiGlow");
                if (shader != null && shader.isSupported) _glowMaterial = new Material(shader) { name = "Spell UI Glow", hideFlags = HideFlags.HideAndDontSave };
            }
            return _glowMaterial;
        }
    }

    /// <summary>root 아래(꺼진 것 포함)의 글자·그림에서 이름으로 글꼴과 둥근 판 그림을 찾아 둔다. fallback은 글꼴을 못 찾았을 때 쓴다.</summary>
    public static void Capture(Transform root, Font fallback)
    {
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
        {
            if (text.font == null) continue;
            switch (text.font.name)
            {
                case "NotoSerifKR-Bold": if (Serif == null) Serif = text.font; break;
                case "NotoSansKR-Regular": if (Sans == null) Sans = text.font; break;
                case "NotoSansKR-Bold": if (SansBold == null) SansBold = text.font; break;
            }
        }
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite == null) continue;
            switch (image.sprite.name)
            {
                case "ui_round_8": if (Round8 == null) Round8 = image.sprite; break;
                case "ui_round_14": if (Round14 == null) Round14 = image.sprite; break;
                case "ui_ring_8": if (Ring8 == null) Ring8 = image.sprite; break;
                case "ui_ring_14": if (Ring14 == null) Ring14 = image.sprite; break;
                case "ui_frame_hairline": if (FrameHairline == null) FrameHairline = image.sprite; break;
                case "ui_frame_gold": if (FrameGold == null) FrameGold = image.sprite; break;
                case "ui_pill": if (Pill == null) Pill = image.sprite; break;
                case "ui_fade_v": if (FadeV == null) FadeV = image.sprite; break;
                case "ui_fade_h": if (FadeH == null) FadeH = image.sprite; break;
            }
        }
        if (Sans == null) Sans = fallback;
        if (SansBold == null) SansBold = Sans;
        if (Serif == null) Serif = SansBold;
    }

    /// <summary>그림에 둥근 판 그림을 9조각으로 입힌다. 그림을 찾지 못했으면 단색 사각형으로 둔다.</summary>
    public static void Round(Image image, Sprite sprite, Color color)
    {
        image.color = color;
        if (sprite == null) return;
        image.sprite = sprite; image.type = Image.Type.Sliced;
    }

    /// <summary>parent를 가득 채우는 테두리 그림을 자식으로 붙인다. 입력은 받지 않는다.</summary>
    public static Image AddRing(RectTransform parent, Sprite sprite, Color color)
    {
        if (sprite == null) return null;
        RectTransform rect = new GameObject("Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = rect.GetComponent<Image>();
        image.sprite = sprite; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false;
        return image;
    }

    /// <summary>색의 알파만 바꾼 색을 반환한다.</summary>
    public static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
