using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【耐寒】仕様4.8：3レーン上で「右→左」「左→右」の腕振りでレーン移動し、飛んでくる氷魔法を回避する。
  /// </summary>
  public class ColdResistMinigame : MinigameBase
  {
    public const int LaneCount = 3;

    private CharacterData _defender;

    public int CurrentLane { get; private set; } = 1; // 0=左,1=中央,2=右
    public float Score { get; private set; }

    protected override void OnMinigameStart()
    {
      _defender = PickRandomAssigned();
      CurrentLane = 1;
      Score = 0f;
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      switch (type)
      {
        case GestureType.SwipeRightToLeft:
          CurrentLane = Mathf.Max(0, CurrentLane - 1);
          break;
        case GestureType.SwipeLeftToRight:
          CurrentLane = Mathf.Min(LaneCount - 1, CurrentLane + 1);
          break;
      }
      // TODO: キャラを対応レーンへ移動させる
    }

    /// <summary>氷魔法の衝突判定側から、回避成功時に呼ばれる。</summary>
    public void RegisterDodge()
    {
      if (!IsRunning) return;
      Score += totalAttackPower;
    }

    /// <summary>氷魔法に被弾した場合はシーン側からこのメソッドを呼び、ゲームオーバーにできる（任意）。</summary>
    public void RegisterHit()
    {
      // 仕様上は明示的なゲームオーバー条件が定義されていないため、制限時間耐久のみで判定する。
      // 被弾をゲームオーバー条件にしたい場合はここで FinishAsGameOver() を呼ぶ。
    }
  }
}
