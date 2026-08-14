using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【俊足】仕様4.3：勇者と100mトラックで競争。「両腕を振る」のたびに前進する。
  /// カウントダウンや勇者との競争演出はTODO、進行距離の計算のみ実装する。
  /// </summary>
  public class SprintMinigame : MinigameBase
  {
    [SerializeField] private float _trackLength = 100f;
    [SerializeField] private float _distancePerMotionFactor = 0.5f;

    private CharacterData _runner;

    public float DistanceTravelled { get; private set; }

    protected override void OnMinigameStart()
    {
      _runner = PickRandomAssigned();
      DistanceTravelled = 0f;
      // TODO: 「3、2、1、GO」カウントダウン演出
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      if (DistanceTravelled >= _trackLength)
      {
        FinishAsDefenseSuccess();
      }
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.ArmSwingBoth) return;

      DistanceTravelled = Mathf.Min(_trackLength, DistanceTravelled + totalAttackPower * _distancePerMotionFactor);
      // TODO: キャラの前進アニメーション・移動を適用する
    }
  }
}
