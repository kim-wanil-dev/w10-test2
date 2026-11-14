using System;
using System.Collections;

using UnityEngine;

/// <summary>화면 연출에 쓰는 보간 곡선과 코루틴 트윈이다. 일시정지나 연출 정지 중에도 UI가 움직이도록 기본은 실제 시간(unscaled)을 쓴다.</summary>
public static class SpellEase
{
    /// <summary>빠르게 시작해 부드럽게 멈춘다.</summary>
    public static float OutCubic(float t) { t = 1 - Mathf.Clamp01(t); return 1 - t * t * t; }

    /// <summary>천천히 시작해 빨라진다.</summary>
    public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }

    /// <summary>천천히 시작해 빨라졌다가 부드럽게 멈춘다.</summary>
    public static float InOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>목표를 살짝 지나쳤다가 돌아온다(1.70158 오버슛).</summary>
    public static float OutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        t = Mathf.Clamp01(t) - 1;
        return 1 + c3 * t * t * t + c1 * t * t;
    }

    /// <summary>용수철처럼 튕기며 멈춘다.</summary>
    public static float OutElastic(float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return Mathf.Pow(2, -10 * t) * Mathf.Sin((t * 10 - 0.75f) * (2 * Mathf.PI / 3)) + 1;
    }

    /// <summary>0에서 1로 갔다가 0으로 돌아오는 한 번의 튐이다. 맞았을 때의 번쩍임·흔들림 세기에 쓴다.</summary>
    public static float Punch(float t) { t = Mathf.Clamp01(t); return Mathf.Sin(t * Mathf.PI) * (1 - t); }

    /// <summary>duration초 동안 ease(0→1) 값을 apply에 넘기고 마지막에 1을 넘긴다. unscaled가 false면 게임 시간 정지에 함께 멈춘다.</summary>
    public static IEnumerator Tween(float duration, Action<float> apply, Func<float, float> ease = null, bool unscaled = true)
    {
        if (duration > 0)
            for (float t = 0f; t < 1f; t += (unscaled ? Time.unscaledDeltaTime : Time.deltaTime) / duration)
            {
                apply(ease != null ? ease(t) : t);
                yield return null;
            }
        apply(1f);
    }
}
