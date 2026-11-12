using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>주문 조각 전투의 카드, 제작판과 턴 상태를 관리한다.</summary>
public sealed class SpellGame
{
    private const int HAMMER_USES_PER_TURN = 1;
    private const float MAX_DECOMPOSITION_CHANCE = 0.9f;
    private const int GLUE_USES_PER_TURN = 1;
    private const int CURSE_INTERVAL_TURNS = 1;
    private const int CURSE_FAILURE_AMOUNT = 10;

    public enum Shape { Square, Triangle, Rectangle, QuarterTriangle, Diamond, Parallelogram, MediumTriangle, House, LargeTriangle, LShape, Clay }
    public enum Effect { None, Period, Resonance }
    public enum Seal { None, Recall, Shard, Refill, Overpaint }
    public enum Coating { None, Guard, Amplify, Cog }
    public enum RewardKind { None, Piece, Pattern, Seal }
    public enum Phase { Title, Combat, Reward, Loadout, Victory, Defeat }
    public enum SpellKind { Single, Block, All, Scatter }
    public enum Keyword { None, OneStroke, Mosaic, Purity }

    public const int PATTERN_COUNT = 8;
    public const int MAX_HAND = 7;
    public const int MAX_HITS = 6;
    public const int SHARD_DAMAGE = 2;
    private const int LOCAL_SIZE = 3;
    private const int CELL_COUNT = LOCAL_SIZE * LOCAL_SIZE;
    private const int FULL_MASK = 15;
    private const int AMPLIFY_BONUS = 2;
    private const int AMPLIFY_HIT_BONUS = 1;
    private const int GUARD_BONUS = 3;
    private const int REWARD_PIECE_COUNT = 3;
    private const int REWARD_PATTERN_COUNT = 3;
    private const double REWARD_SEAL_CHANCE = 0.5;
    private const double OPTIONAL_REWARD_CHANCE = 0.5;
    private const int PERIOD_BONUS = 3;
    private const int RESONANCE_BONUS = 3;
    private const int RESONANCE_COUNT = 3;
    private const int COG_LIMIT = 3;
    private const int OVERPAINT_BONUS = 2;
    private const int ONE_STROKE_BONUS = 6;
    private const int PURITY_BONUS = 6;
    private const int VICTORY_HEAL = 6;
    private const int START_HEALTH = 50;
    private const int DEVELOPER_HEALTH = 9999;
    private const int LAST_BATTLE = 6;

    /// <summary>도형이 칸 하나에서 차지하는 위·오른쪽·아래·왼쪽 구역 비트다.</summary>
    public struct ShapeCell
    {
        public int X; public int Y; public int Mask;
        public ShapeCell(int x, int y, int mask) { X = x; Y = y; Mask = mask; }
    }

    /// <summary>조각의 1/4 구역 하나에 붙은 씰링이다. 좌표는 카드 저장 시 기본 방향(회전 0), 표시용 반환값은 요청한 방향 기준이다.</summary>
    public readonly struct SealSpot
    {
        public int X { get; }
        public int Y { get; }
        public int Sector { get; }
        public Seal Seal { get; }

        /// <summary>칸 좌표, 위·오른쪽·아래·왼쪽 구역 번호와 씰링 종류를 보관한다.</summary>
        public SealSpot(int x, int y, int sector, Seal seal) { X = x; Y = y; Sector = sector; Seal = seal; }
    }

    /// <summary>보상 후보 하나. 도안이면 Pattern이 0 이상이고, 조각이면 Piece에 아직 보유하지 않은 카드가 들어 있다.</summary>
    public struct Reward
    {
        public int Pattern; public Card Piece;
        public bool IsPattern { get { return Pattern >= 0; } }
    }

    /// <summary>쪼개기 결과 조각 하나의 도형과 방향, 원본 기본 방향 기준 왼쪽 위 칸이다.</summary>
    private readonly struct Fragment
    {
        public Shape Shape { get; }
        public int Rotation { get; }
        public int X { get; }
        public int Y { get; }

        /// <summary>도형, 0~3으로 맞춘 방향과 원본 기준 위치를 보관한다.</summary>
        public Fragment(Shape shape, int rotation, int x, int y) { Shape = shape; Rotation = (rotation % 4 + 4) % 4; X = x; Y = y; }
    }

    /// <summary>시전 순간의 판 상태로 계산한 주문 결과다. Value는 다타 주문이면 타격 하나의 피해다.</summary>
    public sealed class SpellEvaluation
    {
        public SpellKind Kind; public int Base; public int Value; public int Hits = 1; public int Block; public int ShardHits; public int Draws; public int Recalls;
        public readonly List<string> Terms = new List<string>();
        public int Total { get { return Kind == SpellKind.Scatter ? Value * Hits : Value; } }
    }
    public enum CurseFailureEffect { HPDamage }
    public enum CurseResolution { None, Spell, Hammer }

    public sealed class CardCurse
    {
        public Card Target { get; internal set; }
        public int AppliedTurn { get; }
        public CurseResolution Resolution { get; internal set; }
        public CurseFailureEffect FailureEffect { get; }
        public int Amount { get; }
        public bool IsResolved { get { return Resolution != CurseResolution.None; } }

        /// <summary>대상 인스턴스와 적용 턴, 실패 효과 종류 및 수치를 저주 상태로 보관한다.</summary>
        internal CardCurse(Card target, int turn, CurseFailureEffect effect, int amount)
        {
            Target = target; AppliedTurn = turn; FailureEffect = effect; Amount = amount;
        }
    }

    public sealed class Card
    {
        internal readonly List<SealSpot> SealList = new List<SealSpot>();

        public int Id { get; internal set; }
        public Shape Shape { get; internal set; }
        public Effect Effect { get; internal set; }
        public Coating Coating { get; internal set; }
        public IReadOnlyList<SealSpot> Seals { get { return SealList; } }
        public GluedShape Glued { get; internal set; }
        public bool IsUnique { get { return Glued == null && Shape == Shape.Clay; } }
    }

    public sealed class GluedShape
    {
        private readonly int[] _cells;

        public Card First { get; }
        public Card Second { get; }
        public int FirstRotation { get; }
        public int SecondRotation { get; }
        public int RelativeX { get; }
        public int RelativeY { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>두 원본과 상대 배치, 정규화한 전체 점유 영역을 변경 불가능한 접착 정보로 보관한다.</summary>
        internal GluedShape(Card first, int firstRotation, Card second, int secondRotation, int relativeX, int relativeY)
        {
            First = first; Second = second; FirstRotation = firstRotation; SecondRotation = secondRotation;
            RelativeX = relativeX; RelativeY = relativeY;
            int left = Math.Min(0, relativeX), top = Math.Min(0, relativeY);
            Width = Math.Max(ShapeWidth(first, firstRotation), relativeX + ShapeWidth(second, secondRotation)) - left;
            Height = Math.Max(ShapeHeight(first, firstRotation), relativeY + ShapeHeight(second, secondRotation)) - top;
            _cells = new int[Width * Height];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                _cells[y * Width + x] = CellMask(first, firstRotation, x + left, y + top)
                    | CellMask(second, secondRotation, x + left - relativeX, y + top - relativeY);
        }

        /// <summary>복합 도형 전체를 90도 단위로 회전한 지정 칸의 삼각 구역을 반환한다.</summary>
        internal int RotatedCell(int rotation, int x, int y)
        {
            int sx = rotation == 0 ? x : rotation == 1 ? y : rotation == 2 ? Width - 1 - x : Width - 1 - y;
            int sy = rotation == 0 ? y : rotation == 1 ? Height - 1 - x : rotation == 2 ? Height - 1 - y : x;
            if (sx < 0 || sy < 0 || sx >= Width || sy >= Height) return 0;
            int bits = _cells[sy * Width + sx];
            return ((bits << rotation) | (bits >> (4 - rotation))) & 15;
        }
    }

    public sealed class Placement
    {
        internal int[] Mask = new int[CELL_COUNT];

        public Card Card { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Rotation { get; internal set; }
        public int Turn { get; internal set; }
        public bool CanRefund { get; internal set; }

        /// <summary>도안 기준 칸 (x, y)에서 이 조각이 차지한 구역 마스크를 반환한다.</summary>
        public int CellMask(int x, int y) { return x < 0 || x >= LOCAL_SIZE || y < 0 || y >= LOCAL_SIZE ? 0 : Mask[y * LOCAL_SIZE + x]; }
    }

    public sealed class Board
    {
        private readonly List<Placement> _pieces = new List<Placement>();

        public int Spell { get; }
        public ReadOnlyCollection<Placement> Pieces { get; }
        public Placement Completer { get; internal set; }
        internal List<Placement> MutablePieces { get { return _pieces; } }

        /// <summary>지정 주문의 제작판과 외부에서 변경할 수 없는 배치 목록을 초기화한다.</summary>
        internal Board(int spell)
        {
            Spell = spell;
            Pieces = _pieces.AsReadOnly();
        }
    }

    public sealed class Enemy
    {
        private readonly int[] _pattern;    
        private readonly ReadOnlyCollection<int> _patternView;

        public string Name { get; internal set; }
        public int Health { get; internal set; }
        public int MaxHealth { get; internal set; }
        public bool IsSealer { get; }
        public bool IsThief { get; }
        public bool IsCurser { get; }
        public int SealedSpell { get; internal set; } = -1;
        internal int PreviousSealedSpell { get; set; } = -1;

        public int Intent { get { return _pattern[ActionCount % _pattern.Length]; } }
        public ReadOnlyCollection<int> Pattern { get { return _patternView; } }
        internal int ActionCount;

        /// <summary>이름, 체력, 공격 패턴과 신규 적 능력 여부로 적의 전투 상태를 초기화한다.</summary>
        internal Enemy(string name, int health, int[] pattern, bool isSealer = false, bool isThief = false, bool isCurser = false)
        {
            Name = name; Health = health; MaxHealth = health; _pattern = pattern; _patternView = Array.AsReadOnly(pattern); IsSealer = isSealer; IsThief = isThief; IsCurser = isCurser;
        }
    }

