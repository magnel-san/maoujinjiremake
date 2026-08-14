using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【戦闘】仕様4.6：「両拳を交互に突き出す（パンチ）」で衝撃波を出し、迫る勇者たちと乱闘する。
  /// </summary>
  public class CombatMinigame : MinigameBase
  {
    [SerializeField] private float _heroApproachSpeed = 0.05f; // 0(遠い)〜1(目前)
    [SerializeField] private float _heroPushBackPerHit = 0.12f;

    public float TotalDamage { get; private set; }
    public float HeroProximity01 { get; private set; }

    protected override void OnMinigameStart()
    {
      TotalDamage = 0f;
      HeroProximity01 = 0f;
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 + _heroApproachSpeed * deltaTime);
      if (HeroProximity01 >= 1f)
      {
        FinishAsGameOver();
      }
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.AlternatingPunch) return;

      // TODO: 波状の衝撃波を前進させる演出
      TotalDamage += totalAttackPower;
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 - _heroPushBackPerHit);
    }
  }
}
