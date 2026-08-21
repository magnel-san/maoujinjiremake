using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// マップ上に生成される勇者拠点オブジェクト。ポインターを一定秒数合わせると発見され、消滅する。
  /// <see cref="ScoutMinigame"/>によって実行時に生成されるため、生成直後に<see cref="Initialize"/>で
  /// 自身の所属ミニゲームを設定してもらう前提。
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class ScoutBaseTarget : MonoBehaviour, IPointerHoldTarget
  {
    [SerializeField] private ScoutMinigame _minigame;
    [SerializeField] private float _discoverHoldSeconds = 0.8f;
    [Tooltip("発見済みの見た目を少し見せてから消えるまでの秒数")]
    [SerializeField] private float _despawnDelay = 0.2f;

    private bool _isHovering;
    private bool _discovered;
    private float _heldSeconds;

    /// <summary>ScoutMinigameが実行時に生成した直後に呼ぶ。</summary>
    public void Initialize(ScoutMinigame minigame)
    {
      _minigame = minigame;
    }

    private void Update()
    {
      if (_discovered || !_isHovering) return;

      _heldSeconds += Time.deltaTime;
      if (_heldSeconds >= _discoverHoldSeconds)
      {
        _discovered = true;
        _minigame?.RegisterBaseDiscovered();
        // TODO: 発見済みの見た目に変更する演出
        Destroy(gameObject, Mathf.Max(_despawnDelay, 0f));
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