    private static readonly string[] PATTERN_NAMES = { "불씨", "방벽", "번개", "유성", "섬광", "산탄", "만화경", "강타" };
    private static readonly SpellKind[] PATTERN_KINDS = { SpellKind.Single, SpellKind.Block, SpellKind.Single, SpellKind.All, SpellKind.Single, SpellKind.Scatter, SpellKind.All, SpellKind.Single };
    private static readonly int[] PATTERN_BASE = { 6, 7, 12, 10, 8, 2, 6, 9 };
    private static readonly Keyword[] PATTERN_KEYWORDS = { Keyword.None, Keyword.None, Keyword.OneStroke, Keyword.None, Keyword.OneStroke, Keyword.Mosaic, Keyword.Purity, Keyword.None };
    private static readonly int[][] SPELL_MASKS = {
        new[] { 3, 8, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 2, 8, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 15, 0, 0, 15, 15, 0, 0, 0, 0 },
        new[] { 15, 15, 0, 15, 15, 0, 0, 0, 0 },
        new[] { 15, 9, 0, 9, 0, 0, 0, 0, 0 },
        new[] { 6, 15, 12, 0, 0, 0, 0, 0, 0 },
        new[] { 6, 12, 0, 3, 9, 0, 0, 0, 0 },
        new[] { 15, 0, 0, 0, 0, 0, 0, 0, 0 } };
    private static readonly ShapeCell[][] BASE_SHAPES = {
        new[] { new ShapeCell(0, 0, 15) },
        new[] { new ShapeCell(0, 0, 3) },
        new[] { new ShapeCell(0, 0, 15), new ShapeCell(1, 0, 15) },
        new[] { new ShapeCell(0, 0, 1) },
        new[] { new ShapeCell(0, 0, 2), new ShapeCell(1, 0, 8) },
        new[] { new ShapeCell(0, 0, 6), new ShapeCell(1, 0, 9) },
        new[] { new ShapeCell(0, 0, 6), new ShapeCell(1, 0, 12) },
        new[] { new ShapeCell(0, 0, 4), new ShapeCell(0, 1, 15) },
        new[] { new ShapeCell(0, 0, 15), new ShapeCell(1, 0, 9), new ShapeCell(0, 1, 9) },
        new[] { new ShapeCell(0, 0, 15), new ShapeCell(0, 1, 15), new ShapeCell(1, 1, 15) },
        new[] { new ShapeCell(0, 0, 15) } };
    private static readonly int SHAPE_KIND_COUNT = Enum.GetValues(typeof(Shape)).Length;
    private static readonly int SEAL_KIND_COUNT = Enum.GetValues(typeof(Seal)).Length - 1;
    private static readonly int COATING_KIND_COUNT = Enum.GetValues(typeof(Coating)).Length - 1;
    private static readonly Card[] DEBUG_PIECES = CreateDebugPieces();
    private static readonly ShapeCell[][][] SHAPE_ORIENTATIONS = CreateOrientations();

    private readonly Random _random = new Random();
    private readonly List<Card> _owned = new List<Card>();
    private readonly List<Card> _draw = new List<Card>();
    private readonly List<Card> _discard = new List<Card>();
    private int _nextId;
    private readonly List<Card> _hand = new List<Card>();
    private readonly List<Enemy> _enemies = new List<Enemy>();
    private readonly List<int> _ownedSpells = new List<int>();
    private readonly List<int> _spells = new List<int>();
    private readonly List<Reward> _rewards = new List<Reward>();
    private readonly List<Reward> _patternRewards = new List<Reward>();

    private readonly Board[] _boards = CreateBoards();
    private bool _hasSealerEncounter;
    private bool _hasThiefEncounter;
    private bool _hasCurserEncounter;
    private readonly List<Card> _stolen = new List<Card>();
    private Enemy _theftOwner;

    public Card TheftTarget { get; private set; }
    public CardCurse Curse { get; private set; }
    public int LastCurseDamage { get; private set; }
    public int StolenCount { get { return _stolen.Count; } }
    public bool HasLivingThief
    {
        get { foreach (Enemy enemy in Enemies) if (enemy.IsThief && enemy.Health > 0) return true; return false; }
    }

    public ReadOnlyCollection<Card> Hand { get; }
    public IReadOnlyList<Card> OwnedCards { get { return _owned; } }
    public static IReadOnlyList<Card> DebugPieces { get { return Array.AsReadOnly(DEBUG_PIECES); } }
    public ReadOnlyCollection<Enemy> Enemies { get; }
    public ReadOnlyCollection<int> Spells { get; }
    public ReadOnlyCollection<int> OwnedSpells { get; }
    public ReadOnlyCollection<Board> Boards { get; }
    public ReadOnlyCollection<Reward> Rewards { get; }
    public bool HasPieceReward { get; private set; }
    public bool HasPatternReward { get; private set; }
    public bool HasSealReward { get; private set; }
    public Seal RewardSeal { get; private set; }
    public RewardKind ActiveReward { get; private set; }
    public IReadOnlyList<Reward> ActiveRewardChoices { get { return ActiveReward == RewardKind.Pattern ? _patternRewards : _rewards; } }

    public Phase CurrentPhase { get; private set; } = Phase.Title;
    public int Health { get; private set; }
    public int MaxHealth { get; private set; } = START_HEALTH;
    public bool IsDeveloperMode { get; private set; }
    public int Block { get; private set; }
    public int Battle { get; private set; }
    public int Turn { get; private set; }
    public int HammerUsesLeft { get; private set; }
    public int GlueUsesLeft { get; private set; }
    public string SealNotice { get; private set; } = "";
    public int Target { get; private set; }
    public int LastAcquiredPattern { get; private set; } = -1;
    public int DrawCount { get { return _draw.Count; } }
    public int DiscardCount { get { return _discard.Count; } }
    public int CraftCount { get { int count = 0; foreach (Board board in Boards) count += board.Pieces.Count; return count; } }

    /// <summary>카드, 적, 도안과 제작판의 읽기 전용 목록을 연결한다.</summary>
    public SpellGame()
    {
        Hand = _hand.AsReadOnly(); Enemies = _enemies.AsReadOnly(); Spells = _spells.AsReadOnly(); OwnedSpells = _ownedSpells.AsReadOnly();
        Boards = Array.AsReadOnly(_boards); Rewards = _rewards.AsReadOnly();
    }

    /// <summary>도안 수만큼 빈 제작판을 만든다.</summary>
    private static Board[] CreateBoards()
    {
        Board[] boards = new Board[PATTERN_COUNT];
        for (int i = 0; i < boards.Length; i++) boards[i] = new Board(i);
        return boards;
    }

    /// <summary>개발자 패널에서 추가할 조각 원형을 만든다. 일반 도형 전체, 씰링·코팅별 정사각형과 보상에서 빠진 이전 효과 조각이다.</summary>
    private static Card[] CreateDebugPieces()
    {
        List<Card> pieces = new List<Card>();
        foreach (Shape shape in Enum.GetValues(typeof(Shape))) pieces.Add(new Card { Shape = shape });
        for (int seal = 1; seal <= SEAL_KIND_COUNT; seal++)
        {
            Card card = new Card { Shape = Shape.Square }; card.SealList.Add(new SealSpot(0, 0, 0, (Seal)seal)); pieces.Add(card);
        }
        for (int coating = 1; coating <= COATING_KIND_COUNT; coating++) pieces.Add(new Card { Shape = Shape.Square, Coating = (Coating)coating });
        pieces.Add(new Card { Shape = Shape.QuarterTriangle, Effect = Effect.Period });
        pieces.Add(new Card { Shape = Shape.Triangle, Effect = Effect.Resonance });
        return pieces.ToArray();
    }

    /// <summary>모든 도형의 네 방향 칸 목록을 미리 계산한다. 평행사변형은 뒤집힌 두 방향을 회전 순서에 포함한다.</summary>
    private static ShapeCell[][][] CreateOrientations()
    {
        ShapeCell[][][] result = new ShapeCell[BASE_SHAPES.Length][][];
        for (int shape = 0; shape < BASE_SHAPES.Length; shape++)
        {
            result[shape] = new ShapeCell[4][];
            ShapeCell[] baseCells = BASE_SHAPES[shape];
            ShapeCell[] mirrored = Mirror(baseCells);
            for (int rotation = 0; rotation < 4; rotation++)
            {
                if ((Shape)shape == Shape.Parallelogram) result[shape][rotation] = Rotate(rotation < 2 ? baseCells : mirrored, rotation % 2);
                else result[shape][rotation] = Rotate(baseCells, rotation);
            }
        }
        return result;
    }

    /// <summary>칸 목록을 좌우로 뒤집는다. 오른쪽과 왼쪽 구역 비트도 맞바꾼다.</summary>
    private static ShapeCell[] Mirror(ShapeCell[] cells)
    {
        int width = 0; foreach (ShapeCell c in cells) width = Math.Max(width, c.X + 1);
        ShapeCell[] result = new ShapeCell[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            int m = cells[i].Mask;
            result[i] = new ShapeCell(width - 1 - cells[i].X, cells[i].Y, (m & 5) | ((m & 2) << 2) | ((m & 8) >> 2));
        }
        return result;
    }

    /// <summary>칸 목록을 시계 방향으로 rotation번 돌리고 왼쪽 위가 (0, 0)이 되게 맞춘다.</summary>
    private static ShapeCell[] Rotate(ShapeCell[] cells, int rotation)
    {
        ShapeCell[] result = (ShapeCell[])cells.Clone();
        for (int r = 0; r < rotation; r++)
        {
            int height = 0; foreach (ShapeCell c in result) height = Math.Max(height, c.Y + 1);
            for (int i = 0; i < result.Length; i++)
                result[i] = new ShapeCell(height - 1 - result[i].Y, result[i].X, RotateMask(result[i].Mask, 1));
        }
        return result;
    }

    /// <summary>구역 비트를 시계 방향으로 rotation번 돌린 마스크를 반환한다.</summary>
    public static int RotateMask(int mask, int rotation)
    {
        int r = (rotation % 4 + 4) % 4;
        return ((mask << r) | (mask >> (4 - r))) & FULL_MASK;
    }

    /// <summary>도형의 지정 방향 칸 목록을 반환한다. 왼쪽 위 범위가 (0, 0)이다. 점토는 한 칸 기준이다.</summary>
    public static IReadOnlyList<ShapeCell> ShapeCells(Shape shape, int rotation)
    {
        return SHAPE_ORIENTATIONS[(int)shape][(rotation % 4 + 4) % 4];
    }

    /// <summary>기본 방향의 구역 번호를 도형의 지정 방향 구역 번호로 바꾼다. 평행사변형의 2·3 방향은 좌우를 뒤집은 뒤 돌린다.</summary>
    private static int RotateSector(Shape shape, int rotation, int sector)
    {
        rotation = (rotation % 4 + 4) % 4;
        if (shape == Shape.Parallelogram && rotation >= 2) { sector = sector == 1 ? 3 : sector == 3 ? 1 : sector; rotation -= 2; }
        return (sector + rotation) % 4;
    }

