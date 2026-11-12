using System.Collections.Generic;

using Newtonsoft.Json;

/// <summary>조각 도형 하나의 표시 이름, 기본 방향 칸, 회전 방식, 보상 후보 여부와 기본 덱 장수다. Shape enum 값마다 하나씩 둔다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellPieceData
{
    /// <summary>네 방향을 만드는 방식이다. MirrorAlternate는 2·3 방향에 좌우를 뒤집은 모양을 쓴다.</summary>
    public enum RotationMode { Rotate, MirrorAlternate }

    [JsonProperty("shape", Required = Required.Always)] public SpellGame.Shape Shape { get; set; }
    [JsonProperty("name", Required = Required.Always)] public string Name { get; set; }
    [JsonProperty("cells", Required = Required.Always)] public List<SpellCellData> Cells { get; set; } = new List<SpellCellData>();
    [JsonProperty("rotation", Required = Required.Always)] public RotationMode Rotation { get; set; }
    [JsonProperty("isInRewardPool", Required = Required.Always)] public bool IsInRewardPool { get; set; }
    // 새 게임 시작 덱에 넣을 장수다. 0이면 기본 덱에 넣지 않는다.
    [JsonProperty("starterCount")] public int StarterCount { get; set; }
}
