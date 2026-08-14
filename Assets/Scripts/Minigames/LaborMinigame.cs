using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【労働】仕様4.4：「腕を振り下ろす（ハンマー）」1回＝合計攻撃力分ゲージ蓄積。
  /// ゲージがMAXになったら次のゲージへ切り替え、どれだけ貯められるか計測する。
  /// </summary>
  public class LaborMinigame : MinigameBase
  {
    [SerializeField] private float _gaugeMax = 100f;

    public float CurrentGauge { get; private set; }
    public int CompletedGaugeCount { get; private set; }

    protected override void OnMinigameStart()
    {
      CurrentGauge = 0f;
      CompletedGaugeCount = 0;
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.HammerSwingDown) return;

      CurrentGauge += totalAttackPower;
      if (CurrentGauge >= _gaugeMax)
      {
        CurrentGauge -= _gaugeMax;
        CompletedGaugeCount++;
        // TODO: 修復ゲージUIの切り替え演出
      }
    }
  }
}
