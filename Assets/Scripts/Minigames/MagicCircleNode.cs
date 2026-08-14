using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>魔法陣周囲に出現する光るノード。ポインターで指すと接続される。</summary>
  [RequireComponent(typeof(Collider))]
  public class MagicCircleNode : MonoBehaviour, IPointerHoldTarget
  {
    [SerializeField] private IntelligenceMinigame _minigame;
    [SerializeField] private float _connectHoldSeconds = 0.5f;

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
        _minigame?.RegisterNodeConnected();
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