    /// <summary>카드를 지정 방향으로 돌렸을 때 씰링 위치를 CardCells와 같은 좌표계로 반환한다. 접착 카드는 두 원본의 씰링을 모두 포함한다.</summary>
    public static List<SealSpot> SealSpots(Card card, int rotation)
    {
        List<SealSpot> result = new List<SealSpot>();
        rotation = (rotation % 4 + 4) % 4;
        if (card.Glued == null)
        {
            IReadOnlyList<ShapeCell> baseCells = ShapeCells(card.Shape, 0), cells = ShapeCells(card.Shape, rotation);
            foreach (SealSpot seal in card.Seals)
                for (int i = 0; i < baseCells.Count; i++)
                {
                    if (baseCells[i].X != seal.X || baseCells[i].Y != seal.Y) continue;
                    result.Add(new SealSpot(cells[i].X, cells[i].Y, RotateSector(card.Shape, rotation, seal.Sector), seal.Seal)); break;
                }
            return result;
        }
        GluedShape glued = card.Glued;
        int left = Math.Min(0, glued.RelativeX), top = Math.Min(0, glued.RelativeY);
        List<SealSpot> flat = new List<SealSpot>();
        foreach (SealSpot s in SealSpots(glued.First, glued.FirstRotation)) flat.Add(new SealSpot(s.X - left, s.Y - top, s.Sector, s.Seal));
        foreach (SealSpot s in SealSpots(glued.Second, glued.SecondRotation)) flat.Add(new SealSpot(s.X + glued.RelativeX - left, s.Y + glued.RelativeY - top, s.Sector, s.Seal));
        foreach (SealSpot s in flat)
        {
            int x = s.X, y = s.Y, width = glued.Width, height = glued.Height;
            // GluedShape.RotatedCell의 역방향과 같은 시계 방향 90도 회전을 rotation번 적용한다.
            for (int r = 0; r < rotation; r++) { int nx = height - 1 - y; y = x; x = nx; int w = width; width = height; height = w; }
            result.Add(new SealSpot(x, y, (s.Sector + rotation) % 4, s.Seal));
        }
        return result;
    }

    /// <summary>카드에 붙은 지정 씰링 수를 반환한다. 접착 카드는 두 원본의 씰링을 합산한다.</summary>
    public static int SealCount(Card card, Seal seal)
    {
        if (card.Glued != null) return SealCount(card.Glued.First, seal) + SealCount(card.Glued.Second, seal);
        int count = 0;
        foreach (SealSpot spot in card.Seals) if (spot.Seal == seal) count++;
        return count;
    }

    /// <summary>도형을 앵커 (x, y)와 방향으로 놓았을 때 칸 (cellX, cellY)에서 차지하는 구역 마스크를 반환한다.</summary>
    public static int ShapeMaskAt(Shape shape, int x, int y, int rotation, int cellX, int cellY)
    {
        foreach (ShapeCell cell in ShapeCells(shape, rotation)) if (x + cell.X == cellX && y + cell.Y == cellY) return cell.Mask;
        return 0;
    }

    /// <summary>도형이 차지하는 1/4 구역 수를 반환한다. 점토는 1칸으로 본다.</summary>
    public static int ShapeArea(Shape shape)
    {
        int area = 0; foreach (ShapeCell cell in ShapeCells(shape, 0)) area += BitCount(cell.Mask);
        return area;
    }

    /// <summary>4비트 구역 마스크에서 켜진 구역 수를 반환한다.</summary>
    public static int BitCount(int mask)
    {
        int count = 0;
        for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) count++;
        return count;
    }

    /// <summary>도안 이름을 반환한다.</summary>
    public static string PatternName(int spell) { return PATTERN_NAMES[spell]; }

    /// <summary>도안의 기본 효과 종류를 반환한다.</summary>
    public static SpellKind PatternKind(int spell) { return PATTERN_KINDS[spell]; }

    /// <summary>도안의 특수 키워드를 반환한다.</summary>
    public static Keyword PatternKeyword(int spell) { return PATTERN_KEYWORDS[spell]; }

    /// <summary>도안의 기본 수치를 반환한다. 다타 도안은 타격 하나의 피해다.</summary>
    public static int PatternBase(int spell) { return PATTERN_BASE[spell]; }

    /// <summary>단일 대상을 골라야 하는 도안인지 반환한다.</summary>
    public static bool NeedsTarget(int spell) { return spell >= 0 && spell < PATTERN_COUNT && PATTERN_KINDS[spell] == SpellKind.Single; }

    /// <summary>도안의 기본 효과와 키워드를 한 줄 설명으로 반환한다.</summary>
    public static string PatternDescription(int spell)
    {
        int value = PATTERN_BASE[spell];
        string text = PATTERN_KINDS[spell] == SpellKind.Single ? $"단일 피해 {value}" : PATTERN_KINDS[spell] == SpellKind.Block ? $"방어 {value}" : PATTERN_KINDS[spell] == SpellKind.All ? $"전체 피해 {value}" : $"조각 1장마다 무작위 피해 {value} (최대 {MAX_HITS}타)";
        switch (PATTERN_KEYWORDS[spell])
        {
            case Keyword.OneStroke: return text + $" · 한 방: 조각 1장으로 완성하면 +{ONE_STROKE_BONUS}";
            case Keyword.Mosaic: return "모자이크 · " + text;
            case Keyword.Purity: return text + $" · 순수: 한 가지 도형으로만 완성하면 +{PURITY_BONUS}";
            default: return text;
        }
    }

    /// <summary>도안의 1/4 구역 총수를 반환한다.</summary>
    public static int PatternArea(int spell)
    {
        int area = 0; foreach (int mask in SPELL_MASKS[spell]) area += BitCount(mask);
        return area;
    }

    /// <summary>드로우 또는 버림 더미를 도형·코팅·씰링이 같은 조각끼리 묶어 대표 카드와 수량 목록으로 반환한다.</summary>
    public List<KeyValuePair<Card, int>> GetPileSummary(bool isDrawPile)
    {
        List<KeyValuePair<Card, int>> summary = new List<KeyValuePair<Card, int>>();
        Dictionary<string, int> indices = new Dictionary<string, int>();
        foreach (Card card in isDrawPile ? _draw : _discard)
        {
            string key = CardKey(card);
            if (indices.TryGetValue(key, out int index)) summary[index] = new KeyValuePair<Card, int>(summary[index].Key, summary[index].Value + 1);
            else { indices.Add(key, summary.Count); summary.Add(new KeyValuePair<Card, int>(card, 1)); }
        }
        return summary;
    }

    /// <summary>더미 요약에서 같은 조각으로 묶을 기준 문자열을 만든다. 접착 카드는 모양이 제각각이라 카드마다 따로 센다.</summary>
    private static string CardKey(Card card)
    {
        if (card.Glued != null) return "Glued:" + card.Id;
        List<string> seals = new List<string>();
        foreach (SealSpot seal in card.Seals) seals.Add($"{seal.X},{seal.Y},{seal.Sector},{seal.Seal}");
        seals.Sort(StringComparer.Ordinal);
        return $"{card.Shape}:{card.Effect}:{card.Coating}:{string.Join(";", seals)}";
    }

    /// <summary>지정한 도안을 지금 시전했을 때의 공격 피해 합을 반환한다. 방어 도안은 0이다.</summary>
    public int GetSpellDamage(int boardIndex)
    {
        if (boardIndex < 0 || boardIndex >= Boards.Count || PATTERN_KINDS[boardIndex] == SpellKind.Block) return 0;
        return Evaluate(boardIndex).Total;
    }

    /// <summary>새 게임의 체력, 카드 및 도안을 초기화하고 첫 전투를 시작한다.</summary>
    public void StartGame(bool isDeveloperMode = false, bool withSealer = false, bool withThief = false, bool withCurser = false)
    {
        _hasSealerEncounter = withSealer;
        _hasThiefEncounter = withThief;
        _hasCurserEncounter = withCurser;
        IsDeveloperMode = isDeveloperMode; MaxHealth = isDeveloperMode ? DEVELOPER_HEALTH : START_HEALTH;
        _owned.Clear(); _spells.Clear(); _ownedSpells.Clear(); _nextId = 0; Health = MaxHealth; Battle = 0; LastAcquiredPattern = -1;
        if (isDeveloperMode) for (int i = 0; i < PATTERN_COUNT; i++) { _ownedSpells.Add(i); _spells.Add(i); }
        else { _ownedSpells.Add(0); _ownedSpells.Add(1); _ownedSpells.Add(7); _spells.Add(0); _spells.Add(1); _spells.Add(7); }
        for (int i = 0; i < 4; i++) AddCard(Shape.QuarterTriangle, Effect.None);
        for (int i = 0; i < 3; i++) AddCard(Shape.Triangle, Effect.None);
        for (int i = 0; i < 4; i++) AddCard(Shape.Diamond, Effect.None);
        AddCard(Shape.Square, Effect.None);
        StartBattle();
    }

    /// <summary>현재 게임을 종료하고 타이틀 화면 상태로 전환한다.</summary>
    public void ReturnToTitle() { FinishCombat(Phase.Title); }

    /// <summary>개별 ID를 가진 카드를 소유 목록에 추가한다.</summary>
    private void AddCard(Shape shape, Effect effect)
    {
        _owned.Add(new Card { Id = ++_nextId, Shape = shape, Effect = effect });
    }

    /// <summary>원본 카드의 도형·효과·코팅·씰링과 접착 구성을 새 ID로 복사한다. keepRecall이 false면 회수 씰링만 뺀다.</summary>
    private Card CopyCard(Card source, bool keepRecall)
    {
        Card copy = new Card { Id = ++_nextId, Shape = source.Shape, Effect = source.Effect, Coating = source.Coating };
        foreach (SealSpot seal in source.Seals) if (keepRecall || seal.Seal != Seal.Recall) copy.SealList.Add(seal);
        if (source.Glued != null)
        {
            GluedShape glued = source.Glued;
            copy.Glued = new GluedShape(CopyCard(glued.First, keepRecall), glued.FirstRotation, CopyCard(glued.Second, keepRecall), glued.SecondRotation, glued.RelativeX, glued.RelativeY);
        }
        return copy;
    }

    /// <summary>개발자 조각 원형을 복사해 보유 덱과 현재 손패에 한 장 추가한다.</summary>
    public bool AddDebugCard(int index)
    {
        if (CurrentPhase != Phase.Combat || index < 0 || index >= DEBUG_PIECES.Length) return false;
        Card card = CopyCard(DEBUG_PIECES[index], true);
        _owned.Add(card); _hand.Add(card);
        return true;
    }

    /// <summary>지정 카드를 보유 덱과 현재 위치에서 제거한다.</summary>
    public bool RemoveDebugCard(Card card)
    {
        if (CurrentPhase != Phase.Combat || !_owned.Remove(card)) return false;
        _hand.Remove(card); _draw.Remove(card); _discard.Remove(card);
        foreach (Board board in Boards)
        {
            for (int i = board.MutablePieces.Count - 1; i >= 0; i--)
                if (board.MutablePieces[i].Card == card) board.MutablePieces.RemoveAt(i);
            UpdateCompleter(board, null);
        }
        return true;
    }

