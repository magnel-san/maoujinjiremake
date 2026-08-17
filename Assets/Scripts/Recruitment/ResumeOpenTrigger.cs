using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Recruitment
{
  /// <summary>
  /// キャラの前に出現する履歴書オブジェクト。ポインターを一定秒数合わせると
  /// <see cref="RecruitmentPhaseController.RequestOpenResume"/>を呼んで履歴書を開く。
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class ResumeOpenTrigger : MonoBehaviour, IPointerHoldTarget
  {
    private RecruitmentPhaseController _controller;
    private CharacterData _character;
    private float _holdSeconds = 3f;

    private bool _isHovering;
    private float _heldSeconds;

    public void Initialize(RecruitmentPhaseController controller, CharacterData character, float holdSeconds)
    {
      _controller = controller;
      _character = character;
      _holdSeconds = holdSeconds;
    }

    private void Update()
    {
      if (!_isHovering) return;

      _heldSeconds += Time.deltaTime;
      if (_heldSeconds >= _holdSeconds)
      {
        _heldSeconds = 0f;
        _controller?.RequestOpenResume(_character);
      }
    }

    public void OnPointerHoldEnter() => _isHovering = true;

    public void OnPointerHoldExit()
    {
      _isHovering = false;
      _heldSeconds = 0f;
    }
  }
}
