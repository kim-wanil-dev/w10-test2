using System;
using System.Collections.Generic;

/// <summary>도형별 분해표 없이 실제 점유 영역을 더 작은 등록 도형으로 정확히 분할한다.</summary>
public static class SpellShapePartitioner
{
    private const int MAX_SEARCH_STATES = 50000;

    public readonly struct Part
    {
        public int Shape { get; }
        public int Rotation { get; }
        public int X { get; }
        public int Y { get; }
        public ulong Mask { get; }
        public int Area { get; }
        public int Priority { get; }

        /// <summary>부분 도형의 종류·방향·위치와 원본 좌표 기준 점유 영역을 저장한다.</summary>
        public Part(int shape, int rotation, int x, int y, ulong mask, int priority = 0)
        {
            Shape = shape; Rotation = rotation; X = x; Y = y; Mask = mask; Area = CountBits(mask); Priority = priority;
        }
    }

    /// <summary>원본을 덮는 분할을 반환한다. 조각 수와 면적 불균형이 작은 해를 우선하며, 해가 없으면 빈 배열이다. shapes는 방향별 정규화 비트맵이다.</summary>
    public static Part[] Split(ulong parent, ulong[][] shapes, int gridSize, int excludedShape, int[] priorities = null)
    {
        if (gridSize < 1 || gridSize * gridSize * 4 > 64) throw new ArgumentOutOfRangeException(nameof(gridSize));
        if (priorities != null && priorities.Length != shapes.Length) throw new ArgumentException("도형과 우선순위 개수가 다릅니다", nameof(priorities));
        int parentArea = CountBits(parent);
        List<Part> candidates = new List<Part>();
        Dictionary<ulong, Part> unique = new Dictionary<ulong, Part>();
        for (int shape = 0; shape < shapes.Length; shape++)
        {
            if (shape == excludedShape) continue;
            for (int rotation = 0; rotation < shapes[shape].Length; rotation++)
            {
                ulong mask = shapes[shape][rotation];
                int area = CountBits(mask);
                if (area == 0 || area >= parentArea) continue;
                GetBounds(mask, gridSize, out int width, out int height);
                for (int y = 0; y <= gridSize - height; y++) for (int x = 0; x <= gridSize - width; x++)
                {
                    ulong moved = mask << ((y * gridSize + x) * 4);
                    if ((moved & parent) != moved) continue;
                    Part part = new Part(shape, rotation, x, y, moved, priorities == null ? 0 : priorities[shape]);
                    if (!unique.TryGetValue(moved, out Part existing) || part.Priority < existing.Priority || (part.Priority == existing.Priority && ComparePart(part, existing) < 0)) unique[moved] = part;
                }
            }
        }
        candidates.AddRange(unique.Values);
        candidates.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Area != b.Area ? b.Area.CompareTo(a.Area) : ComparePart(a, b));
        Dictionary<ulong, Part[]> memo = new Dictionary<ulong, Part[]>();
        int states = 0;
        return Solve(parent, candidates, memo, ref states) ?? Array.Empty<Part>();
    }

    /// <summary>남은 영역을 모두 덮는 해를 메모화 탐색한다. 탐색 한도에 도달하면 확인된 유효 해만 사용한다.</summary>
    private static Part[] Solve(ulong remaining, List<Part> candidates, Dictionary<ulong, Part[]> memo, ref int states)
    {
        if (remaining == 0) return Array.Empty<Part>();
        if (memo.TryGetValue(remaining, out Part[] cached)) return cached;
        if (++states > MAX_SEARCH_STATES) return null;
        ulong first = remaining & (~remaining + 1);
        Part[] best = null;
        foreach (Part candidate in candidates)
        {
            if ((candidate.Mask & first) == 0 || (candidate.Mask & remaining) != candidate.Mask) continue;
            Part[] tail = Solve(remaining ^ candidate.Mask, candidates, memo, ref states);
            if (tail == null) continue;
            Part[] result = new Part[tail.Length + 1];
            result[0] = candidate; Array.Copy(tail, 0, result, 1, tail.Length);
            Array.Sort(result, ComparePart);
            if (IsBetter(result, best)) best = result;
        }
        memo[remaining] = best;
        return best;
    }

    /// <summary>조각 수, 면적 제곱합, 종류·방향·위치 순으로 두 분할을 비교해 일관된 결과를 선택한다.</summary>
    private static bool IsBetter(Part[] candidate, Part[] current)
    {
        if (current == null) return true;
        long candidatePriority = 0, currentPriority = 0;
        foreach (Part part in candidate) candidatePriority += (long)part.Priority * part.Area;
        foreach (Part part in current) currentPriority += (long)part.Priority * part.Area;
        if (candidatePriority != currentPriority) return candidatePriority < currentPriority;
        if (candidate.Length != current.Length) return candidate.Length < current.Length;
        int candidateScore = 0, currentScore = 0;
        for (int i = 0; i < candidate.Length; i++) { candidateScore += candidate[i].Area * candidate[i].Area; currentScore += current[i].Area * current[i].Area; }
        if (candidateScore != currentScore) return candidateScore < currentScore;
        for (int i = 0; i < candidate.Length; i++)
        {
            int compare = ComparePart(candidate[i], current[i]);
            if (compare != 0) return compare < 0;
        }
        return false;
    }

    /// <summary>부분 도형을 종류, 방향, 행, 열 순서로 정렬한다.</summary>
    private static int ComparePart(Part a, Part b)
    {
        if (a.Shape != b.Shape) return a.Shape.CompareTo(b.Shape);
        if (a.Rotation != b.Rotation) return a.Rotation.CompareTo(b.Rotation);
        return a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
    }

    /// <summary>비트맵에 포함된 점유 구역 수를 반환한다.</summary>
    public static int CountBits(ulong mask)
    {
        int count = 0;
        while (mask != 0) { mask &= mask - 1; count++; }
        return count;
    }

    /// <summary>정규화 비트맵의 오른쪽·아래 경계를 칸 수로 반환한다.</summary>
    private static void GetBounds(ulong mask, int gridSize, out int width, out int height)
    {
        width = height = 0;
        for (int y = 0; y < gridSize; y++) for (int x = 0; x < gridSize; x++)
            if (((mask >> ((y * gridSize + x) * 4)) & 15UL) != 0) { width = Math.Max(width, x + 1); height = Math.Max(height, y + 1); }
    }
}
