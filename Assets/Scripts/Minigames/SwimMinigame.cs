using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>【遊泳】仕様4.1：「手を横に払う」で波を召喚し、正面から接近する勇者を押し戻す。</summary>
  public class SwimMinigame : MinigameBase
  {
    [SerializeField] private float _heroApproachSpeed = 0.05f; // 0(遠い)〜1(目前)
    [SerializeField] private float _heroPushBackPerHit = 0.15f;

    public float TotalDamage { get; private set; }
    public float HeroProximity01 { get; private set; } // 0=遠い, 1=目前

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
      if (type != GestureType.SwipeSideways) return;

      // TODO: 波オブジェクトを召喚する演出
      TotalDamage += totalAttackPower;
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 - _heroPushBackPerHit);
    }
  }
}
