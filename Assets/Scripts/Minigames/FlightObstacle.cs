using System;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// ObstaclePairプレハブのルートにアタッチする、前進＋当たり判定コールバックの中継役。
  /// 隙間のサイズやパイロットの当たり判定は全て実際のコライダー（土管側の<see cref="FlightObstacleZone"/>、
  /// パイロット側は採用キャラのプレハブが持つCollider）に委ねるため、キャラごとにサイズが違っても
  /// 手動でgapHeight等の数値を調整する必要がない。
  ///
  /// 移動はFlightMinigame側の<see cref="IsMoving"/>制御に従う（墜落後の猶予中は障害物側も止める）。
  /// </summary>
  public class FlightObstacle : MonoBehaviour
  {
    [SerializeField] private float speed = 5f;

    private Vector3 _direction = Vector3.back;
    private Transform _pilotTarget;
    private Action _onHazardHit;
    private Action _onPassed;
    private bool _passedFired;
    private bool _hazardFired;

    /// <summary>墜落判定でカウントダウンに入っている間など、外側からON/OFFする。</summary>
    public bool IsMoving { get; set; } = true;

    public void Initialize(Vector3 direction, float speedOverride, float lifetimeSeconds, Transform pilotTarget, Action onHazardHit, Action onPassed)
    {
      _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.back;
      if (speedOverride > 0f) speed = speedOverride;
      _pilotTarget = pilotTarget;
      _onHazardHit = onHazardHit;
      _onPassed = onPassed;

      foreach (var zone in GetComponentsInChildren<FlightObstacleZone>())
      {
        zone.Initialize(this);
      }

      if (lifetimeSeconds > 0f) Destroy(gameObject, lifetimeSeconds); // 何にも当たらなかった場合の保険
    }

    private void Update()
    {
      if (!IsMoving) return;
      transform.position += _direction * (speed * Time.deltaTime);
    }

    public void NotifyHazardHit(Collider other)
    {
      if (_hazardFired || !IsPilot(other)) return;
      _hazardFired = true;
      _onHazardHit?.Invoke();
    }

    public void NotifyPassed(Collider other)
    {
      if (_passedFired || !IsPilot(other)) return;
      _passedFired = true;
      _onPassed?.Invoke();
    }

    private bool IsPilot(Collider other) =>
      _pilotTarget != null && (other.transform == _pilotTarget || other.transform.IsChildOf(_pilotTarget));
  }
}
