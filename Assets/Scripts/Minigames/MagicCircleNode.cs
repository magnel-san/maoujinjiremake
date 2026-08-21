using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>魔法陣周囲（五芒星の頂点）に配置するノード。ポインターで指すと接続される。
  /// 五芒星は正しい順番でなぞる必要があるため、各ノードは自分が何番目の頂点かを<see cref="nodeIndex"/>で
  /// 持っておき、<see cref="IntelligenceMinigame"/>側で順番の正否を判定する。</summary>
  [RequireComponent(typeof(Collider))]
  public class MagicCircleNode : MonoBehaviour, IPointerHoldTarget
  {
    [SerializeField] private IntelligenceMinigame _minigame;
    [SerializeField] private float _connectHoldSeconds = 0.5f;
    [Tooltip("五芒星の頂点として、円周上を並んだ順に0〜4を振る（IntelligenceMinigame側のなぞり順はこの番号を基準に決まる）")]
    [SerializeField] private int nodeIndex;

    private bool _isHovering;
    private bool _connected;
    private float _heldSeconds;

    private void Update()
    {
      if (_connected || !_isHovering) return;

      _heldSeconds += Time.deltaTime;
      if (_heldSeconds >= _connectHoldSeconds)
      {
        _connected = true;
        _minigame?.RegisterNodeTouched(nodeIndex);
      }
    }

    public void OnPointerHoldEnter() => _isHovering = true;

    public void OnPointerHoldExit()
    {
      _isHovering = false;
      if (!_connected) _heldSeconds = 0f;
    }

    public void ResetNode()
    {
      _connected = false;
      _heldSeconds = 0f;
    }
  }
}