    /// <summary>도안을 보유 목록과 전투 장착 목록에 추가한다.</summary>
    public bool AddDebugPattern(int spell)
    {
        if (CurrentPhase != Phase.Combat || spell < 0 || spell >= PATTERN_COUNT || _spells.Contains(spell)) return false;
        if (!_ownedSpells.Contains(spell)) _ownedSpells.Add(spell);
        _spells.Add(spell);
        return true;
    }

    /// <summary>다음 전투의 적과 덱을 준비하고 첫 턴을 시작한다.</summary>
    private void StartBattle()
    {
        Curse = null; LastCurseDamage = 0;
        CancelTheft(); _stolen.Clear();
        Battle++; Turn = 0; Block = 0; Target = 0; _enemies.Clear(); _hand.Clear(); _discard.Clear(); _draw.Clear();
        foreach (Board board in Boards) { board.MutablePieces.Clear(); board.Completer = null; }
        _draw.AddRange(_owned); Shuffle(_draw);
        GuaranteeOpeningShape(Shape.Square, _draw.Count - 1); GuaranteeOpeningShape(Shape.QuarterTriangle, _draw.Count - 2);
        if (IsDeveloperMode)
        {
            for (int i = 0; i < 3; i++) _enemies.Add(new Enemy("잉크 하수인", DEVELOPER_HEALTH, new[] { 4 }));
        }
        else if (Battle == 1) AddEnemy(0);
        else if (Battle == 2) { AddEnemy(0); AddEnemy(0); }
        else if (Battle == 3) AddEnemy(1);
        else if (Battle == 4) { AddEnemy(1); AddEnemy(0); }
        else if (Battle == 5) { AddEnemy(1); AddEnemy(1); }
        else if (Battle == 6) AddEnemy(2);
        if (_hasSealerEncounter && Battle == 1) _enemies.Add(new Enemy("형상 봉인자", 12, new[] { 4 }, true));
        if (_hasThiefEncounter && Battle == 1) _enemies.Add(new Enemy("도둑손", 12, new[] { 4 }, false, true));
        if (_hasCurserEncounter && Battle == 1) _enemies.Add(new Enemy("조각 저주술사", 12, new[] { 4 }, false, false, true));
        CurrentPhase = Phase.Combat; StartTurn();
        RefreshSeals();
    }

    /// <summary>적 종류에 따라 체력과 공격 패턴을 설정한다.</summary>
    private void AddEnemy(int kind)
    {
        _enemies.Add(kind == 0 ? new Enemy("잉크 하수인", 12, new[] { 4 }) : kind == 1 ? new Enemy("거대 잉크", 26, new[] { 2, 8 }) : new Enemy("서고의 수호자", 60, new[] { 4, 7, 12 }));
    }

    /// <summary>목록을 제자리에서 무작위로 섞는다.</summary>
    private void Shuffle(List<Card> cards)
    {
        for (int i = cards.Count - 1; i > 0; i--) { int j = _random.Next(i + 1); Card c = cards[i]; cards[i] = cards[j]; cards[j] = c; }
    }

    /// <summary>첫 손패에 포함할 도형을 섞인 덱의 지정된 뽑기 위치로 옮긴다.</summary>
    private void GuaranteeOpeningShape(Shape shape, int drawIndex)
    {
        for (int i = 0; i <= drawIndex; i++)
        {
            if (_draw[i].Shape != shape) continue;
            Card card = _draw[drawIndex]; _draw[drawIndex] = _draw[i]; _draw[i] = card; return;
        }
    }

    /// <summary>새 턴의 사용 횟수와 손패를 준비한 뒤 생존 저주술사의 대상 한 장을 지정한다.</summary>
    private void StartTurn()
    {
        Turn++; HammerUsesLeft = HAMMER_USES_PER_TURN; GlueUsesLeft = GLUE_USES_PER_TURN; Draw(5);
        ApplyCardCurse();
    }

    /// <summary>설정된 턴 간격에 생존 저주술사가 있으면 실제 손패에서 균등 무작위로 한 장만 저주한다.</summary>
    private void ApplyCardCurse()
    {
        if (Curse != null && Curse.AppliedTurn == Turn) return;
        Curse = null;
        if (Hand.Count == 0 || (Turn - 1) % CURSE_INTERVAL_TURNS != 0) return;
        foreach (Enemy enemy in Enemies)
        {
            if (!enemy.IsCurser || enemy.Health <= 0) continue;
            Curse = new CardCurse(Hand[_random.Next(Hand.Count)], Turn, CurseFailureEffect.HPDamage, CURSE_FAILURE_AMOUNT);
            return;
        }
    }

    /// <summary>지정 카드 인스턴스가 이번 턴의 미해결 저주 대상인지 반환한다.</summary>
    public bool IsCursed(Card card)
    {
        return card != null && Curse != null && Curse.AppliedTurn == Turn && !Curse.IsResolved && Curse.Target == card;
    }

    /// <summary>현재 저주 대상의 성공한 시전 또는 실제 분해를 기록한다. 다른 카드에는 영향을 주지 않는다.</summary>
    private void ResolveCurse(Card card, CurseResolution resolution)
    {
        if (IsCursed(card)) Curse.Resolution = resolution;
    }

    /// <summary>미해결 저주의 실패 효과 정보를 적용하고 실제 HP 감소량을 기록한다. HP 피해는 방어를 소비하지 않는다.</summary>
    private void ApplyCurseFailure(CardCurse curse)
    {
        switch (curse.FailureEffect)
        {
            case CurseFailureEffect.HPDamage:
                LastCurseDamage = Math.Min(Health, curse.Amount);
                Health -= LastCurseDamage;
                break;
        }
    }

    /// <summary>손패 카드의 망치 대상 조건과 한 단계 분해 공간을 확인해 거부 사유를 반환한다.</summary>
    public string HammerIssue(Card card)
    {
        if (CurrentPhase != Phase.Combat) return "플레이어 턴에만 망치를 사용할 수 있습니다";
        if (HammerUsesLeft <= 0) return "이번 턴의 망치를 이미 사용했습니다";
        if (card == null || !Hand.Contains(card)) return "손패의 조각 하나를 선택하세요";
        if (card.IsUnique) return "고유 조각은 쪼갤 수 없습니다";
        if (card.Glued == null && card.Shape > Shape.QuarterTriangle) return "이 도형은 망치 분해를 지원하지 않습니다";
        if (card.Glued != null && (card.Glued.First.Shape > Shape.QuarterTriangle || card.Glued.Second.Shape > Shape.QuarterTriangle)) return "이 접착 도형은 망치 분해를 지원하지 않습니다";
        if (card.Glued == null && card.Shape == Shape.QuarterTriangle) return "1/4 삼각형은 가장 작은 조각이라 분해할 수 없습니다";
        if (Hand.Count >= MAX_HAND) return "손패가 가득 찼습니다. 조각을 배치해 한 칸 이상 비워 주세요";
        return null;
    }

    /// <summary>조각을 한 단계 작은 두 조각으로 나눈다. 직사각형은 두 칸, 정사각형은 대각선 두 삼각형, 삼각형은 1/4 삼각형 둘이 된다.</summary>
    private static Fragment[] SplitOnce(Fragment fragment)
    {
        if (fragment.Shape == Shape.Rectangle)
        {
            IReadOnlyList<ShapeCell> cells = ShapeCells(Shape.Rectangle, fragment.Rotation);
            return new[] { new Fragment(Shape.Square, 0, fragment.X + cells[0].X, fragment.Y + cells[0].Y), new Fragment(Shape.Square, 0, fragment.X + cells[1].X, fragment.Y + cells[1].Y) };
        }
        if (fragment.Shape == Shape.Square)
            return new[] { new Fragment(Shape.Triangle, fragment.Rotation, fragment.X, fragment.Y), new Fragment(Shape.Triangle, fragment.Rotation + 2, fragment.X, fragment.Y) };
        return new[] { new Fragment(Shape.QuarterTriangle, fragment.Rotation, fragment.X, fragment.Y), new Fragment(Shape.QuarterTriangle, fragment.Rotation + 1, fragment.X, fragment.Y) };
    }

    /// <summary>강도 0~1에서 첫 분해 이후 자식의 추가 분해 확률을 반환한다.</summary>
    public static float DecompositionChance(float strength)
    {
        if (float.IsNaN(strength)) throw new ArgumentOutOfRangeException(nameof(strength));
        return Math.Max(0, Math.Min(1, strength)) * MAX_DECOMPOSITION_CHANCE;
    }

    /// <summary>가능한 원본은 반드시 한 번 나누고 강도로 추가 분해를 추첨해 혼합 도형을 반환한다. 카드·전투·덱 상태는 변경하지 않는다.</summary>
    public static Shape[] DecomposeShape(Shape shape, float strength, int maxPieces, Random random)
    {
        if (shape < Shape.Square || shape > Shape.QuarterTriangle) throw new ArgumentOutOfRangeException(nameof(shape));
        if (maxPieces < 1) throw new ArgumentOutOfRangeException(nameof(maxPieces));
        if (random == null) throw new ArgumentNullException(nameof(random));
        List<Fragment> fragments = Decompose(shape, DecompositionChance(strength), maxPieces, random);
        Shape[] shapes = new Shape[fragments.Count];
        for (int i = 0; i < fragments.Count; i++) shapes[i] = fragments[i].Shape;
        return shapes;
    }

    /// <summary>원본을 반드시 한 번 나누고 남은 분할 수 안에서 추가 분해를 추첨해 원본 기본 방향 기준 위치를 가진 조각 목록을 반환한다.</summary>
    private static List<Fragment> Decompose(Shape shape, float chance, int maxPieces, Random random)
    {
        List<Fragment> pieces = new List<Fragment>();
        Fragment root = new Fragment(shape, 0, 0, 0);
        if (shape == Shape.QuarterTriangle || maxPieces == 1) { pieces.Add(root); return pieces; }
        int splitsLeft = maxPieces - 2;
        foreach (Fragment child in SplitOnce(root)) SplitFragment(child, chance, random, pieces, ref splitsLeft);
        return pieces;
    }

    /// <summary>남은 분할 수 안에서 각 자식의 추가 분해를 추첨해 면적이 보존된 최종 조각을 누적한다.</summary>
    private static void SplitFragment(Fragment fragment, float chance, Random random, List<Fragment> pieces, ref int splitsLeft)
    {
        if (fragment.Shape == Shape.QuarterTriangle || splitsLeft == 0 || random.NextDouble() >= chance)
        {
            pieces.Add(fragment); return;
        }
        splitsLeft--;
        foreach (Fragment child in SplitOnce(fragment)) SplitFragment(child, chance, random, pieces, ref splitsLeft);
    }

