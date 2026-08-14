using DemonLordHR.HandTracking;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【耐熱】仕様4.9：「胸の前でぐるぐるバルブを回す」でマグマを放出する。
  /// GestureRecognizerのValveSpinは検出フレームごとに発火するため、1発火＝1周分として扱う。
  /// </summary>
  public class HeatResistMinigame : MinigameBase
  {
    public float TotalMagma { get; private set; }

    protected override void OnMinigameStart()
    {
      TotalMagma = 0f;
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.ValveSpin) return;

      TotalMagma += totalAttackPower;
      // TODO: マグマ放出エフェクトを再生する
    }
  }
}
