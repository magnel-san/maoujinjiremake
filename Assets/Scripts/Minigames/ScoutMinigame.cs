using DemonLordHR.HandTracking;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【偵察】仕様4.5：マップ内に隠れた勇者拠点をポインターで数秒指して発見する。
  /// このミニゲームはジェスチャーではなくポインター保持で進行するため、
  /// <see cref="OnGestureForMinigame"/>は使用しない（<see cref="ScoutBaseTarget"/>から呼ばれる）。
  /// </summary>
  public class ScoutMinigame : MinigameBase
  {
    public float Score { get; private set; }
    public int BasesFound { get; private set; }

    protected override void OnMinigameStart()
    {
      Score = 0f;
      BasesFound = 0;
    }

    /// <summary>シーン上の<see cref="ScoutBaseTarget"/>が発見判定した際に呼ばれる。</summary>
    public void RegisterBaseDiscovered()
    {
      if (!IsRunning) return;
      Score += totalAttackPower;
      BasesFound++;
    }

    protected override void OnGestureForMinigame(GestureType type) { }
  }
}