    /// <summary>원본 기본 방향 기준 씰링이 쪼갠 조각 안에 있으면 그 조각의 기본 방향 기준 씰링으로 바꿔 반환한다.</summary>
    private static bool TryMapSeal(Fragment fragment, SealSpot seal, out SealSpot mapped)
    {
        IReadOnlyList<ShapeCell> cells = ShapeCells(fragment.Shape, fragment.Rotation), baseCells = ShapeCells(fragment.Shape, 0);
        for (int i = 0; i < cells.Count; i++)
        {
            if (fragment.X + cells[i].X != seal.X || fragment.Y + cells[i].Y != seal.Y || (cells[i].Mask & (1 << seal.Sector)) == 0) continue;
            mapped = new SealSpot(baseCells[i].X, baseCells[i].Y, ((seal.Sector - fragment.Rotation) % 4 + 4) % 4, seal.Seal); return true;
        }
        mapped = default; return false;
    }

    /// <summary>현재 손패에서 원본을 교체할 때 허용되는 최대 결과 수를 반환한다. 대상이 무효하면 0이다.</summary>
    public int HammerMaxPieces(Card card)
    {
        if (HammerIssue(card) != null) return 0;
        int smallestPieces = card.Glued == null ? SmallestPieceCount(card.Shape)
            : SmallestPieceCount(card.Glued.First.Shape) + SmallestPieceCount(card.Glued.Second.Shape);
        return Math.Min(smallestPieces, MAX_HAND - Hand.Count + 1);
    }

    /// <summary>기본 도형을 모두 1/4까지 나눴을 때 면적에 해당하는 최소 조각 수를 반환한다.</summary>
    private static int SmallestPieceCount(Shape shape)
    {
        return shape == Shape.Rectangle ? 8 : shape == Shape.Square ? 4 : shape == Shape.Triangle ? 2 : 1;
    }

    /// <summary>강도에 따른 분해 결과로 손패를 교체하고 망치 횟수를 소비한다. 실제 교체된 대상의 저주만 해제한다.</summary>
    public bool TryHammer(Card card, float strength, out Shape[] resultShapes)
    {
        resultShapes = Array.Empty<Shape>();
        int maxPieces = HammerMaxPieces(card);
        if (maxPieces == 0) return false;
        List<Card> results = new List<Card>();
        float chance = DecompositionChance(strength);
        if (card.Glued == null) AddHammerCards(card, Decompose(card.Shape, chance, maxPieces, _random), card.Coating, results);
        else
        {
            List<Fragment> firstPieces = new List<Fragment>(), secondPieces = new List<Fragment>();
            int splitsLeft = maxPieces - 2;
            SplitFragment(new Fragment(card.Glued.First.Shape, 0, 0, 0), chance, _random, firstPieces, ref splitsLeft);
            SplitFragment(new Fragment(card.Glued.Second.Shape, 0, 0, 0), chance, _random, secondPieces, ref splitsLeft);
            AddHammerCards(card.Glued.First, firstPieces, card.Coating, results);
            AddHammerCards(card.Glued.Second, secondPieces, card.Coating, results);
        }
        resultShapes = new Shape[results.Count];
        for (int i = 0; i < results.Count; i++) resultShapes[i] = results[i].Shape;
        HammerUsesLeft--;
        if (resultShapes.Length == 1) return true;
        int index = _hand.IndexOf(card);
        _hand.RemoveAt(index);
        _hand.InsertRange(index, results);
        ResolveCurse(card, CurseResolution.Hammer);
        return true;
    }

