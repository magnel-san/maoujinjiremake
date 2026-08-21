using System;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 波（遊泳）プレハブにアタッチする、前進＋命中判定のための軽量スクリプト。
  /// SwimMinigame.SpawnWaveがInstantiate直後にInitializeを呼び、狙う対象と命中時のコールバックを渡す。
  /// 練習中は対象/コールバックをnullで渡すことで、前進はするがスコアには影響しない見た目だけの発射にできる。
  ///
  /// 命中判定はコライダーのトリガーで行うため、プレハブ側に非トリガーの当たり判定は不要だが、
  /// Collider(Is Trigger=ON)は必須。トリガーイベントの発火にはどちらか一方にRigidbodyが要るため、
  /// このプレハブにKinematicなRigidbodyを付けておくこと（狙う対象側には不要）。
  /// </summary>
  public class WaveProjectile : MonoBehaviour
  {
    [SerializeField] private float speed = 5f;

    private Transform _target;
    private Action _onHitTarget;

    /// <summary>狙う対象と命中時のコールバックを設定する。targetがnullなら命中判定自体を行わない
    /// （練習中の見た目だけの発射用）。speedOverrideを渡すとInspector既定値を上書きする。</summary>
    public void Initialize(float speedOverride, Transform target, Action onHitTarget)
    {
      if (speedOverride > 0f) speed = speedOverride;
      _target = target;
      _onHitTarget = onHitTarget;
    }

    private void Update()
    {
      transform.position += transform.forward * (speed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
      if (_target == null) return;
      if (other.transform != _target && !other.transform.IsChildOf(_target)) return;

      _onHitTarget?.Invoke();
      Destroy(gameObject);
    }
  }
}
