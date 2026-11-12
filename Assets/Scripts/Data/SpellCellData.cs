using Newtonsoft.Json;

/// <summary>도안·조각 JSON의 칸 하나다. 좌표는 SpellGame.LOCAL_SIZE 격자 기준이고 Mask는 위1·오른쪽2·아래4·왼쪽8 구역 비트다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellCellData
{
    [JsonProperty("x", Required = Required.Always)] public int X { get; set; }
    [JsonProperty("y", Required = Required.Always)] public int Y { get; set; }
    [JsonProperty("mask", Required = Required.Always)] public int Mask { get; set; }
}
