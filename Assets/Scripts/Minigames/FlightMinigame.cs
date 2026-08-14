using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【飛行】仕様4.2：「腕を翼のように上下に振る」で操作するTPSフラッピーバード風ミニゲーム。
  /// 実際の飛行制御・障害物生成は3D演出のためTODO、復活回数管理と失敗判定のみ実装する。
  /// </summary>
  public class FlightMinigame : MinigameBase
  {
    private CharacterData _pilot;
    private int _revivalsRemaining;
    private float _revivalTimer = -1f;

    public int RevivalsRemaining => _revivalsRemaining;

    protected override void OnMinigameStart()
    {
      _pilot = PickRandomAssigned();
      var divisor = settings != null ? settings.flightRevivalDivisor : 100f;
      _revivalsRemaining = Mathf.Max(0, Mathf.FloorToInt(totalAttackPower / Mathf.Max(divisor, 0.01f)));
      _revivalTimer = -1f;
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      if (_revivalTimer >= 0f)
      {
        _revivalTimer -= deltaTime;
        if (_revivalTimer <= 0f)
        {
          _revivalTimer = -1f;
          // TODO: 復活位置へ再配置する
        }
      }
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.WingFlap) return;
      // TODO: 上昇操作を適用する
    }

    /// <summary>障害物等への衝突時に、シーン側から呼び出す。</summary>
    public void RegisterFailure()
    {
      if (_revivalTimer >= 0f) return;

      if (_revivalsRemaining <= 0)
      {
        FinishAsGameOver();
        return;
      }

      _revivalsRemaining--;
      _revivalTimer = settings != null ? settings.flightRevivalDelaySeconds : 1f;
    }
  }
}
