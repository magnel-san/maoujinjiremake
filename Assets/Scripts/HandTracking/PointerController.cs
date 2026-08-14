using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 指先の延長線とレイキャストの交点にポインター（Wiiリモコン方式）を表示する。
  /// 手モデル自体はゲーム開始まで非表示だが、ポインターは常時表示する。
  /// 現在ポインターが指しているターゲット（<see cref="IPointerHoldTarget"/>）へ
  /// hover通知を送る。
  /// </summary>
  public class PointerController : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;
    [Tooltip("ポインターとして表示するオブジェクト（未指定なら自動生成）")]
    [SerializeField] private Transform _pointerVisual;
    [SerializeField] private LayerMask _raycastMask = ~0;
    [SerializeField] private float _maxDistance = 50f;
    [Tooltip("右手・左手のどちらの人差し指をポインターに使うか")]
    [SerializeField] private bool _useRightHand = true;

    private IPointerHoldTarget _currentTarget;

    public bool IsPointerActive { get; private set; }
    public Vector3 PointerWorldPosition { get; private set; }

    private void Awake()
    {
      if (_pointerVisual == null)
      {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "PointerVisual";
        go.transform.localScale = Vector3.one * 0.03f;
        Destroy(go.GetComponent<Collider>());
        _pointerVisual = go.transform;
      }
    }

    private void Update()
    {
      var rig = _useRightHand ? _handTrackingController?.RightHandInstance : _handTrackingController?.LeftHandInstance;
      var tip = rig != null ? rig.IndexTip : null;

      if (tip == null)
      {
        SetPointerActive(false);
        return;
      }

      var origin = tip.position;
      var direction = tip.forward;

      if (Physics.Raycast(origin, direction, out var hit, _maxDistance, _raycastMask))
      {
        SetPointerActive(true);
        PointerWorldPosition = hit.point;
        _pointerVisual.position = hit.point;

        var target = hit.collider.GetComponentInParent<IPointerHoldTarget>();
        UpdateHoverTarget(target);
      }
      else
      {
        SetPointerActive(true);
        PointerWorldPosition = origin + direction * _maxDistance;
        _pointerVisual.position = PointerWorldPosition;
        UpdateHoverTarget(null);
      }
    }

    private void UpdateHoverTarget(IPointerHoldTarget target)
    {
      if (target == _currentTarget) return;

      _currentTarget?.OnPointerHoldExit();
      _currentTarget = target;
      _currentTarget?.OnPointerHoldEnter();
    }

    private void SetPointerActive(bool active)
    {
      IsPointerActive = active;
      if (_pointerVisual != null && _pointerVisual.gameObject.activeSelf != active)
      {
        _pointerVisual.gameObject.SetActive(active);
      }
      if (!active)
      {
        UpdateHoverTarget(null);
      }
    }
  }

  /// <summary>
  /// ポインターのhover対象になれるオブジェクトが実装するインターフェース。
  /// <see cref="UI.CircularHoldButton"/>等で使用する。
  /// </summary>
  public interface IPointerHoldTarget
  {
    void OnPointerHoldEnter();
    void OnPointerHoldExit();
  }
}
