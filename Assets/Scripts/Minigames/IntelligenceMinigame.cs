using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【知性】仕様4.7：ランダムに光るノードをポインターで順番に指してつなぎ、
  /// 「両手を前で合わせる」で魔法陣を起動する。
  /// </summary>
  public class IntelligenceMinigame : MinigameBase
  {
    [SerializeField] private int _requiredNodeCount = 5;

    public float Score { get; private set; }
    public int CompletionCount { get; private set; }
    public int ConnectedNodeCount { get; private set; }

    protected override void OnMinigameStart()
    {
      Score = 0f;
      CompletionCount = 0;
      ConnectedNodeCount = 0;
      // TODO: 未完成の巨大な魔法陣を表示し、光るノードをランダム配置する
    }

    /// <summary>シーン上の<see cref="MagicCircleNode"/>がポインターで指された際に呼ばれる。</summary>
    public void RegisterNodeConnected()
    {
      if (!IsRunning) return;
      ConnectedNodeCount = Mathf.Min(_requiredNodeCount, ConnectedNodeCount + 1);
      // TODO: ノード間に線を描画する
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.HandsTogether) return;
      if (ConnectedNodeCount < _requiredNodeCount) return;

      Score += totalAttackPower;
      CompletionCount++;
      ConnectedNodeCount = 0;
      // TODO: 魔法陣起動エフェクトを再生し、次のノードセットを配置する
    }
  }
}
