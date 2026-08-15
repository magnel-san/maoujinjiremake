using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 指の向き（人差し指の付け根→指先ベクトル）を延長し、<see cref="_pointerPlane"/>で指定した
  /// 平面（通常はUIを表示しているWorld Space Canvas）との交点にポインターを表示する、
  /// Wiiリモコン方式のポインター。平面上だけを移動し、奥行き(Z)方向には動かない。
  /// 手モデル自体はゲーム開始まで非表示だが、ポインターは常時表示する。
  /// 現在ポインターが指しているターゲット（<see cref="IPointerHoldTarget"/>）へ
  /// hover通知を送る。
  /// </summary>
  public class PointerController : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;
    [Tooltip("ポインターとして表示するオブジェクト（未指定なら自動生成）")]
    [SerializeField] private Transform _pointerVisual;
    [Tooltip("自動生成する場合のポインター(球)の半径")]
    [SerializeField] private float _pointerRadius = 5f;
    [Tooltip("ポインターを投影する平面のTransform（例：UIのWorld Space Canvas）。" +
      "この平面の位置・向きで無限平面を定義し、ポインターは常にこの平面上に表示される（Z方向には動かない）。" +
      "未設定の場合は通常の3Dレイキャストにフォールバックする。")]
    [SerializeField] private Transform _pointerPlane;
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
        Destroy(go.GetComponent<Collider>());
        _pointerVisual = go.transform;
      }

      // Sphereプリミティブは半径0.5(直径1)がスケール1に相当するため、
      // 指定した半径になるようスケールを直径換算で設定する。
      _pointerVisual.localScale = Vector3.one * (_pointerRadius * 2f);
    }

    private void Update()
    {
      var rig = _useRightHand ? _handTrackingController?.RightHandInstance : _handTrackingController?.LeftHandInstance;
      var indexFinger = rig != null ? rig.index : null;
      var baseBone = indexFinger != null && indexFinger.bones.Length == 4 ? indexFinger.bones[0] : null;
      var tip = indexFinger != null && indexFinger.bones.Length == 4 ? indexFinger.bones[3] : null;

      if (tip == null || baseBone == null)
      {
        SetPointerActive(false);
        return;
      }

      var origin = tip.position;
      // NOTE: リターゲット処理(HandTrackingController)は各ボーンのlocalPositionしか更新しておらず、
      // localRotationは更新していないため、tip.forward(=ボーンの回転)は常に一定で使えない。
      // 代わりに「人差し指の付け根→指先」の実際の位置ベクトルから向きを求める。
      var direction = (tip.position - baseBone.position).normalized;
      if (direction.sqrMagnitude < 0.0001f)
      {
        SetPointerActive(false);
        return;
      }

      if (_pointerPlane != null)
      {
        UpdateUsingPlane(origin, direction);
      }
      else
      {
        UpdateUsingColliderRaycast(origin, direction);
      }
    }

    /// <summary>
    /// _pointerPlaneが定義する無限平面との交点にポインターを固定する（Wiiリモコン方式）。
    /// Z方向（平面の奥行き）には動かず、平面上のX/Yだけが変化する。
    /// </summary>
    private void UpdateUsingPlane(Vector3 origin, Vector3 direction)
    {
      var plane = new Plane(_pointerPlane.forward, _pointerPlane.position);
      var ray = new Ray(origin, direction);

      if (!plane.Raycast(ray, out var enter) || enter <= 0f || enter > _maxDistance)
      {
        SetPointerActive(false);
        return;
      }

      var planePoint = origin + direction * enter;
      SetPointerActive(true);
      PointerWorldPosition = planePoint;
      _pointerVisual.position = planePoint;

      // どのボタン/対象を指しているかの判定は、平面上の当たり判定(Collider)を使う。
      IPointerHoldTarget target = null;
      if (Physics.Raycast(origin, direction, out var hit, _maxDistance, _raycastMask))
      {
        target = hit.collider.GetComponentInParent<IPointerHoldTarget>();
      }
      UpdateHoverTarget(target);
    }

    /// <summary>_pointerPlane未設定時の従来の3Dレイキャスト方式。</summary>
    private void UpdateUsingColliderRaycast(Vector3 origin, Vector3 direction)
    {
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
