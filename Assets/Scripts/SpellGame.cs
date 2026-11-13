using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>주문 조각 전투의 카드, 제작판과 턴 상태를 관리한다.</summary>
public sealed class SpellGame
{
    private const float MAX_DECOMPOSITION_CHANCE = 0.9f;
    private const int CURSE_FAILURE_AMOUNT = 10;
    private const int WEAK_CONTAMINATION_COUNT = 3;
    private const int STRONG_CONTAMINATION_COUNT = 5;
    public const int CONTAMINATION_PENALTY = 2;

    public enum Shape { Square, Triangle, Rectangle, QuarterTriangle, Diamond, Parallelogram, MediumTriangle, House, LargeTriangle, LShape, Clay }
    public enum Effect { None, Period, Resonance }
    public enum Seal { None, Recall, Shard, Refill, Overpaint }
    public enum Coating { None, Guard, Amplify, Cog }
    // 시전 중 피해가 어디서 왔는지다. 연출이 도안 타격과 파편 씰링 타격을 구분해 그리는 데 쓴다.
    public enum HitSource { Spell, Shard, Keyword }
    public enum RewardKind { None, Piece, Pattern, Seal }
    public enum Phase { Title, Combat, Reward, Loadout, Victory, Defeat, Event, Rest }
    // None은 기본 효과가 없고 키워드·씰링·코팅 효과만 쓰는 도안이다. JSON은 이름으로 저장하므로 끝에 추가했다.
    public enum SpellKind { Single, Block, All, Scatter, None }
    public enum Keyword { None, OneStroke, Mosaic, Purity }
    // 키워드 조건을 만족했을 때 받는 보상 종류다. 추가 피해는 주문 수치에 더하고, 무작위 적 피해는 별도 1타, 방어도는 시전자 방어에 더한다.
    public enum KeywordBonus { Damage, RandomDamage, Block }
    // 도안 시전 연출 프리셋이다. Auto는 효과 종류로 고르고, None은 연출이 없다. JSON은 이름으로 저장한다.
    public enum CastEffect { Auto, None, Ember, Lightning, Meteor, Slash, Strike, Buckshot, ShieldBash, Barrier, IronWall }
    public enum EnemyAction { Attack, Defend, Curse, Charge }

    public const int MAX_HAND = 7;
    public const int SHARD_DAMAGE = 2;
    // 도안 데이터에 키워드 보상 수치가 없을 때 쓰는 기본값이다.
    public const int DEFAULT_KEYWORD_BONUS = 6;
    // 도안 기준 좌표 범위의 한 변 칸 수다. 칸은 화면에 똑바로 놓인 정사각형이고 두 대각선이 1/4 구역을 나눈다.
    public const int LOCAL_SIZE = 4;
    private const int CELL_COUNT = LOCAL_SIZE * LOCAL_SIZE;
    private const int FULL_MASK = 15;
    private const int AMPLIFY_BONUS = 2;
    private const int AMPLIFY_HIT_BONUS = 1;
    private const int GUARD_BONUS = 3;
    private const int REWARD_PIECE_COUNT = 3;
    private const int REWARD_PATTERN_COUNT = 3;
    private const double REWARD_SEAL_CHANCE = 0.5;
    private const int PERIOD_BONUS = 3;
    private const int RESONANCE_BONUS = 3;
    private const int RESONANCE_COUNT = 3;
    private const int COG_LIMIT = 3;
    private const int OVERPAINT_BONUS = 2;
    private const int VICTORY_HEAL = 6;
    private const int START_HEALTH = 50;
    private const int DEVELOPER_HEALTH = 9999;
    private const int LAST_BATTLE = 5;
    private const double REST_HEAL_RATIO = 0.3;

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
        public SpellKind Kind; public int Base; public int Value; public int Hits = 1; public int Block; public int ShardHits; public int Draws; public int Recalls; public int KeywordRandomDamage; public int KeywordRandomHits;
        public readonly List<string> Terms = new List<string>();
        public int Total { get { return Kind == SpellKind.None ? 0 : Kind == SpellKind.Scatter ? Value * Hits : Value; } }
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
        public bool IsContaminated { get; internal set; }
        // 회수 씰링으로 손패에 들어온 이번 전투 임시 복사본이다. 시전에 쓰이거나 턴이 끝나 버려지면 버림 더미에 가지 않고 사라진다.
        public bool IsRecallCopy { get; internal set; }
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
        public bool IsContaminator { get; }
        public int Block { get; internal set; }
        public int Charge { get; internal set; }
        public int SealedSpell { get; internal set; } = -1;
        internal int PreviousSealedSpell { get; set; } = -1;

        public int ActionAmount { get { return _pattern[ActionCount % _pattern.Length]; } }
        public EnemyAction NextAction
        {
            get
            {
                int step = ActionCount % _pattern.Length;
                if (IsCurser) return step == 0 ? EnemyAction.Curse : step == 1 ? EnemyAction.Charge : EnemyAction.Attack;
                return (IsThief || IsContaminator) && step == 1 ? EnemyAction.Defend : EnemyAction.Attack;
            }
        }
        public int Intent { get { return NextAction == EnemyAction.Attack ? ActionAmount + Charge : 0; } }
        public int ContaminationCount { get { return IsContaminator && NextAction == EnemyAction.Attack ? (ActionCount % _pattern.Length == 0 ? WEAK_CONTAMINATION_COUNT : STRONG_CONTAMINATION_COUNT) : 0; } }
        public ReadOnlyCollection<int> Pattern { get { return _patternView; } }
        internal int ActionCount;

        /// <summary>이름, 체력, 공격 패턴과 신규 적 능력 여부로 적의 전투 상태를 초기화한다.</summary>
        internal Enemy(string name, int health, int[] pattern, bool isSealer = false, bool isThief = false, bool isCurser = false, bool isContaminator = false)
        {
            Name = name; Health = health; MaxHealth = health; _pattern = pattern; _patternView = Array.AsReadOnly(pattern); IsSealer = isSealer; IsThief = isThief; IsCurser = isCurser;
            IsContaminator = isContaminator;
        }

