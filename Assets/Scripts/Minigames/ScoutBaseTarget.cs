using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// マップ上に配置する勇者拠点オブジェクト。ポインターを一定秒数合わせると発見される。
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class ScoutBaseTarget : MonoBehaviour, IPointerHoldTarget
  {
    [SerializeField] private ScoutMinigame _minigame;
    [SerializeField] private float _discoverHoldSeconds = 2f;

    private bool _isHovering;
    private bool _discovered;
    private float _heldSeconds;

    private void Update()
    {
      if (_discovered || !_isHovering) return;

      _heldSeconds += Time.deltaTime;
      if (_heldSeconds >= _discoverHoldSeconds)
      {
        _discovered = true;
        _minigame?.RegisterBaseDiscovered();
        // TODO: 発見済みの見た目に変更する
      }
    }

    public void OnPointerHoldEnter() => _isHovering = true;

    public void OnPointerHoldExit()
    {
      _isHovering = false;
      if (!_discovered) _heldSeconds = 0f;
    }
  }
}