    /// <summary>쪼갠 조각마다 카드를 만든다. 코팅은 모든 조각에 적용하고, 씰링은 그 1/4 구역을 가진 조각에만 남긴다. 이전 효과는 가장 큰 조각 한 장에만 남기며 동률은 무작위로 고른다.</summary>
    private void AddHammerCards(Card source, IReadOnlyList<Fragment> fragments, Coating coating, List<Card> results)
    {
        int effectIndex = 0, largest = 0, ties = 0;
        if (source.Effect != Effect.None)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                int area = SmallestPieceCount(fragments[i].Shape);
                if (area > largest) { largest = area; effectIndex = i; ties = 1; }
                else if (area == largest && _random.Next(++ties) == 0) effectIndex = i;
            }
        }
        for (int i = 0; i < fragments.Count; i++)
        {
            Card card = new Card { Id = ++_nextId, Shape = fragments[i].Shape, Effect = i == effectIndex ? source.Effect : Effect.None, Coating = coating };
            foreach (SealSpot seal in source.Seals) if (TryMapSeal(fragments[i], seal, out SealSpot mapped)) card.SealList.Add(mapped);
            results.Add(card);
        }
    }

    /// <summary>접착 카드에서는 두 원본의 효과 수를 합산하고 기본 카드에서는 해당 효과 보유 수를 반환한다.</summary>
    public static int EffectCount(Card card, Effect effect)
    {
        if (effect == Effect.None) return 0;
        return card.Glued == null ? (card.Effect == effect ? 1 : 0)
            : EffectCount(card.Glued.First, effect) + EffectCount(card.Glued.Second, effect);
    }

    /// <summary>손패의 기본 카드인지 확인해 접착 선택 거부 사유를 반환한다. 특수 효과는 허용한다.</summary>
    public string GlueCardIssue(Card card)
    {
        if (card == null || !Hand.Contains(card)) return "손패의 조각을 선택하세요";
        if (card.IsUnique) return "고유 조각은 접착할 수 없습니다";
        if (card.Glued != null) return "접착된 카드는 다시 접착할 수 없습니다";
        return null;
    }

    /// <summary>두 손패 조각의 중복·겹침과 실제 삼각 구역의 변 접촉을 검사해 접착 거부 사유를 반환한다.</summary>
    public string GlueIssue(Card first, int firstRotation, Card second, int secondRotation, int relativeX, int relativeY)
    {
        if (CurrentPhase != Phase.Combat) return "플레이어 행동 중에만 접착할 수 있습니다";
        if (GlueUsesLeft <= 0) return "이번 턴의 접착을 이미 사용했습니다";
        string issue = GlueCardIssue(first) ?? GlueCardIssue(second);
        if (issue != null) return issue;
        if (first == second) return "서로 다른 조각 두 장을 선택하세요";
        if (relativeX < -4 || relativeX > 4 || relativeY < -4 || relativeY > 4) return "조각 사이에 틈이 있습니다";
        bool touching = false;
        for (int y = 0; y < ShapeHeight(first, firstRotation); y++) for (int x = 0; x < ShapeWidth(first, firstRotation); x++)
        {
            int a = CellMask(first, firstRotation, x, y);
            int b = CellMask(second, secondRotation, x - relativeX, y - relativeY);
            if ((a & b) != 0) return "두 조각이 겹칩니다";
            int adjacent = ((a << 1) | (a >> 3) | (a >> 1) | (a << 3)) & 15;
            if ((adjacent & b) != 0) touching = true;
            for (int side = 0; side < 4; side++)
            {
                if ((a & (1 << side)) == 0) continue;
                int nx = x + (side == 1 ? 1 : side == 3 ? -1 : 0);
                int ny = y + (side == 2 ? 1 : side == 0 ? -1 : 0);
                if ((CellMask(second, secondRotation, nx - relativeX, ny - relativeY) & (1 << ((side + 2) % 4))) != 0) touching = true;
            }
        }
        return touching ? null : "실제 도형의 변을 맞대세요. 꼭짓점만 닿으면 접착할 수 없습니다";
    }

    /// <summary>두 조각에 서로 다른 코팅이 있어 붙이기 전에 유지할 코팅을 골라야 하는지 반환한다.</summary>
    public static bool NeedsCoatingChoice(Card first, Card second)
    {
        return first.Coating != Coating.None && second.Coating != Coating.None && first.Coating != second.Coating;
    }

    /// <summary>두 손패 카드의 접촉을 재검증해 복합 카드로 교체하고 미해결 저주를 결과 인스턴스로 이전한다. 코팅이 서로 다르면 keptCoating이 둘 중 하나여야 한다.</summary>
    public bool TryGlue(Card first, int firstRotation, Card second, int secondRotation, int relativeX, int relativeY, Coating keptCoating = Coating.None)
    {
        if (GlueIssue(first, firstRotation, second, secondRotation, relativeX, relativeY) != null) return false;
        Coating coating = first.Coating != Coating.None ? first.Coating : second.Coating;
        if (NeedsCoatingChoice(first, second))
        {
            if (keptCoating != first.Coating && keptCoating != second.Coating) return false;
            coating = keptCoating;
        }
        GluedShape glued = new GluedShape(first, (firstRotation % 4 + 4) % 4, second, (secondRotation % 4 + 4) % 4, relativeX, relativeY);
        Card result = new Card { Id = _nextId + 1, Shape = first.Shape, Effect = Effect.None, Glued = glued, Coating = coating };
        int index = Math.Min(_hand.IndexOf(first), _hand.IndexOf(second));
        _hand.Remove(first); _hand.Remove(second); _hand.Insert(index, result);
        if (IsCursed(first) || IsCursed(second)) Curse.Target = result;
        _nextId++; GlueUsesLeft--; return true;
    }

    /// <summary>전투 임시 카드와 배치를 정리하고 보상 카드를 포함한 소유 덱을 복구해 지정 화면으로 전환한다.</summary>
    private void FinishCombat(Phase phase)
    {
        Curse = null;
        CancelTheft(); _stolen.Clear();
        foreach (Enemy enemy in Enemies) { enemy.SealedSpell = -1; enemy.PreviousSealedSpell = -1; }
        SealNotice = "";
        _hand.Clear(); _draw.Clear(); _discard.Clear();
        foreach (Board board in Boards) { board.MutablePieces.Clear(); board.Completer = null; }
        _draw.AddRange(_owned);
        CurrentPhase = phase;
    }

    /// <summary>손패 한도를 지키며 지정 수만큼 카드를 뽑는다.</summary>
    private void Draw(int count)
    {
        while (count-- > 0 && Hand.Count < MAX_HAND)
        {
            if (_draw.Count == 0) { _draw.AddRange(_discard); _discard.Clear(); Shuffle(_draw); }
            if (_draw.Count == 0) break;
            int last = _draw.Count - 1; _hand.Add(_draw[last]); _draw.RemoveAt(last);
        }
    }

    /// <summary>카드를 버림 더미로 보낸다. 절도 대상이었다면 절도를 취소한다.</summary>
    private void Discard(Card card)
    {
        if (TheftTarget == card) CancelTheft();
        _discard.Add(card);
    }

    /// <summary>현재 턴 저주를 판정·정리하고 생존 시 손패 버림과 기존 적 공격을 순서대로 처리한다.</summary>
    public void EndTurn()
    {
        if (CurrentPhase != Phase.Combat) return;
        LastCurseDamage = 0;
        CardCurse endingCurse = Curse;
        Curse = null;
        if (endingCurse != null && endingCurse.AppliedTurn == Turn && !endingCurse.IsResolved) ApplyCurseFailure(endingCurse);
        if (Health <= 0) { Health = 0; FinishCombat(Phase.Defeat); return; }
        SealNotice = "";
        foreach (Card card in Hand) Discard(card);
        _hand.Clear();
        foreach (Enemy enemy in Enemies)
        {
            if (enemy.Health <= 0) continue;
            int damage = enemy.Intent; enemy.ActionCount++; int absorbed = Math.Min(Block, damage); Block -= absorbed; Health -= damage - absorbed;
            if (Health <= 0) { Health = 0; FinishCombat(Phase.Defeat); return; }
        }
        Block = 0; RefreshSeals(); StartTurn();
    }

    /// <summary>생존 봉인자가 지정한 고유 주문 ID를 봉인 중인지 반환한다.</summary>
    public bool IsSealed(int spell)
    {
        foreach (Enemy enemy in Enemies) if (enemy.IsSealer && enemy.Health > 0 && enemy.SealedSpell == spell) return true;
        return false;
    }

    /// <summary>전투 시작과 턴 종료에 생존 봉인자의 대상을 소유 도안에서 고른다. 도안이 여럿이면 직전 대상을 제외한다.</summary>
    private void RefreshSeals()
    {
        SealNotice = "";
        foreach (Enemy enemy in Enemies)
        {
            if (!enemy.IsSealer) continue;
            enemy.SealedSpell = -1;
            if (enemy.Health <= 0 || Spells.Count == 0) continue;
            int previousIndex = Spells.IndexOf(enemy.PreviousSealedSpell);
            int index = _random.Next(Spells.Count - (Spells.Count > 1 && previousIndex >= 0 ? 1 : 0));
            if (Spells.Count > 1 && previousIndex >= 0 && index >= previousIndex) index++;
            enemy.SealedSpell = Spells[index]; enemy.PreviousSealedSpell = enemy.SealedSpell;
            SealNotice = "형상 봉인자가 주문 도안을 봉인했습니다. 피해를 주면 해제됩니다";
        }
    }

    /// <summary>실제 HP 피해를 적용하고 피해 받은 봉인자의 현재 봉인만 해제한다. 피해 0과 다른 적의 피격은 영향을 주지 않는다.</summary>
    private void DamageEnemy(Enemy enemy, int damage)
    {
        if (enemy.Health <= 0 || damage <= 0) return;
        int before = enemy.Health;
        enemy.Health -= damage;
        if (enemy == _theftOwner && enemy.Health <= 0) CancelTheft();
        if (enemy.IsSealer && enemy.Health < before && enemy.SealedSpell >= 0)
        {
            enemy.SealedSpell = -1;
            SealNotice = "형상 봉인자가 피해를 받아 봉인이 해제되었습니다!";
        }
    }

    /// <summary>제외할 조각을 뺀 판의 구역별 겹침 수를 칸·구역 순서로 반환한다.</summary>
    private int[] Coverage(Board board, Placement ignored)
    {
        int[] coverage = new int[CELL_COUNT * 4];
        foreach (Placement piece in board.Pieces)
        {
            if (piece == ignored) continue;
            for (int i = 0; i < CELL_COUNT; i++) for (int s = 0; s < 4; s++) if ((piece.Mask[i] & (1 << s)) != 0) coverage[i * 4 + s]++;
        }
        return coverage;
    }

    /// <summary>카드를 도안 기준 앵커와 방향으로 놓았을 때의 칸별 마스크를 반환한다. 범위를 벗어나거나 점토가 채울 곳이 없으면 null이다.</summary>
    private int[] CandidateMask(Board board, Card card, int x, int y, int rotation, Placement ignored)
    {
        int[] mask = new int[CELL_COUNT];
        if (card.Glued == null && card.Shape == Shape.Clay)
        {
            if (x < 0 || x >= LOCAL_SIZE || y < 0 || y >= LOCAL_SIZE) return null;
            int[] coverage = Coverage(board, ignored); int index = y * LOCAL_SIZE + x, free = 0;
            for (int s = 0; s < 4; s++) if ((SPELL_MASKS[board.Spell][index] & (1 << s)) != 0 && coverage[index * 4 + s] == 0) free |= 1 << s;
            if (free == 0) return null;
            mask[index] = free; return mask;
        }
        foreach (ShapeCell cell in CardCells(card, rotation))
        {
            int cx = x + cell.X, cy = y + cell.Y;
            if (cx < 0 || cx >= LOCAL_SIZE || cy < 0 || cy >= LOCAL_SIZE) return null;
            mask[cy * LOCAL_SIZE + cx] = cell.Mask;
        }
        return mask;
    }

    /// <summary>카드 전체를 회전한 뒤 점유하는 격자 너비를 반환한다.</summary>
    public static int ShapeWidth(Card card, int rotation)
    {
        if (card.Glued != null) return (rotation & 1) == 0 ? card.Glued.Width : card.Glued.Height;
        int width = 0;
        foreach (ShapeCell cell in ShapeCells(card.Shape, rotation)) width = Math.Max(width, cell.X + 1);
        return width;
    }

    /// <summary>카드 전체를 회전한 뒤 점유하는 격자 높이를 반환한다.</summary>
    public static int ShapeHeight(Card card, int rotation)
    {
        if (card.Glued != null) return (rotation & 1) == 0 ? card.Glued.Height : card.Glued.Width;
        int height = 0;
        foreach (ShapeCell cell in ShapeCells(card.Shape, rotation)) height = Math.Max(height, cell.Y + 1);
        return height;
    }

    /// <summary>기본·복합 카드의 회전 후 로컬 좌표에서 실제로 점유하는 삼각 구역을 반환한다.</summary>
    public static int CellMask(Card card, int rotation, int x, int y)
    {
        rotation = (rotation % 4 + 4) % 4;
        return card.Glued != null ? card.Glued.RotatedCell(rotation, x, y) : ShapeMaskAt(card.Shape, 0, 0, rotation, x, y);
    }

    /// <summary>기존 조각과 도안 영역을 고려해 배치 가능 여부를 반환한다.</summary>
    public bool CanPlace(int boardIndex, Card card, int x, int y, int rotation, Placement ignored = null)
    {
        return PlacementIssue(boardIndex, card, x, y, rotation, ignored) == null;
    }

    /// <summary>배치가 불가능한 이유를 반환하고, 가능하면 null을 반환한다. 덧칠 씰링이 있는 조각은 조각 전체를 구역마다 한 겹까지 겹칠 수 있다.</summary>
    public string PlacementIssue(int boardIndex, Card card, int x, int y, int rotation, Placement ignored = null)
    {
        if (CurrentPhase != Phase.Combat || boardIndex < 0 || boardIndex >= Boards.Count || card == null) return "지금은 배치할 수 없습니다";
        if (!Spells.Contains(Boards[boardIndex].Spell)) return "장착하지 않은 도안입니다";
        if (IsSealed(Boards[boardIndex].Spell)) return "봉인된 도안에는 조각을 놓을 수 없습니다";
        Board board = Boards[boardIndex]; int[] candidate = CandidateMask(board, card, x, y, rotation, ignored);
        if (candidate == null) return card.Glued == null && card.Shape == Shape.Clay ? "점토가 채울 빈 구역이 없습니다" : "도형이 도안 범위를 벗어납니다";
        int[] allowed = SPELL_MASKS[board.Spell];
        for (int i = 0; i < CELL_COUNT; i++) if ((candidate[i] & ~allowed[i]) != 0) return "도안 밖으로 벗어납니다";
        int[] coverage = Coverage(board, ignored);
        bool overpaint = SealCount(card, Seal.Overpaint) > 0;
        for (int i = 0; i < CELL_COUNT; i++) for (int s = 0; s < 4; s++)
        {
            if ((candidate[i] & (1 << s)) == 0) continue;
            if (overpaint && coverage[i * 4 + s] >= 2) return "덧칠은 한 겹까지만 올릴 수 있습니다";
            if (!overpaint && coverage[i * 4 + s] >= 1) return "다른 조각과 겹칩니다";
        }
        return null;
    }

    /// <summary>배치가 도안을 완성하는지 반환한다.</summary>
    public bool WouldComplete(int boardIndex, Card card, int x, int y, int rotation, Placement ignored = null)
    {
        if (!CanPlace(boardIndex, card, x, y, rotation, ignored)) return false;
        Board board = Boards[boardIndex];
        int[] filled = CandidateMask(board, card, x, y, rotation, ignored);
        foreach (Placement piece in board.Pieces)
        {
            if (piece == ignored) continue;
            for (int i = 0; i < CELL_COUNT; i++) filled[i] |= piece.Mask[i];
        }
        for (int i = 0; i < CELL_COUNT; i++) if (filled[i] != SPELL_MASKS[board.Spell][i]) return false;
        return true;
    }

    /// <summary>손패 카드를 도안의 유효한 위치에 배치한다.</summary>
    public bool Place(int board, Card card, int x, int y, int rotation)
    {
        if (!Hand.Contains(card) || !CanPlace(board, card, x, y, rotation)) return false;
        Placement placement = new Placement { Card = card, Turn = Turn, CanRefund = true };
        SetPosition(Boards[board], placement, x, y, rotation);
        _hand.Remove(card); Boards[board].MutablePieces.Add(placement); UpdateCompleter(Boards[board], placement); return true;
    }

    /// <summary>배치 위치·방향과 점유 마스크를 설정한다. 호출 전에 배치 가능 여부를 확인한다.</summary>
    private void SetPosition(Board board, Placement placement, int x, int y, int rotation)
    {
        placement.Mask = CandidateMask(board, placement.Card, x, y, rotation, placement);
        placement.X = x; placement.Y = y; placement.Rotation = placement.Card.Shape == Shape.Clay ? 0 : (rotation % 4 + 4) % 4;
    }

    /// <summary>판이 막 완성되면 그 조각을 완성 조각으로 기록하고, 미완성이 되면 기록을 지운다.</summary>
    private void UpdateCompleter(Board board, Placement changed)
    {
        if (!IsBoardComplete(board)) { board.Completer = null; return; }
        if (board.Completer == null || !board.Pieces.Contains(board.Completer)) board.Completer = changed;
    }

    /// <summary>현재 턴에 배치한 미시전 조각만 개별 이동하거나 회수할 수 있는지 반환한다.</summary>
    public bool CanMovePiece(Placement piece) { return piece != null && piece.Turn == Turn && piece.CanRefund; }

    /// <summary>배치된 조각을 비용 없이 다른 위치 또는 판으로 옮긴다.</summary>
    public bool Move(int source, Placement piece, int destination, int x, int y, int rotation)
    {
        if (source < 0 || source >= Boards.Count || !CanMovePiece(piece) || !Boards[source].Pieces.Contains(piece) || !CanPlace(destination, piece.Card, x, y, rotation, source == destination ? piece : null)) return false;
        Boards[source].MutablePieces.Remove(piece); SetPosition(Boards[destination], piece, x, y, rotation); Boards[destination].MutablePieces.Add(piece);
        UpdateCompleter(Boards[source], null); UpdateCompleter(Boards[destination], piece); return true;
    }

    /// <summary>현재 턴의 미시전 조각을 손패로 회수한다.</summary>
    public bool Recall(int board, Placement piece)
    {
        if (CurrentPhase != Phase.Combat || board < 0 || board >= Boards.Count || !CanMovePiece(piece) || Hand.Count >= MAX_HAND || !Boards[board].MutablePieces.Remove(piece)) return false;
        if (TheftTarget == piece.Card) CancelTheft();
        _hand.Add(piece.Card); UpdateCompleter(Boards[board], null); return true;
    }

    /// <summary>판의 현재 턴 조각은 회수하고 이전 턴 조각은 버린다.</summary>
    public bool Abandon(int board)
    {
        if (CurrentPhase != Phase.Combat || board < 0 || board >= Boards.Count) return false;
        Board b = Boards[board]; int current = 0; foreach (Placement p in b.Pieces) if (CanMovePiece(p)) current++;
        if (Hand.Count + current > MAX_HAND) return false;
        foreach (Placement p in b.Pieces)
        {
            if (TheftTarget == p.Card) CancelTheft();
            if (CanMovePiece(p)) { _hand.Add(p.Card); }
            else Discard(p.Card);
        }
        b.MutablePieces.Clear(); b.Completer = null; return true;
    }

    /// <summary>살아 있는 적을 단일 대상 주문의 대상으로 선택한다.</summary>
    public void SelectTarget(int index) { if (index >= 0 && index < Enemies.Count && Enemies[index].Health > 0) Target = index; }

    /// <summary>생존 도둑손이 있을 때 실제 배치 카드 중 하나를 균등 무작위로 골라 절도 대상으로 보관한다.</summary>
    public bool BeginTheft()
    {
        if (CurrentPhase != Phase.Combat || TheftTarget != null || CraftCount == 0) return false;
        Enemy owner = null;
        foreach (Enemy enemy in Enemies) if (enemy.IsThief && enemy.Health > 0) { owner = enemy; break; }
        if (owner == null) return false;
        int index = _random.Next(CraftCount);
        foreach (Board board in Boards) foreach (Placement piece in board.Pieces)
        {
            if (index-- != 0) continue;
            _theftOwner = owner; TheftTarget = piece.Card; return true;
        }
        return false;
    }

    /// <summary>현재 절도 대상이 여전히 배치되어 있는지 확인해 해당 도안 ID와 현재 배치 정보를 반환한다.</summary>
    public bool TryGetTheftPlacement(out int boardIndex, out Placement placement)
    {
        boardIndex = -1; placement = null;
        if (CurrentPhase != Phase.Combat || TheftTarget == null || _theftOwner == null || _theftOwner.Health <= 0) return false;
        for (int i = 0; i < Boards.Count; i++) foreach (Placement piece in Boards[i].Pieces)
        {
            if (piece.Card != TheftTarget) continue;
            boardIndex = i; placement = piece; return true;
        }
        return false;
    }

    /// <summary>절도 시도를 취소하며 대상 카드와 배치 횟수는 변경하지 않는다.</summary>
    public void CancelTheft() { TheftTarget = null; _theftOwner = null; }

    /// <summary>도착 시 대상을 재검증하고 배치에서 전투 내 절도 보관소로 옮긴다. 배치 횟수는 환불하지 않는다.</summary>
    public bool ResolveTheft()
    {
        int board; Placement piece;
        if (!TryGetTheftPlacement(out board, out piece)) { CancelTheft(); return false; }
        Boards[board].MutablePieces.Remove(piece); UpdateCompleter(Boards[board], null);
        _stolen.Add(piece.Card);
        CancelTheft(); return true;
    }

    /// <summary>지정 도안이 모든 삼각 구역을 빈틈없이 채웠는지 반환한다.</summary>
    public bool IsComplete(int boardIndex)
    {
        if (boardIndex < 0 || boardIndex >= Boards.Count || !Spells.Contains(boardIndex)) return false;
        return IsBoardComplete(Boards[boardIndex]);
    }

    /// <summary>판의 모든 도안 구역이 한 겹 이상 덮였는지 반환한다.</summary>
    private static bool IsBoardComplete(Board board)
    {
        int[] filled = new int[CELL_COUNT];
        foreach (Placement piece in board.Pieces) for (int i = 0; i < CELL_COUNT; i++) filled[i] |= piece.Mask[i];
        for (int i = 0; i < CELL_COUNT; i++) if (filled[i] != SPELL_MASKS[board.Spell][i]) return false;
        return true;
    }

    /// <summary>도안의 덮인 구역 수를 반환한다. 덧칠로 겹친 구역은 한 번만 센다.</summary>
    public int FilledArea(int boardIndex)
    {
        int[] filled = new int[CELL_COUNT]; int area = 0;
        foreach (Placement piece in Boards[boardIndex].Pieces) for (int i = 0; i < CELL_COUNT; i++) filled[i] |= piece.Mask[i];
        foreach (int mask in filled) area += BitCount(mask);
        return area;
    }

    /// <summary>도안의 지정 칸에 필요한 상·우·하·좌 삼각 구역을 반환한다.</summary>
    public static int PatternMask(int spell, int x, int y)
    {
        return spell >= 0 && spell < SPELL_MASKS.Length && x >= 0 && x < LOCAL_SIZE && y >= 0 && y < LOCAL_SIZE ? SPELL_MASKS[spell][y * LOCAL_SIZE + x] : 0;
    }

    /// <summary>두 조각이 변(구역 경계)을 맞대고 있는지 반환한다. 같은 칸의 이웃 구역과 옆 칸의 맞닿은 구역을 본다.</summary>
    private static bool Touches(Placement a, Placement b)
    {
        for (int y = 0; y < LOCAL_SIZE; y++) for (int x = 0; x < LOCAL_SIZE; x++)
        {
            int mask = a.CellMask(x, y);
            for (int s = 0; s < 4; s++)
            {
                if ((mask & (1 << s)) == 0) continue;
                if ((b.CellMask(x, y) & ((1 << ((s + 1) % 4)) | (1 << ((s + 3) % 4)))) != 0) return true;
                int nx = x + (s == 1 ? 1 : s == 3 ? -1 : 0), ny = y + (s == 2 ? 1 : s == 0 ? -1 : 0);
                if ((b.CellMask(nx, ny) & (1 << ((s + 2) % 4))) != 0) return true;
            }
        }
        return false;
    }

    /// <summary>도안을 지금 시전했을 때의 결과를 시전 순간의 판 상태로 계산한다. 코팅은 조각당 하나, 씰링은 붙은 개수만큼 적용한다.</summary>
    public SpellEvaluation Evaluate(int boardIndex)
    {
        Board board = Boards[boardIndex]; int spell = board.Spell;
        SpellEvaluation e = new SpellEvaluation { Kind = PATTERN_KINDS[spell], Base = PATTERN_BASE[spell] };
        int counted = board.Pieces.Count, amplify = 0, guard = 0, resonance = 0, overpaint = 0, cog = 0;
        bool pure = board.Pieces.Count > 0; Shape first = board.Pieces.Count > 0 ? board.Pieces[0].Card.Shape : Shape.Square;
        foreach (Placement p in board.Pieces)
        {
            if (p.Card.Glued != null || p.Card.Shape != first) pure = false;
            if (p.Card.Coating == Coating.Amplify) amplify++;
            else if (p.Card.Coating == Coating.Guard) guard++;
            else if (p.Card.Coating == Coating.Cog)
            {
                int touching = 0;
                foreach (Placement other in board.Pieces) if (other != p && Touches(p, other)) touching++;
                cog += Math.Min(COG_LIMIT, touching);
            }
            e.Draws += SealCount(p.Card, Seal.Refill);
            e.ShardHits += SealCount(p.Card, Seal.Shard);
            e.Recalls += SealCount(p.Card, Seal.Recall);
            overpaint += SealCount(p.Card, Seal.Overpaint);
            resonance += EffectCount(p.Card, Effect.Resonance);
        }
        bool attack = e.Kind != SpellKind.Block;
        int value = e.Base;
        if (attack && amplify > 0) { int bonus = amplify * (e.Kind == SpellKind.Scatter ? AMPLIFY_HIT_BONUS : AMPLIFY_BONUS); value += bonus; e.Terms.Add($"증폭 +{bonus}"); }
        if (attack && cog > 0) { value += cog; e.Terms.Add($"톱니 +{cog}"); }
        if (board.Completer != null && EffectCount(board.Completer.Card, Effect.Period) > 0) { int bonus = PERIOD_BONUS * EffectCount(board.Completer.Card, Effect.Period); value += bonus; e.Terms.Add($"마침표 +{bonus}"); }
        if (resonance > 0 && counted >= RESONANCE_COUNT) { value += resonance * RESONANCE_BONUS; e.Terms.Add($"공명 +{resonance * RESONANCE_BONUS}"); }
        if (overpaint > 0) { value += overpaint * OVERPAINT_BONUS; e.Terms.Add($"덧칠 +{overpaint * OVERPAINT_BONUS}"); }
        if (PATTERN_KEYWORDS[spell] == Keyword.OneStroke && counted == 1) { value += ONE_STROKE_BONUS; e.Terms.Add($"한 방 +{ONE_STROKE_BONUS}"); }
        if (PATTERN_KEYWORDS[spell] == Keyword.Purity && pure) { value += PURITY_BONUS; e.Terms.Add($"순수 +{PURITY_BONUS}"); }
        if (e.Kind == SpellKind.Scatter) e.Hits = Math.Max(1, Math.Min(MAX_HITS, counted));
        e.Value = value;
        e.Block = (e.Kind == SpellKind.Block ? value : 0) + guard * GUARD_BONUS;
        return e;
    }

    /// <summary>살아 있는 적 중 하나를 무작위로 골라 피해를 준다. 모두 쓰러졌으면 아무것도 하지 않는다.</summary>
    private void HitRandom(int damage)
    {
        int alive = 0; foreach (Enemy enemy in Enemies) if (enemy.Health > 0) alive++;
        if (alive == 0) return;
        int pick = _random.Next(alive);
        foreach (Enemy enemy in Enemies)
        {
            if (enemy.Health <= 0) continue;
            if (pick-- == 0) { DamageEnemy(enemy, damage); return; }
        }
    }

    /// <summary>완성된 주문을 지정 대상에 시전하고 효과와 승리 및 카드 소비를 처리한다.</summary>
    public bool Cast(int boardIndex, int target)
    {
        if (CurrentPhase != Phase.Combat || !IsComplete(boardIndex) || IsSealed(boardIndex)) return false;
        bool single = NeedsTarget(boardIndex);
        if (single && (target < 0 || target >= Enemies.Count || Enemies[target].Health <= 0)) return false;
        if (single) Target = target;
        Board board = Boards[boardIndex];
        SpellEvaluation e = Evaluate(boardIndex);
        Block += e.Block;
        if (e.Kind == SpellKind.Single) DamageEnemy(Enemies[Target], e.Value);
        else if (e.Kind == SpellKind.All) { foreach (Enemy enemy in Enemies) if (enemy.Health > 0) DamageEnemy(enemy, e.Value); }
        else if (e.Kind == SpellKind.Scatter) for (int i = 0; i < e.Hits; i++) HitRandom(e.Value);
        for (int i = 0; i < e.ShardHits; i++) HitRandom(SHARD_DAMAGE);
        bool alive = false; for (int i = 0; i < Enemies.Count; i++) if (Enemies[i].Health > 0) { if (!alive && Enemies[Target].Health <= 0) Target = i; alive = true; }
        List<Card> recalled = new List<Card>();
        for (int i = board.Pieces.Count - 1; i >= 0; i--)
        {
            Placement piece = board.Pieces[i];
            ResolveCurse(piece.Card, CurseResolution.Spell);
            int recalls = SealCount(piece.Card, Seal.Recall);
            for (int k = 0; k < recalls; k++) recalled.Add(CopyCard(piece.Card, false));
            Discard(piece.Card); board.MutablePieces.RemoveAt(i);
        }
        board.Completer = null;
        // 회수 복사본은 이번 전투에만 쓰는 임시 카드라 보유 덱에 넣지 않고, 손패 한도도 무시한다.
        _hand.AddRange(recalled);
        if (e.Draws > 0 && alive) Draw(e.Draws);
        if (!alive)
        {
            if (Battle != LAST_BATTLE) Health = Math.Min(MaxHealth, Health + VICTORY_HEAL);
            FinishCombat(Phase.Reward); EnterReward();
        }
        return true;
    }

    /// <summary>확정 조각 보상과 독립 확률의 도안·인장 보상을 생성하고 보상 단계로 전환한다.</summary>
    private void EnterReward()
    {
        _rewards.Clear();
        _patternRewards.Clear();
        LastAcquiredPattern = -1;
        ActiveReward = RewardKind.None;
        HasPieceReward = true;
        HasPatternReward = _random.NextDouble() < OPTIONAL_REWARD_CHANCE;
        HasSealReward = false;
        RewardSeal = HasSealReward ? (Seal)(1 + _random.Next(SEAL_KIND_COUNT)) : Seal.None;
        bool hasClay = false;
        for (int i = 0; i < REWARD_PIECE_COUNT; i++)
        {
            Card piece = RandomRewardPiece(!hasClay);
            hasClay |= piece.IsUnique;
            _rewards.Add(new Reward { Pattern = -1, Piece = piece });
        }
        for (int spell = 0; spell < PATTERN_COUNT; spell++) if (!_ownedSpells.Contains(spell)) _patternRewards.Add(new Reward { Pattern = spell });
        int patternCount = Math.Min(REWARD_PATTERN_COUNT, _patternRewards.Count);
        for (int i = 0; i < patternCount; i++)
        {
            int selected = _random.Next(i, _patternRewards.Count);
            Reward candidate = _patternRewards[i];
            _patternRewards[i] = _patternRewards[selected];
            _patternRewards[selected] = candidate;
        }
        if (_patternRewards.Count > patternCount) _patternRewards.RemoveRange(patternCount, _patternRewards.Count - patternCount);
        HasPatternReward &= _patternRewards.Count > 0;
        CurrentPhase = Phase.Reward;
    }

    /// <summary>보상용 랜덤 조각을 만든다. 도형을 고른 뒤 반반 확률로 무작위 1/4 구역에 씰링 1개 또는 코팅 1개를 붙인다. 고유 조각 점토는 아무것도 붙이지 않는다.</summary>
    private Card RandomRewardPiece(bool canBeClay)
    {
        // Clay가 Shape의 마지막 값이므로 점토를 빼려면 마지막 하나를 제외하고 뽑는다.
        Shape shape = (Shape)_random.Next(canBeClay ? SHAPE_KIND_COUNT : SHAPE_KIND_COUNT - 1);
        Card card = new Card { Shape = shape };
        if (card.IsUnique) return card;
        if (_random.NextDouble() < REWARD_SEAL_CHANCE)
        {
            List<SealSpot> sectors = new List<SealSpot>();
            foreach (ShapeCell cell in ShapeCells(shape, 0))
                for (int sector = 0; sector < 4; sector++) if ((cell.Mask & (1 << sector)) != 0) sectors.Add(new SealSpot(cell.X, cell.Y, sector, Seal.None));
            SealSpot spot = sectors[_random.Next(sectors.Count)];
            card.SealList.Add(new SealSpot(spot.X, spot.Y, spot.Sector, (Seal)(1 + _random.Next(SEAL_KIND_COUNT))));
        }
        else card.Coating = (Coating)(1 + _random.Next(COATING_KIND_COUNT));
        return card;
    }

    /// <summary>선택한 조각 또는 도안을 수령하고 목록으로 돌아간다. -1은 수령을 마친다.</summary>
    public bool ChooseReward(int index)
    {
        if (CurrentPhase != Phase.Reward) return false;
        if (index == -1) { CurrentPhase = Battle == LAST_BATTLE ? Phase.Victory : Phase.Loadout; ActiveReward = RewardKind.None; return true; }
        IReadOnlyList<Reward> choices = ActiveRewardChoices;
        if (index < 0 || index >= choices.Count) return false;
        Reward reward = choices[index];
        if (ActiveReward == RewardKind.Pattern && HasPatternReward)
        {
            if (_ownedSpells.Contains(reward.Pattern)) return false;
            _ownedSpells.Add(reward.Pattern); LastAcquiredPattern = reward.Pattern; HasPatternReward = false;
        }
        else if (ActiveReward == RewardKind.Piece && HasPieceReward)
        {
            _owned.Add(CopyCard(reward.Piece, true)); HasPieceReward = false;
        }
        else return false;
        ActiveReward = RewardKind.None;
        return true;
    }

    /// <summary>미수령 보상 종류를 연다. 후보는 다시 추첨하지 않고 유지한다.</summary>
    public bool OpenReward(RewardKind kind)
    {
        if (CurrentPhase != Phase.Reward) return false;
        bool available = kind == RewardKind.None || kind == RewardKind.Piece && HasPieceReward
            || kind == RewardKind.Pattern && HasPatternReward || kind == RewardKind.Seal && HasSealReward;
        if (!available) return false;
        ActiveReward = kind;
        return true;
    }

    public readonly struct RewardSealTarget
    {
        public Card Piece { get; }
        public SealSpot Spot { get; }

        /// <summary>인장을 붙일 원본 조각과 빈 영역을 저장한다.</summary>
        internal RewardSealTarget(Card piece, SealSpot spot) { Piece = piece; Spot = spot; }
    }

    /// <summary>소유 조각의 빈 인장 영역을 반환한다. 접착 조각은 구성 조각까지 조회한다.</summary>
    public IReadOnlyList<RewardSealTarget> RewardSealTargets(Card card)
    {
        List<RewardSealTarget> targets = new List<RewardSealTarget>();
        if (card != null && _owned.Contains(card)) CollectRewardSealTargets(card, targets);
        return targets;
    }

    /// <summary>빈 1/4 영역을 목록에 추가한다. 찰흙은 기존 규칙에 따라 제외한다.</summary>
    private static void CollectRewardSealTargets(Card card, List<RewardSealTarget> targets)
    {
        if (card.Glued != null)
        {
            CollectRewardSealTargets(card.Glued.First, targets);
            CollectRewardSealTargets(card.Glued.Second, targets);
            return;
        }
        if (card.IsUnique) return;
        foreach (ShapeCell cell in CardCells(card, 0))
            for (int sector = 0; sector < 4; sector++)
            {
                if ((cell.Mask & (1 << sector)) == 0) continue;
                bool occupied = false;
                foreach (SealSpot seal in card.Seals)
                    if (seal.X == cell.X && seal.Y == cell.Y && seal.Sector == sector) { occupied = true; break; }
                if (!occupied) targets.Add(new RewardSealTarget(card, new SealSpot(cell.X, cell.Y, sector, Seal.None)));
            }
    }

    /// <summary>선택한 빈 영역에 보상 인장을 붙이고 보상 목록으로 돌아간다. 중복 수령은 거부한다.</summary>
    public bool ClaimRewardSeal(Card card, int targetIndex)
    {
        if (CurrentPhase != Phase.Reward || ActiveReward != RewardKind.Seal || !HasSealReward) return false;
        IReadOnlyList<RewardSealTarget> targets = RewardSealTargets(card);
        if (targetIndex < 0 || targetIndex >= targets.Count) return false;
        RewardSealTarget target = targets[targetIndex];
        target.Piece.SealList.Add(new SealSpot(target.Spot.X, target.Spot.Y, target.Spot.Sector, RewardSeal));
        HasSealReward = false; ActiveReward = RewardKind.None;
        return true;
    }

    /// <summary>장착할 보유 도안을 정하고 다음 전투를 시작한다. 하나 이상 장착해야 한다.</summary>
    public bool StartNextBattle(IReadOnlyList<int> equipped)
    {
        if (CurrentPhase != Phase.Loadout || equipped.Count == 0) return false;
        foreach (int spell in equipped) if (!_ownedSpells.Contains(spell)) return false;
        _spells.Clear(); _spells.AddRange(equipped); _spells.Sort();
        StartBattle(); return true;
    }
    /// <summary>기본·복합 카드의 지정 회전에서 점유하는 칸 목록을 반환한다.</summary>
    public static IReadOnlyList<ShapeCell> CardCells(Card card, int rotation)
    {
        if (card.Glued == null) return ShapeCells(card.Shape, rotation);
        List<ShapeCell> cells = new List<ShapeCell>();
        for (int y = 0; y < ShapeHeight(card, rotation); y++) for (int x = 0; x < ShapeWidth(card, rotation); x++)
        {
            int mask = CellMask(card, rotation, x, y);
            if (mask != 0) cells.Add(new ShapeCell(x, y, mask));
        }
        return cells;
    }

}