        /// <summary>다음 행동 수치와 현재 방어·충전·패시브 상태를 행동 예고 설명으로 반환한다.</summary>
        public string DescribeIntent()
        {
            string text;
            switch (NextAction)
            {
                case EnemyAction.Defend: text = $"방어 쌓기 · 방어 {ActionAmount} 획득"; break;
                case EnemyAction.Curse: text = $"조각 저주 · 다음 손패 1장 / 미해결 HP -{CURSE_FAILURE_AMOUNT}"; break;
                case EnemyAction.Charge: text = $"힘 모으기 · 다음 공격 +{ActionAmount}"; break;
                default:
                    string strength = IsThief || IsContaminator ? (ActionCount % _pattern.Length == 0 ? "약한 " : "강한 ") : IsCurser ? "강한 " : "";
                    text = $"{strength}{(IsContaminator ? "오염 공격" : "공격")} · 피해 {Intent}";
                    if (ContaminationCount > 0) text += $" / 오염 {ContaminationCount}장";
                    break;
            }
            if (Block > 0) text += $"\n현재 방어 {Block}";
            if (Charge > 0) text += $"\n충전 +{Charge}";
            if (IsThief) text += "\n패시브 · 약 15초마다 조각 절도";
            return text;
        }
    }

    private static readonly Dictionary<int, Fragment[]> SPLIT_CACHE = new Dictionary<int, Fragment[]>();
    private static readonly int SHAPE_KIND_COUNT = Enum.GetValues(typeof(Shape)).Length;
    private static readonly int SEAL_KIND_COUNT = Enum.GetValues(typeof(Seal)).Length - 1;
    private static readonly int COATING_KIND_COUNT = Enum.GetValues(typeof(Coating)).Length - 1;
    private static readonly Card[] DEBUG_PIECES = CreateDebugPieces();
    // 점토가 놓일 때 변할 수 있는 1/4 구역 4개(1칸 넓이) 모양의 모든 방향이다. 정사각형, 삼각형, 45도 직사각형, 평행사변형 순으로 우선한다.
    private static readonly ShapeCell[][] CLAY_FORMS = CreateClayForms();
    // 아래 데이터 필드는 JSON을 읽는 static 생성자에서 순서대로 채운다.
    private static readonly SpellPatternData[] PATTERNS;
    private static readonly int[][] SPELL_MASKS;
    private static readonly ShapeCell[][] BASE_SHAPES;
    private static readonly string[] SHAPE_NAMES;
    private static readonly bool[] SHAPE_MIRRORED;
    private static readonly Shape[] REWARD_SHAPES;
    private static readonly int[] STARTER_COUNTS;
    private static readonly int[] DECOMPOSITION_PRIORITIES;
    private static readonly ShapeCell[][][] SHAPE_ORIENTATIONS;

    private readonly Random _random = new Random();
    private readonly List<Card> _owned = new List<Card>();
    // 마지막 시전에서 실제로 피해를 준 적 번호를 타격 순서대로 기록한다. 연출이 죽은 적을 치지 않도록 쓴다.
    private readonly List<int> _lastCastHits = new List<int>();
    private readonly List<HitSource> _lastCastHitSources = new List<HitSource>();
    private bool _isRecordingHits;
    private HitSource _hitSource;
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
    private bool _hasUsedWorkshop;
    private bool _hasRested;
    private bool _hasContaminatorEncounter;
    private bool _hasPendingCurse;
    private readonly List<Card> _stolen = new List<Card>();
    private Enemy _theftOwner;

    public int LastContaminationAdded { get; private set; }
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
    public IReadOnlyList<int> LastCastHits { get { return _lastCastHits; } }
    // LastCastHits와 같은 순서로 각 타격의 출처를 담는다.
    public IReadOnlyList<HitSource> LastCastHitSources { get { return _lastCastHitSources; } }
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
    public bool IsWorkshopEvent { get { return (CurrentPhase == Phase.Event && Battle == 2) || CurrentPhase == Phase.Rest; } }
    public bool IsSealEvent { get { return CurrentPhase == Phase.Event && Battle == 3; } }
    public bool CanUseWorkshop { get { return IsWorkshopEvent && !_hasUsedWorkshop; } }
    public bool CanRest { get { return CurrentPhase == Phase.Rest && !_hasRested; } }
    public int HammerUsesLeft { get { return CanUseWorkshop ? 1 : 0; } }
    public int GlueUsesLeft { get { return CanUseWorkshop ? 1 : 0; } }
    public string SealNotice { get; private set; } = "";
    public int Target { get; private set; }
    public int LastAcquiredPattern { get; private set; } = -1;
    public int DrawCount { get { return _draw.Count; } }
    public int DiscardCount { get { return _discard.Count; } }
    public int CraftCount { get { int count = 0; foreach (Board board in Boards) count += board.Pieces.Count; return count; } }
    public static int PatternCount { get { return PATTERNS.Length; } }

    /// <summary>Resources의 도안·조각 JSON을 읽어 도안 정보, 도안 칸, 도형 칸·이름·회전 방식, 보상 도형과 네 방향 칸을 채운다. 데이터 오류는 예외로 전달한다.</summary>
    static SpellGame()
    {
        PATTERNS = SpellDataStore.LoadPatterns().Patterns.ToArray();
        SPELL_MASKS = new int[PATTERNS.Length][];
        for (int i = 0; i < PATTERNS.Length; i++)
        {
            SPELL_MASKS[i] = new int[CELL_COUNT];
            foreach (SpellCellData cell in PATTERNS[i].Cells) SPELL_MASKS[i][cell.Y * LOCAL_SIZE + cell.X] = cell.Mask;
        }
        BASE_SHAPES = new ShapeCell[SHAPE_KIND_COUNT][];
        SHAPE_NAMES = new string[SHAPE_KIND_COUNT];
        SHAPE_MIRRORED = new bool[SHAPE_KIND_COUNT];
        bool[] isReward = new bool[SHAPE_KIND_COUNT];
        STARTER_COUNTS = new int[SHAPE_KIND_COUNT];
        DECOMPOSITION_PRIORITIES = new int[SHAPE_KIND_COUNT];
        foreach (SpellPieceData piece in SpellDataStore.LoadPieces().Pieces)
        {
            STARTER_COUNTS[(int)piece.Shape] = piece.StarterCount;
            DECOMPOSITION_PRIORITIES[(int)piece.Shape] = piece.DecompositionPriority;
            int shape = (int)piece.Shape;
            BASE_SHAPES[shape] = new ShapeCell[piece.Cells.Count];
            for (int i = 0; i < piece.Cells.Count; i++) BASE_SHAPES[shape][i] = new ShapeCell(piece.Cells[i].X, piece.Cells[i].Y, piece.Cells[i].Mask);
            SHAPE_NAMES[shape] = piece.Name;
            SHAPE_MIRRORED[shape] = piece.Rotation == SpellPieceData.RotationMode.MirrorAlternate;
            isReward[shape] = piece.IsInRewardPool;
        }
        List<Shape> rewards = new List<Shape>();
        for (int shape = 0; shape < SHAPE_KIND_COUNT; shape++) if (isReward[shape]) rewards.Add((Shape)shape);
        REWARD_SHAPES = rewards.ToArray();
        SHAPE_ORIENTATIONS = CreateOrientations();
    }

    /// <summary>카드, 적, 도안과 제작판의 읽기 전용 목록을 연결한다.</summary>
    public SpellGame()
    {
        Hand = _hand.AsReadOnly(); Enemies = _enemies.AsReadOnly(); Spells = _spells.AsReadOnly(); OwnedSpells = _ownedSpells.AsReadOnly();
        Boards = Array.AsReadOnly(_boards); Rewards = _rewards.AsReadOnly();
    }

    /// <summary>도안 수만큼 빈 제작판을 만든다.</summary>
    private static Board[] CreateBoards()
    {
        Board[] boards = new Board[PatternCount];
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
            Card card = new Card { Shape = Shape.Square }; card.SealList.Add(new SealSpot(0, 0, 1, (Seal)seal)); pieces.Add(card);
        }
        for (int coating = 1; coating <= COATING_KIND_COUNT; coating++) pieces.Add(new Card { Shape = Shape.Square, Coating = (Coating)coating });
        pieces.Add(new Card { Shape = Shape.QuarterTriangle, Effect = Effect.Period });
        pieces.Add(new Card { Shape = Shape.Triangle, Effect = Effect.Resonance });
        return pieces.ToArray();
    }

    /// <summary>점토가 변할 수 있는 1칸 넓이 모양들의 회전·뒤집기 방향을 중복 없이 만든다.</summary>
    private static ShapeCell[][] CreateClayForms()
    {
        ShapeCell[][] bases = {
            new[] { new ShapeCell(0, 0, 15) },
            new[] { new ShapeCell(0, 0, 12), new ShapeCell(0, 1, 9) },
            new[] { new ShapeCell(0, 0, 2), new ShapeCell(1, 0, 10), new ShapeCell(2, 0, 8) },
            new[] { new ShapeCell(0, 0, 6), new ShapeCell(1, 0, 9) },
        };
        List<ShapeCell[]> forms = new List<ShapeCell[]>(); HashSet<string> seen = new HashSet<string>();
        foreach (ShapeCell[] form in bases)
            for (int mirror = 0; mirror < 2; mirror++) for (int rotation = 0; rotation < 4; rotation++)
            {
                ShapeCell[] cells = Rotate(mirror == 0 ? form : Mirror(form), rotation);
                List<string> keys = new List<string>();
                foreach (ShapeCell cell in cells) keys.Add(cell.X + "," + cell.Y + "," + cell.Mask);
                keys.Sort();
                if (seen.Add(string.Join("|", keys))) forms.Add(cells);
            }
        return forms.ToArray();
    }

    /// <summary>모든 도형의 네 방향 칸 목록을 미리 계산한다. 뒤집기 회전 도형은 뒤집힌 두 방향을 회전 순서에 포함한다.</summary>
    private static ShapeCell[][][] CreateOrientations()
    {
        ShapeCell[][][] result = new ShapeCell[BASE_SHAPES.Length][][];
        for (int shape = 0; shape < BASE_SHAPES.Length; shape++)
        {
            result[shape] = new ShapeCell[4][];
            for (int rotation = 0; rotation < 4; rotation++) result[shape][rotation] = OrientCells(BASE_SHAPES[shape], SHAPE_MIRRORED[shape], rotation);
        }
        return result;
    }

    /// <summary>기본 방향 칸 목록을 지정 방향으로 바꾼 새 목록을 반환한다. isMirrored면 2·3 방향에 좌우를 뒤집은 모양을 쓴다. 왼쪽 위가 (0, 0)이다.</summary>
    public static ShapeCell[] OrientCells(IReadOnlyList<ShapeCell> baseCells, bool isMirrored, int rotation)
    {
        rotation = (rotation % 4 + 4) % 4;
        ShapeCell[] cells = new ShapeCell[baseCells.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = baseCells[i];
        if (!isMirrored) return Rotate(cells, rotation);
        return Rotate(rotation < 2 ? cells : Mirror(cells), rotation % 2);
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

    /// <summary>기본 방향의 구역 번호를 도형의 지정 방향 구역 번호로 바꾼다. 뒤집기 회전 도형의 2·3 방향은 좌우를 뒤집은 뒤 돌린다.</summary>
    private static int RotateSector(Shape shape, int rotation, int sector)
    {
        rotation = (rotation % 4 + 4) % 4;
        if (SHAPE_MIRRORED[(int)shape] && rotation >= 2) { sector = sector == 1 ? 3 : sector == 3 ? 1 : sector; rotation -= 2; }
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
    public static string PatternName(int spell) { return PATTERNS[spell].Name; }

    /// <summary>도안 JSON의 식별자를 반환한다.</summary>
    public static string PatternId(int spell) { return PATTERNS[spell].Id; }

    /// <summary>도안의 기본 효과 종류를 반환한다.</summary>
    public static SpellKind PatternKind(int spell) { return PATTERNS[spell].Kind; }

    /// <summary>도안의 특수 키워드를 반환한다.</summary>
    public static Keyword PatternKeyword(int spell) { return PATTERNS[spell].Keyword; }

    /// <summary>도안의 기본 수치를 반환한다. 다타 도안은 타격 하나의 피해다.</summary>
    public static int PatternBase(int spell) { return PATTERNS[spell].BaseValue; }

    /// <summary>장착 화면 공유 판에서 도안의 기본 좌상단 격자 좌표와 회전(0~3)을 반환한다.</summary>
    public static void PatternLayout(int spell, out int x, out int y, out int rotation)
    {
        x = PATTERNS[spell].LayoutX; y = PATTERNS[spell].LayoutY; rotation = PATTERNS[spell].LayoutRotation;
    }

    /// <summary>도안 표시 색을 "#RRGGBB" 형식 문자열로 반환한다.</summary>
    public static string PatternColorCode(int spell) { return PATTERNS[spell].Color; }

    /// <summary>새 게임 시작 시 기본으로 보유·장착하는 도안인지 반환한다.</summary>
    public static bool IsStarterPattern(int spell) { return PATTERNS[spell].IsStarter; }

    /// <summary>도형의 표시 이름을 반환한다.</summary>
    public static string ShapeName(Shape shape) { return SHAPE_NAMES[(int)shape]; }

    /// <summary>단일 대상을 골라야 하는 도안인지 반환한다.</summary>
    public static bool NeedsTarget(int spell) { return spell >= 0 && spell < PatternCount && PATTERNS[spell].Kind == SpellKind.Single; }

    /// <summary>도안의 기본 효과와 키워드를 한 줄 설명으로 반환한다.</summary>
    public static string PatternDescription(int spell) { return SpellDataStore.PatternDescription(PATTERNS[spell]); }

    /// <summary>도안의 "이름 - 기본 효과\n태그: 보상" 표기를 반환한다.</summary>
    public static string PatternTitle(int spell) { return SpellDataStore.PatternTitle(PATTERNS[spell]); }

    /// <summary>도안 키워드 태그의 발동 조건 설명을 반환한다. 태그가 없으면 빈 문자열이다.</summary>
    public static string PatternKeywordExplanation(int spell) { return SpellDataStore.KeywordExplanation(PATTERNS[spell]); }

    /// <summary>도안의 시전 연출을 반환한다. Auto면 효과 종류(단일 타격, 전체 유성, 방어 방벽, 산탄 산탄, 없음 연출 없음)로 고른다.</summary>
    public static CastEffect PatternCastEffect(int spell)
    {
        CastEffect effect = PATTERNS[spell].CastEffect;
        if (effect != CastEffect.Auto) return effect;
        switch (PATTERNS[spell].Kind)
        {
            case SpellKind.Single: return CastEffect.Strike;
            case SpellKind.All: return CastEffect.Meteor;
            case SpellKind.Block: return CastEffect.Barrier;
            case SpellKind.Scatter: return CastEffect.Buckshot;
            default: return CastEffect.None;
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
        return $"{card.Shape}:{card.Effect}:{card.Coating}:{card.IsContaminated}:{string.Join(";", seals)}";
    }

    /// <summary>지정한 도안을 지금 시전했을 때의 공격 피해 합을 반환한다. 방어 도안은 0이다.</summary>
    public int GetSpellDamage(int boardIndex)
    {
        if (boardIndex < 0 || boardIndex >= Boards.Count || PATTERNS[boardIndex].Kind == SpellKind.Block || PATTERNS[boardIndex].Kind == SpellKind.None) return 0;
        return Evaluate(boardIndex).Total;
    }

    /// <summary>개발자 모드와 테스트 적 선택으로 새 게임의 체력·덱·도안을 초기화하고 첫 전투를 시작한다.</summary>
    public void StartGame(bool isDeveloperMode = false, bool withSealer = false, bool withThief = false, bool withCurser = false, bool withContaminator = false)
    {
        _hasSealerEncounter = withSealer;
        _hasThiefEncounter = withThief;
        _hasCurserEncounter = withCurser;
        _hasContaminatorEncounter = withContaminator;
        IsDeveloperMode = isDeveloperMode; MaxHealth = isDeveloperMode ? DEVELOPER_HEALTH : START_HEALTH;
        _owned.Clear(); _spells.Clear(); _ownedSpells.Clear(); _nextId = 0; Health = MaxHealth; Battle = 0; LastAcquiredPattern = -1;
        _hasUsedWorkshop = false;
        _hasRested = false;
        for (int i = 0; i < PatternCount; i++)
        {
            if (!isDeveloperMode && !PATTERNS[i].IsStarter) continue;
            _ownedSpells.Add(i); _spells.Add(i);
        }
        // 기본 덱은 조각 데이터(pieces.json)의 도형별 starterCount 장수로 만든다.
        for (int shape = 0; shape < STARTER_COUNTS.Length; shape++)
            for (int i = 0; i < STARTER_COUNTS[shape]; i++) AddCard((Shape)shape, Effect.None);
        StartBattle();
    }

    /// <summary>현재 게임을 종료하고 타이틀 화면 상태로 전환한다.</summary>
    public void ReturnToTitle() { FinishCombat(Phase.Title); }

    /// <summary>개별 ID를 가진 카드를 소유 목록에 추가한다.</summary>
    private void AddCard(Shape shape, Effect effect)
    {
        _owned.Add(new Card { Id = ++_nextId, Shape = shape, Effect = effect });
    }

    /// <summary>원본 도형·효과·오염·코팅·씰링과 접착 구성을 새 ID로 복사한다. keepRecall이 false면 회수 씰링만 뺀다.</summary>
    private Card CopyCard(Card source, bool keepRecall)
    {
        Card copy = new Card { Id = ++_nextId, Shape = source.Shape, Effect = source.Effect, Coating = source.Coating, IsContaminated = source.IsContaminated };
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

    /// <summary>전투 임시 카드를 제외한 도형과 유효한 씰링·코팅을 복사해 보유 덱과 손패에 추가한다.</summary>
    public bool AddDebugCard(Card source)
    {
        if (CurrentPhase != Phase.Combat || source == null || source.Glued != null || !Enum.IsDefined(typeof(Shape), source.Shape)
            || !Enum.IsDefined(typeof(Coating), source.Coating) || source.Effect != Effect.None) return false;
        if (source.IsUnique && (source.Coating != Coating.None || source.Seals.Count > 0)) return false;
        HashSet<int> used = new HashSet<int>();
        foreach (SealSpot seal in source.Seals)
        {
            if (seal.Seal == Seal.None || !Enum.IsDefined(typeof(Seal), seal.Seal) || seal.Sector < 0 || seal.Sector >= 4) return false;
            bool occupied = false;
            foreach (ShapeCell cell in ShapeCells(source.Shape, 0))
                if (cell.X == seal.X && cell.Y == seal.Y && (cell.Mask & (1 << seal.Sector)) != 0) { occupied = true; break; }
            if (!occupied || !used.Add((seal.Y * LOCAL_SIZE + seal.X) * 4 + seal.Sector)) return false;
        }
        if (source.IsContaminated) return false;
        Card card = CopyCard(source, true);
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
        if (CurrentPhase != Phase.Combat || spell < 0 || spell >= PatternCount || _spells.Contains(spell)) return false;
        if (!_ownedSpells.Contains(spell)) _ownedSpells.Add(spell);
        _spells.Add(spell);
        return true;
    }

    /// <summary>다음 전투의 적과 덱을 준비하고 첫 턴을 시작한다.</summary>
    private void StartBattle()
    {
        Curse = null; LastCurseDamage = 0;
        _hasPendingCurse = false;
        LastContaminationAdded = 0;
        CancelTheft(); _stolen.Clear();
        Battle++; Turn = 0; Block = 0; Target = 0; _enemies.Clear(); _hand.Clear(); _discard.Clear(); _draw.Clear();
        foreach (Board board in Boards) { board.MutablePieces.Clear(); board.Completer = null; }
        _draw.AddRange(_owned); Shuffle(_draw);
        GuaranteeOpeningShape(Shape.Square, _draw.Count - 1); GuaranteeOpeningShape(Shape.QuarterTriangle, _draw.Count - 2);
        if (IsDeveloperMode)
        {
            for (int i = 0; i < 3; i++) _enemies.Add(new Enemy("잉크 하수인", DEVELOPER_HEALTH, new[] { 4 }));
        }
        else if (Battle == 1) { AddEnemy(0); AddEnemy(0); }
        else if (Battle == 2)
        {
            _enemies.Add(new Enemy("도둑손", 12, new[] { 4, 5, 10 }, isThief: true));
            AddEnemy(1);
        }
        else if (Battle == 3)
        {
            AddEnemy(1); AddEnemy(1);
            _enemies.Add(new Enemy("조각 저주술사", 12, new[] { 0, 5, 4 }, isCurser: true));
        }
        else if (Battle == 4)
        {
            AddEnemy(1); AddEnemy(1);
            _enemies.Add(new Enemy("형상 봉인자", 12, new[] { 4 }, isSealer: true));
        }
        else if (Battle == LAST_BATTLE)
        {
            AddEnemy(2);
            _enemies.Add(new Enemy("오염술사", 12, new[] { 5, 5, 10 }, isContaminator: true));
            _enemies.Add(new Enemy("도둑손", 12, new[] { 4, 5, 10 }, isThief: true));
        }
        if (_hasSealerEncounter && Battle == 1) _enemies.Add(new Enemy("형상 봉인자", 12, new[] { 4 }, true));
        if (_hasThiefEncounter && Battle == 1) _enemies.Add(new Enemy("도둑손", 12, new[] { 4, 5, 10 }, false, true));
        if (_hasCurserEncounter && Battle == 1) _enemies.Add(new Enemy("조각 저주술사", 12, new[] { 0, 5, 4 }, false, false, true));
        if (_hasContaminatorEncounter && Battle == 1) _enemies.Add(new Enemy("오염술사", 12, new[] { 5, 5, 10 }, isContaminator: true));
        CurrentPhase = Phase.Combat; StartTurn();
        RefreshSeals();
    }

    /// <summary>적 종류에 따라 체력과 공격 패턴을 설정한다.</summary>
    private void AddEnemy(int kind)
    {
        _enemies.Add(kind == 0 ? new Enemy("잉크 하수인", 12, new[] { 4 }) : kind == 1 ? new Enemy("거대 잉크", 26, new[] { 2, 8 }) : new Enemy("골렘", 60, new[] { 4, 7, 12 }));
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

    /// <summary>턴을 증가시키고 손패를 준비한 뒤 직전 저주 행동이 예약한 카드 한 장만 지정한다.</summary>
    private void StartTurn()
    {
        Turn++; Draw(5);
        ApplyCardCurse();
    }

    /// <summary>실행된 저주 행동의 예약을 한 번 소비하여 새 손패 한 장을 저주한다. 손패가 비어 있으면 생략한다.</summary>
    private void ApplyCardCurse()
    {
        if (!_hasPendingCurse) return;
        _hasPendingCurse = false;
        Curse = null;
        if (Hand.Count == 0) return;
        Curse = new CardCurse(Hand[_random.Next(Hand.Count)], Turn, CurseFailureEffect.HPDamage, CURSE_FAILURE_AMOUNT);
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

    /// <summary>첫 이벤트의 작업 가능 여부와 소유 조각의 분해 조건을 확인해 거부 사유를 반환한다.</summary>
    public string HammerIssue(Card card)
    {
        if (!IsWorkshopEvent) return "이벤트 또는 휴식에서만 분해할 수 있습니다";
        if (!CanUseWorkshop) return "이번 이벤트의 작업을 이미 완료했습니다";
        if (card == null || !_owned.Contains(card)) return "덱에서 분해할 조각 하나를 선택하세요";
        if (card.IsUnique) return "고유 조각은 쪼갤 수 없습니다";
        int firstSplit = card.Glued != null ? 2 : SplitOnce(new Fragment(card.Shape, 0, 0, 0)).Length;
        if (firstSplit == 0) return "현재 도형으로 빈틈없이 나눌 수 없는 최소 조각입니다";
        return null;
    }

    /// <summary>등록 도형의 실제 영역에서 분해 조합을 계산·캐시하고 원본의 방향·위치를 적용한 자식 목록을 반환한다.</summary>
    private static Fragment[] SplitOnce(Fragment fragment)
    {
        if (fragment.Shape == Shape.Clay) return Array.Empty<Fragment>();
        int key = (int)fragment.Shape * 4 + fragment.Rotation;
        if (!SPLIT_CACHE.TryGetValue(key, out Fragment[] split))
        {
            ulong[][] masks = new ulong[SHAPE_KIND_COUNT][];
            for (int shape = 0; shape < masks.Length; shape++)
            {
                masks[shape] = new ulong[4];
                for (int rotation = 0; rotation < 4; rotation++) masks[shape][rotation] = ShapeBits((Shape)shape, rotation);
            }
            SpellShapePartitioner.Part[] parts = SpellShapePartitioner.Split(masks[(int)fragment.Shape][fragment.Rotation], masks, LOCAL_SIZE, (int)Shape.Clay, DECOMPOSITION_PRIORITIES);
            split = new Fragment[parts.Length];
            for (int i = 0; i < parts.Length; i++) split[i] = new Fragment((Shape)parts[i].Shape, parts[i].Rotation, parts[i].X, parts[i].Y);
            SPLIT_CACHE.Add(key, split);
        }
        Fragment[] result = new Fragment[split.Length];
        for (int i = 0; i < result.Length; i++) result[i] = new Fragment(split[i].Shape, split[i].Rotation, fragment.X + split[i].X, fragment.Y + split[i].Y);
        return result;
    }

    /// <summary>현재 도형·방향의 점유 구역을 4×4칸 비트맵으로 반환한다.</summary>
    private static ulong ShapeBits(Shape shape, int rotation)
    {
        ulong result = 0;
        foreach (ShapeCell cell in ShapeCells(shape, rotation)) result |= (ulong)cell.Mask << ((cell.Y * LOCAL_SIZE + cell.X) * 4);
        return result;
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
        if (maxPieces < 1) throw new ArgumentOutOfRangeException(nameof(maxPieces));
        if (random == null) throw new ArgumentNullException(nameof(random));
        List<Fragment> fragments = Decompose(shape, DecompositionChance(strength), maxPieces, random);
        Shape[] shapes = new Shape[fragments.Count];
        for (int i = 0; i < fragments.Count; i++) shapes[i] = fragments[i].Shape;
        return shapes;
    }

    /// <summary>원본을 반드시 한 번 나누고 남은 결과 수 안에서 추가 분해를 추첨해 원본 기본 방향 기준 위치를 가진 조각 목록을 반환한다. 나눌 수 없거나 상한이 부족하면 원본만 반환한다.</summary>
    private static List<Fragment> Decompose(Shape shape, float chance, int maxPieces, Random random)
    {
        List<Fragment> pieces = new List<Fragment>();
        Fragment root = new Fragment(shape, 0, 0, 0);
        Fragment[] children = SplitOnce(root);
        if (children.Length == 0 || children.Length > maxPieces) { pieces.Add(root); return pieces; }
        int extraLeft = maxPieces - children.Length;
        foreach (Fragment child in children) SplitFragment(child, chance, random, pieces, ref extraLeft);
        return pieces;
    }

    /// <summary>남은 추가 결과 수 안에서 각 자식의 추가 분해를 추첨해 면적이 보존된 최종 조각을 누적한다. 한 번 나눌 때 늘어나는 장수만큼 extraLeft를 줄인다.</summary>
    private static void SplitFragment(Fragment fragment, float chance, Random random, List<Fragment> pieces, ref int extraLeft)
    {
        Fragment[] children = SplitOnce(fragment);
        if (children.Length == 0 || children.Length - 1 > extraLeft || random.NextDouble() >= chance)
        {
            pieces.Add(fragment); return;
        }
        extraLeft -= children.Length - 1;
        foreach (Fragment child in children) SplitFragment(child, chance, random, pieces, ref extraLeft);
    }

    /// <summary>원본 기본 방향 기준 씰링이 쪼갠 조각 안에 있으면 그 조각의 기본 방향 기준 씰링으로 바꿔 반환한다.</summary>
    private static bool TryMapSeal(Fragment fragment, SealSpot seal, out SealSpot mapped)
    {
        IReadOnlyList<ShapeCell> cells = ShapeCells(fragment.Shape, fragment.Rotation), baseCells = ShapeCells(fragment.Shape, 0);
        for (int i = 0; i < cells.Count; i++)
        {
            if (fragment.X + cells[i].X != seal.X || fragment.Y + cells[i].Y != seal.Y || (cells[i].Mask & (1 << seal.Sector)) == 0) continue;
            for (int sector = 0; sector < 4; sector++)
                if (RotateSector(fragment.Shape, fragment.Rotation, sector) == seal.Sector)
                {
                    mapped = new SealSpot(baseCells[i].X, baseCells[i].Y, sector, seal.Seal); return true;
                }
        }
        mapped = default; return false;
    }

    /// <summary>소유 조각을 최소 단위까지 분해할 때 가능한 최대 결과 수를 반환한다. 작업이 무효하면 0이다.</summary>
    public int HammerMaxPieces(Card card)
    {
        if (HammerIssue(card) != null) return 0;
        return SmallestPieceCount(card);
    }

    private static int SmallestPieceCount(Card card)
    {
        return card.Glued == null ? SmallestPieceCount(card.Shape)
            : SmallestPieceCount(card.Glued.First) + SmallestPieceCount(card.Glued.Second);
    }

    /// <summary>현재 도형 데이터의 공통 분해 규칙을 끝까지 적용했을 때의 결과 장수를 반환한다.</summary>
    private static int SmallestPieceCount(Shape shape)
    {
        Fragment[] parts = SplitOnce(new Fragment(shape, 0, 0, 0));
        if (parts.Length == 0) return 1;
        int count = 0;
        foreach (Fragment part in parts) count += SmallestPieceCount(part.Shape);
        return count;
    }

    /// <summary>강도에 따른 분해 결과로 덱 원본을 영구 교체하고 이벤트 작업 기회를 소모한다.</summary>
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
            int extraLeft = maxPieces - 2;
            SplitHammerCard(card.Glued.First, chance, card.Coating, results, ref extraLeft);
            SplitHammerCard(card.Glued.Second, chance, card.Coating, results, ref extraLeft);
        }
        resultShapes = new Shape[results.Count];
        for (int i = 0; i < results.Count; i++) resultShapes[i] = results[i].Shape;
        _hasUsedWorkshop = true;
        if (resultShapes.Length == 1) return true;
        int index = _owned.IndexOf(card);
        _owned.RemoveAt(index);
        _owned.InsertRange(index, results);
        return true;
    }

    /// <summary>쪼갠 조각마다 카드를 만든다. 코팅은 모든 조각에 적용하고, 씰링은 그 1/4 구역을 가진 조각에만 남긴다. 이전 효과는 가장 큰 조각 한 장에만 남기며 동률은 무작위로 고른다.</summary>
    private void SplitHammerCard(Card source, float chance, Coating coating, List<Card> results, ref int extraLeft)
    {
        if (source.Glued != null)
        {
            if (extraLeft == 0 || _random.NextDouble() >= chance)
            {
                Card copy = CopyCard(source, true);
                copy.Coating = coating;
                results.Add(copy);
                return;
            }
            extraLeft--;
            SplitHammerCard(source.Glued.First, chance, coating, results, ref extraLeft);
            SplitHammerCard(source.Glued.Second, chance, coating, results, ref extraLeft);
            return;
        }
        List<Fragment> fragments = new List<Fragment>();
        SplitFragment(new Fragment(source.Shape, 0, 0, 0), chance, _random, fragments, ref extraLeft);
        AddHammerCards(source, fragments, coating, results);
    }

    private void AddHammerCards(Card source, IReadOnlyList<Fragment> fragments, Coating coating, List<Card> results)
    {
        int effectIndex = 0, largest = 0, ties = 0;
        if (source.Effect != Effect.None)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                int area = ShapeArea(fragments[i].Shape);
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

    /// <summary>이벤트·휴식에서 접착할 소유 조각의 거부 사유를 반환한다. 오염·고유·이미 접착된 카드는 제외한다.</summary>
    public string GlueCardIssue(Card card)
    {
        if (!IsWorkshopEvent) return "이벤트 또는 휴식에서만 접착할 수 있습니다";
        if (!CanUseWorkshop) return "이번 이벤트의 작업을 이미 완료했습니다";
        if (card == null || !_owned.Contains(card)) return "덱에서 조각을 선택하세요";
        if (card.IsContaminated) return "오염 조각은 이번 프로토타입에서 접착할 수 없습니다";
        if (card.IsUnique) return "고유 조각은 접착할 수 없습니다";
        return null;
    }

    /// <summary>덱의 두 조각의 중복·겹침과 실제 삼각 구역의 변 접촉을 검사해 접착 거부 사유를 반환한다.</summary>
    public string GlueIssue(Card first, int firstRotation, Card second, int secondRotation, int relativeX, int relativeY)
    {
        string issue = GlueCardIssue(first) ?? GlueCardIssue(second);
        if (issue != null) return issue;
        if (first == second) return "서로 다른 조각 두 장을 선택하세요";
        if ((long)relativeX > ShapeWidth(first, firstRotation) || (long)relativeX < -ShapeWidth(second, secondRotation)
            || (long)relativeY > ShapeHeight(first, firstRotation) || (long)relativeY < -ShapeHeight(second, secondRotation)) return "조각 사이에 틈이 있습니다";
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

    /// <summary>덱의 두 조각을 배치·회전을 보존한 접착 카드 한 장으로 영구 교체하고 작업 기회를 소모한다. 서로 다른 코팅은 keptCoating으로 선택한다.</summary>
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
        int index = Math.Min(_owned.IndexOf(first), _owned.IndexOf(second));
        _owned.Remove(first); _owned.Remove(second); _owned.Insert(index, result);
        _hasUsedWorkshop = true;
        _nextId++; return true;
    }

    /// <summary>전투 임시 카드와 배치를 정리하고 보상 카드를 포함한 소유 덱을 복구해 지정 화면으로 전환한다.</summary>
    private void FinishCombat(Phase phase)
    {
        Curse = null; _hasPendingCurse = false;
        CancelTheft(); _stolen.Clear();
        foreach (Enemy enemy in Enemies) { enemy.SealedSpell = -1; enemy.PreviousSealedSpell = -1; enemy.Block = 0; enemy.Charge = 0; enemy.ActionCount = 0; }
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

    /// <summary>카드를 버림 더미로 보낸다. 절도 목표 소멸은 다음 목표 조회에서 처리한다.</summary>
    private void Discard(Card card)
    {
        // 회수 복사본은 덱에 쌓이지 않도록 버리는 순간 사라진다.
        if (card.IsRecallCopy) return;
        _discard.Add(card);
    }

    /// <summary>미해결 저주를 판정한 뒤 생존 적의 공격·방어·저주 예약·충전을 하나씩 처리하고 각 행동 인덱스를 진행한다.</summary>
    public void EndTurn()
    {
        if (CurrentPhase != Phase.Combat) return;
        LastCurseDamage = 0;
        LastContaminationAdded = 0;
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
            switch (enemy.NextAction)
            {
                case EnemyAction.Defend: enemy.Block += enemy.ActionAmount; break;
                case EnemyAction.Curse: _hasPendingCurse = true; break;
                case EnemyAction.Charge: enemy.Charge = enemy.ActionAmount; break;
                case EnemyAction.Attack:
                    int damage = enemy.Intent;
                    int absorbed = Math.Min(Block, damage);
                    Block -= absorbed; Health -= damage - absorbed;
                    if (enemy.ContaminationCount > 0) AddContamination(enemy.ContaminationCount);
                    enemy.Charge = 0;
                    break;
            }
            enemy.ActionCount++;
            if (Health <= 0) { Health = 0; FinishCombat(Phase.Defeat); return; }
        }
        Block = 0; RefreshSeals(); StartTurn();
    }

    /// <summary>지정 수량의 고유 ID를 가진 전투 한정 오염 1/4 삼각형을 드로우에 추가하고 전체를 섞어 생성 수를 기록한다.</summary>
    private void AddContamination(int count)
    {
        for (int i = 0; i < count; i++)
            _draw.Add(new Card { Id = ++_nextId, Shape = Shape.QuarterTriangle, IsContaminated = true });
        Shuffle(_draw);
        LastContaminationAdded += count;
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

    /// <summary>적 방어부터 소모하고 남은 HP 피해를 적용한다. 사망 시 충전을 정리하고 실제 HP 피해를 받은 봉인자의 봉인만 해제한다.</summary>
    private void DamageEnemy(Enemy enemy, int damage)
    {
        if (enemy.Health <= 0 || damage <= 0) return;
        if (_isRecordingHits) { _lastCastHits.Add(_enemies.IndexOf(enemy)); _lastCastHitSources.Add(_hitSource); }
        int absorbed = Math.Min(enemy.Block, damage);
        enemy.Block -= absorbed; damage -= absorbed;
        int before = enemy.Health;
        enemy.Health -= damage;
        if (enemy == _theftOwner && enemy.Health < before) CancelTheft();
        if (enemy.Health <= 0) { enemy.Block = 0; enemy.Charge = 0; }
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
        if (card.Glued == null && card.Shape == Shape.Clay) return ClayMask(board, x, y, ignored);
        foreach (ShapeCell cell in CardCells(card, rotation))
        {
            int cx = x + cell.X, cy = y + cell.Y;
            if (cx < 0 || cx >= LOCAL_SIZE || cy < 0 || cy >= LOCAL_SIZE) return null;
            mask[cy * LOCAL_SIZE + cx] = cell.Mask;
        }
        return mask;
    }

    /// <summary>점토를 도안 칸 (x, y)에 놓을 때 그 칸을 덮으면서 도안의 빈 구역에만 들어가는 1칸 넓이 모양의 마스크를 고른다. 도안을 완성하는 모양, 가리킨 칸을 많이 채우는 모양, CLAY_FORMS 순서 순으로 우선한다. 들어갈 모양이 없으면 null이다.</summary>
    private int[] ClayMask(Board board, int x, int y, Placement ignored)
    {
        if (x < 0 || x >= LOCAL_SIZE || y < 0 || y >= LOCAL_SIZE) return null;
        int[] allowed = SPELL_MASKS[board.Spell], coverage = Coverage(board, ignored), filled = new int[CELL_COUNT];
        foreach (Placement piece in board.Pieces)
        {
            if (piece == ignored) continue;
            for (int i = 0; i < CELL_COUNT; i++) filled[i] |= piece.Mask[i];
        }
        int[] best = null; int bestScore = -1;
        foreach (ShapeCell[] form in CLAY_FORMS)
            foreach (ShapeCell pivot in form)
            {
                int ox = x - pivot.X, oy = y - pivot.Y; int[] mask = new int[CELL_COUNT]; bool fits = true;
                foreach (ShapeCell cell in form)
                {
                    int cx = ox + cell.X, cy = oy + cell.Y, index = cy * LOCAL_SIZE + cx;
                    if (cx < 0 || cx >= LOCAL_SIZE || cy < 0 || cy >= LOCAL_SIZE || (cell.Mask & ~allowed[index]) != 0) { fits = false; break; }
                    for (int s = 0; s < 4 && fits; s++) if ((cell.Mask & (1 << s)) != 0 && coverage[index * 4 + s] > 0) fits = false;
                    if (!fits) break;
                    mask[index] = cell.Mask;
                }
                if (!fits) continue;
                bool completes = true;
                for (int i = 0; i < CELL_COUNT; i++) if ((filled[i] | mask[i]) != allowed[i]) completes = false;
                int score = (completes ? 100 : 0) + BitCount(pivot.Mask);
                if (score > bestScore) { best = mask; bestScore = score; }
            }
        return best ?? ClayGapMask(allowed, coverage, x, y);
    }

    /// <summary>1칸 모양이 들어가지 않을 때, 칸 (x, y)에 닿은 빈 구역 덩어리 중 1칸(4구역) 이하인 가장 큰 덩어리를 그대로 채우는 마스크를 반환한다. 없으면 null이다.</summary>
    private static int[] ClayGapMask(int[] allowed, int[] coverage, int x, int y)
    {
        bool IsEmpty(int cx, int cy, int s)
        {
            if (cx < 0 || cx >= LOCAL_SIZE || cy < 0 || cy >= LOCAL_SIZE) return false;
            int index = cy * LOCAL_SIZE + cx;
            return (allowed[index] & (1 << s)) != 0 && coverage[index * 4 + s] == 0;
        }
        int[] best = null; int bestCount = 0;
        HashSet<int> visited = new HashSet<int>();
        for (int start = 0; start < 4; start++)
        {
            int startKey = (y * LOCAL_SIZE + x) * 4 + start;
            if (!IsEmpty(x, y, start) || visited.Contains(startKey)) continue;
            // 같은 칸의 이웃 구역과 변을 맞댄 옆 칸 구역으로 이어지는 빈 구역 덩어리를 모은다.
            List<int> component = new List<int>(); Queue<int> queue = new Queue<int>();
            queue.Enqueue(startKey); visited.Add(startKey);
            while (queue.Count > 0)
            {
                int key = queue.Dequeue(); component.Add(key);
                int cell = key / 4, s = key % 4, cx = cell % LOCAL_SIZE, cy = cell / LOCAL_SIZE;
                int ax = cx + (s == 1 ? 1 : s == 3 ? -1 : 0), ay = cy + (s == 2 ? 1 : s == 0 ? -1 : 0);
                int[][] neighbors = { new[] { cx, cy, (s + 1) % 4 }, new[] { cx, cy, (s + 3) % 4 }, new[] { ax, ay, (s + 2) % 4 } };
                foreach (int[] n in neighbors)
                {
                    if (!IsEmpty(n[0], n[1], n[2])) continue;
                    int next = (n[1] * LOCAL_SIZE + n[0]) * 4 + n[2];
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            if (component.Count > 4 || component.Count <= bestCount) continue;
            int[] mask = new int[CELL_COUNT];
            foreach (int key in component) mask[key / 4] |= 1 << (key % 4);
            best = mask; bestCount = component.Count;
        }
        return best;
    }

    /// <summary>순수 판정에 쓰는 조각의 도형이다. 점토는 놓인 모양과 같은 기본 도형(회전·뒤집기 포함)으로 보고, 맞는 도형이 없으면 점토로 둔다.</summary>
    private static Shape PurityShape(Placement placement)
    {
        Card card = placement.Card;
        if (card.Glued != null || card.Shape != Shape.Clay) return card.Shape;
        string placed = NormalizedKey(placement.Mask);
        for (int shape = 0; shape < BASE_SHAPES.Length; shape++)
        {
            if (shape == (int)Shape.Clay || BASE_SHAPES[shape] == null) continue;
            for (int mirror = 0; mirror < 2; mirror++) for (int rotation = 0; rotation < 4; rotation++)
            {
                ShapeCell[] cells = Rotate(mirror == 0 ? BASE_SHAPES[shape] : Mirror(BASE_SHAPES[shape]), rotation);
                int minX = int.MaxValue, minY = int.MaxValue;
                foreach (ShapeCell cell in cells) { minX = Math.Min(minX, cell.X); minY = Math.Min(minY, cell.Y); }
                List<string> keys = new List<string>();
                foreach (ShapeCell cell in cells) keys.Add((cell.X - minX) + "," + (cell.Y - minY) + "," + cell.Mask);
                keys.Sort();
                if (string.Join("|", keys) == placed) return (Shape)shape;
            }
        }
        return Shape.Clay;
    }

    /// <summary>도안 칸 마스크를 왼쪽 위가 (0, 0)이 되도록 옮긴 "x,y,mask" 정렬 문자열로 만든다.</summary>
    private static string NormalizedKey(int[] mask)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        for (int i = 0; i < mask.Length; i++) if (mask[i] != 0) { minX = Math.Min(minX, i % LOCAL_SIZE); minY = Math.Min(minY, i / LOCAL_SIZE); }
        List<string> keys = new List<string>();
        for (int i = 0; i < mask.Length; i++) if (mask[i] != 0) keys.Add((i % LOCAL_SIZE - minX) + "," + (i / LOCAL_SIZE - minY) + "," + mask[i]);
        keys.Sort();
        return string.Join("|", keys);
    }

    /// <summary>카드를 도안 기준 앵커와 방향으로 놓았을 때의 칸별 마스크 사본을 반환한다. 점토는 실제로 변할 모양이다. 놓을 수 없으면 null이다.</summary>
    public int[] PreviewMask(int boardIndex, Card card, int x, int y, int rotation, Placement ignored = null)
    {
        if (!CanPlace(boardIndex, card, x, y, rotation, ignored)) return null;
        return CandidateMask(Boards[boardIndex], card, x, y, rotation, ignored);
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

    public bool HasActiveTheft { get { return CurrentPhase == Phase.Combat && _theftOwner != null && _theftOwner.Health > 0; } }

    /// <summary>현재 배치를 반환한다. 목표 소멸 시 다른 배치 조각을 고르고, 조각이 없으면 접근을 취소한다.</summary>
    public bool TryGetTheftPlacement(out int boardIndex, out Placement placement)
    {
        boardIndex = -1; placement = null;
        if (!HasActiveTheft) return false;
        for (int i = 0; i < Boards.Count; i++) foreach (Placement piece in Boards[i].Pieces)
        {
            if (piece.Card != TheftTarget) continue;
            boardIndex = i; placement = piece; return true;
        }
        if (CraftCount > 0)
        {
            int index = _random.Next(CraftCount);
            for (int i = 0; i < Boards.Count; i++) foreach (Placement piece in Boards[i].Pieces)
            {
                if (index-- != 0) continue;
                TheftTarget = piece.Card; boardIndex = i; placement = piece; return true;
            }
        }
        CancelTheft(); return false;
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

    /// <summary>지정 도안의 코팅·씰링·키워드 계산 후 참여 오염 카드마다 공격·방어를 2 낮춰 최소 0인 결과를 반환한다. 산탄은 타격별로 낮추고 파편·드로우·회수는 유지한다.</summary>
    public SpellEvaluation Evaluate(int boardIndex)
    {
        Board board = Boards[boardIndex]; int spell = board.Spell;
        SpellEvaluation e = new SpellEvaluation { Kind = PATTERNS[spell].Kind, Base = PATTERNS[spell].BaseValue };
        int counted = board.Pieces.Count, amplify = 0, guard = 0, resonance = 0, overpaint = 0, cog = 0, contamination = 0;
        // 점토는 놓인 모양에 맞는 도형으로 순수를 판정한다.
        bool pure = board.Pieces.Count > 0; Shape first = board.Pieces.Count > 0 ? PurityShape(board.Pieces[0]) : Shape.Square;
        foreach (Placement p in board.Pieces)
        {
            if (p.Card.IsContaminated) contamination += CONTAMINATION_PENALTY;
            if (p.Card.Glued != null || PurityShape(p) != first) pure = false;
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
        bool attack = e.Kind != SpellKind.Block && e.Kind != SpellKind.None;
        int value = e.Base;
        if (attack && amplify > 0) { int bonus = amplify * (e.Kind == SpellKind.Scatter ? AMPLIFY_HIT_BONUS : AMPLIFY_BONUS); value += bonus; e.Terms.Add($"증폭 +{bonus}"); }
        if (attack && cog > 0) { value += cog; e.Terms.Add($"톱니 +{cog}"); }
        if (board.Completer != null && EffectCount(board.Completer.Card, Effect.Period) > 0) { int bonus = PERIOD_BONUS * EffectCount(board.Completer.Card, Effect.Period); value += bonus; e.Terms.Add($"마침표 +{bonus}"); }
        if (resonance > 0 && counted >= RESONANCE_COUNT) { value += resonance * RESONANCE_BONUS; e.Terms.Add($"공명 +{resonance * RESONANCE_BONUS}"); }
        if (overpaint > 0) { value += overpaint * OVERPAINT_BONUS; e.Terms.Add($"덧칠 +{overpaint * OVERPAINT_BONUS}"); }
        SpellPatternData data = PATTERNS[spell];
        // 모자이크는 조각 1장 이상이면 항상 발동하고 보상을 조각 수만큼 받는다. 한 방·순수는 조건을 만족하면 1번 받는다.
        bool isMosaic = data.Keyword == Keyword.Mosaic;
        bool keywordMet = (data.Keyword == Keyword.OneStroke && counted == 1) || (data.Keyword == Keyword.Purity && pure) || (isMosaic && counted >= 1);
        int keywordBlock = 0, times = isMosaic ? counted : 1;
        if (keywordMet && data.KeywordBonusValue > 0)
        {
            string name = isMosaic ? "모자이크" : data.Keyword == Keyword.OneStroke ? "한 방" : "순수";
            string amount = isMosaic ? $"{data.KeywordBonusValue}×{times}" : $"{data.KeywordBonusValue}";
            if (data.KeywordBonus == KeywordBonus.Damage) { value += data.KeywordBonusValue * times; e.Terms.Add($"{name} +{amount}"); }
            else if (data.KeywordBonus == KeywordBonus.RandomDamage) { e.KeywordRandomDamage = data.KeywordBonusValue; e.KeywordRandomHits = times; }
            else keywordBlock = data.KeywordBonusValue * times;
        }
        // 산탄은 조각 수와 관계없이 도안에 정한 횟수만큼 타격한다.
        if (e.Kind == SpellKind.Scatter) e.Hits = Math.Max(1, data.Hits);
        // 기본 효과가 없는 도안은 주문 수치에 더하는 항목도 적용할 곳이 없어 버린다. 무작위 적 피해·방어도·파편·보충·회수는 그대로 받는다.
        if (e.Kind == SpellKind.None) { value = 0; e.Base = 0; e.Terms.Clear(); }
        e.Value = value;
        e.Block = (e.Kind == SpellKind.Block ? value : 0) + guard * GUARD_BONUS + keywordBlock;
        if (contamination > 0)
        {
            e.Value = Math.Max(0, e.Value - contamination);
            e.Block = Math.Max(0, e.Block - contamination);
            e.Terms.Add($"오염 -{contamination} (하한 0)");
        }
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
        _lastCastHits.Clear(); _lastCastHitSources.Clear(); _isRecordingHits = true; _hitSource = HitSource.Spell;
        if (e.Kind == SpellKind.Single) DamageEnemy(Enemies[Target], e.Value);
        else if (e.Kind == SpellKind.All) { foreach (Enemy enemy in Enemies) if (enemy.Health > 0) DamageEnemy(enemy, e.Value); }
        // 산탄 타격은 매번 그 순간 살아 있는 적 중에서 고르므로, 앞 타격으로 쓰러진 적은 다음 타격 대상이 아니다.
        else if (e.Kind == SpellKind.Scatter) for (int i = 0; i < e.Hits; i++) HitRandom(e.Value);
        _hitSource = HitSource.Shard;
        for (int i = 0; i < e.ShardHits; i++) HitRandom(SHARD_DAMAGE);
        _hitSource = HitSource.Keyword;
        for (int i = 0; i < e.KeywordRandomHits && e.KeywordRandomDamage > 0; i++) HitRandom(e.KeywordRandomDamage);
        _isRecordingHits = false;
        bool alive = false; for (int i = 0; i < Enemies.Count; i++) if (Enemies[i].Health > 0) { if (!alive && Enemies[Target].Health <= 0) Target = i; alive = true; }
        List<Card> recalled = new List<Card>();
        for (int i = board.Pieces.Count - 1; i >= 0; i--)
        {
            Placement piece = board.Pieces[i];
            ResolveCurse(piece.Card, CurseResolution.Spell);
            int recalls = SealCount(piece.Card, Seal.Recall);
            for (int k = 0; k < recalls; k++) { Card copy = CopyCard(piece.Card, false); copy.IsRecallCopy = true; recalled.Add(copy); }
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

    /// <summary>조각 보상과 전투 2회마다 제공할 도안 후보를 생성하고 보상 단계로 전환한다. 인장 보상은 비활성 상태를 유지한다.</summary>
    private void EnterReward()
    {
        _rewards.Clear();
        _patternRewards.Clear();
        LastAcquiredPattern = -1;
        ActiveReward = RewardKind.None;
        HasPieceReward = true;
        HasPatternReward = Battle % 2 == 0;
        HasSealReward = false;
        RewardSeal = HasSealReward ? (Seal)(1 + _random.Next(SEAL_KIND_COUNT)) : Seal.None;
        bool hasClay = false;
        for (int i = 0; i < REWARD_PIECE_COUNT; i++)
        {
            Card piece = RandomRewardPiece(!hasClay);
            hasClay |= piece.IsUnique;
            _rewards.Add(new Reward { Pattern = -1, Piece = piece });
        }
        for (int spell = 0; spell < PatternCount; spell++) if (!_ownedSpells.Contains(spell) && PATTERNS[spell].IsInRewardPool) _patternRewards.Add(new Reward { Pattern = spell });
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
        // 점토를 뺄 때는 보상 도형 목록에서 점토를 제외한 칸 중에서 뽑는다.
        int clayIndex = canBeClay ? -1 : Array.IndexOf(REWARD_SHAPES, Shape.Clay);
        int pick = _random.Next(REWARD_SHAPES.Length - (clayIndex >= 0 ? 1 : 0));
        if (clayIndex >= 0 && pick >= clayIndex) pick++;
        Shape shape = REWARD_SHAPES[pick];
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
        if (index == -1)
        {
            CurrentPhase = Battle == LAST_BATTLE ? Phase.Victory : Phase.Loadout;
            HasSealReward = false;
            RewardSeal = Seal.None;
            ActiveReward = RewardKind.None;
            return true;
        }
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
        if (IsSealEvent)
        {
            if (kind != RewardKind.None && (kind != RewardKind.Seal || !HasSealReward)) return false;
            ActiveReward = kind;
            return true;
        }
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
        public SealSpot DisplaySpot { get; }

        /// <summary>인장을 붙일 원본 조각과 빈 영역을 저장한다.</summary>
        internal RewardSealTarget(Card piece, SealSpot spot, SealSpot displaySpot) { Piece = piece; Spot = spot; DisplaySpot = displaySpot; }
    }

    /// <summary>소유 조각의 빈 인장 영역을 반환한다. 접착 조각은 구성 조각까지 조회한다.</summary>
    public IReadOnlyList<RewardSealTarget> RewardSealTargets(Card card)
    {
        List<RewardSealTarget> targets = new List<RewardSealTarget>();
        if (card != null && _owned.Contains(card)) CollectRewardSealTargets(card, targets, spot => spot);
        return targets;
    }

    /// <summary>빈 1/4 영역을 목록에 추가한다. 찰흙은 기존 규칙에 따라 제외한다.</summary>
    private static void CollectRewardSealTargets(Card card, List<RewardSealTarget> targets, Func<SealSpot, SealSpot> project)
    {
        if (card.Glued != null)
        {
            GluedShape glued = card.Glued;
            int left = Math.Min(0, glued.RelativeX), top = Math.Min(0, glued.RelativeY);
            CollectRewardSealTargets(glued.First, targets, spot =>
            {
                SealSpot rotated = RotateSealSpot(glued.First, glued.FirstRotation, spot);
                return project(new SealSpot(rotated.X - left, rotated.Y - top, rotated.Sector, rotated.Seal));
            });
            CollectRewardSealTargets(glued.Second, targets, spot =>
            {
                SealSpot rotated = RotateSealSpot(glued.Second, glued.SecondRotation, spot);
                return project(new SealSpot(rotated.X + glued.RelativeX - left, rotated.Y + glued.RelativeY - top, rotated.Sector, rotated.Seal));
            });
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
                if (!occupied)
                {
                    SealSpot spot = new SealSpot(cell.X, cell.Y, sector, Seal.None);
                    targets.Add(new RewardSealTarget(card, spot, project(spot)));
                }
            }
    }

    /// <summary>원본 조각 영역을 카드 회전에 맞춰 변환해 접착 카드 표시 좌표 계산에 사용한다.</summary>
    private static SealSpot RotateSealSpot(Card card, int rotation, SealSpot spot)
    {
        if (card.Glued == null)
        {
            IReadOnlyList<ShapeCell> original = ShapeCells(card.Shape, 0), rotated = ShapeCells(card.Shape, rotation);
            for (int i = 0; i < original.Count; i++)
                if (original[i].X == spot.X && original[i].Y == spot.Y)
                    return new SealSpot(rotated[i].X, rotated[i].Y, RotateSector(card.Shape, rotation, spot.Sector), spot.Seal);
        }
        int x = spot.X, y = spot.Y, width = ShapeWidth(card, 0), height = ShapeHeight(card, 0);
        for (int r = 0; r < rotation; r++) { int nx = height - 1 - y; y = x; x = nx; int w = width; width = height; height = w; }
        return new SealSpot(x, y, (spot.Sector + rotation) % 4, spot.Seal);
    }

    /// <summary>선택한 빈 영역에 보상 인장을 붙이고 보상 목록으로 돌아간다. 중복 수령은 거부한다.</summary>
    public bool ClaimRewardSeal(Card card, int targetIndex)
    {
        if ((CurrentPhase != Phase.Reward && !IsSealEvent) || ActiveReward != RewardKind.Seal || !HasSealReward) return false;
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
        if (Battle == 2 || Battle == 3 || Battle == 4)
        {
            CurrentPhase = Battle == 4 ? Phase.Rest : Phase.Event;
            _hasUsedWorkshop = false;
            _hasRested = false;
            HasSealReward = IsSealEvent;
            RewardSeal = HasSealReward ? (Seal)(1 + _random.Next(SEAL_KIND_COUNT)) : Seal.None;
            ActiveReward = RewardKind.None;
            return true;
        }
        StartBattle(); return true;
    }

    /// <summary>휴식 시 회복할 최대 체력 30%를 소수점 이하 버림으로 반환한다.</summary>
    public int RestHealAmount { get { return (int)(MaxHealth * REST_HEAL_RATIO); } }

    /// <summary>휴식 선택을 한 번 적용해 최대 체력의 30%를 상한까지 회복한다. 작업 선택 화면은 유지한다.</summary>
    public bool TryRest()
    {
        if (!CanRest) return false;
        Health = Math.Min(MaxHealth, Health + RestHealAmount);
        _hasRested = true;
        return true;
    }

    /// <summary>이벤트 또는 휴식 선택을 마치고 도안 배치로 이동한다.</summary>
    public bool ContinueEncounter()
    {
        if (CurrentPhase != Phase.Event && CurrentPhase != Phase.Rest) return false;
        HasSealReward = false;
        ActiveReward = RewardKind.None;
        StartBattle();
        return true;
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

