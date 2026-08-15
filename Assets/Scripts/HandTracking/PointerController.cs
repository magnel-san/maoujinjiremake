using UnityEngine;
using UnityEngine.InputSystem;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 指の「向き」は一切使わない。MediaPipeが検出した人差し指先の正規化座標
  /// （<see cref="HandTrackingController.TryGetIndexFingertipViewport"/>）を直接カメラのビューポート座標として扱う。
  /// 手モデルのボーン・回転計算を完全に迂回するため、手モデル側の回転リターゲットのブレの影響を受けない。
  ///
  /// UIのRectTransform(<see cref="_targetUIRect"/>)と参照カメラ(<see cref="_referenceCamera"/>)が
  /// 設定されている場合：そのビューポート座標を通るレイと、UI矩形が乗っている平面との交点をポインター位置にする。
  /// 「カメラの映る範囲＝UIの範囲」という対応になるよう、カメラ側だけを調整すればよい。
  ///
  /// 未設定の場合は、手モデルの指先ボーンのワールド座標をそのままポインターの表示位置として使う（3D空間のターゲット用）。
  ///
  /// ポインターが指しているターゲットの判定も、レイキャストではなく「ポインター表示位置の周囲に
  /// コライダーがあるか」（<see cref="Physics.OverlapSphere"/>）で行う。
  /// </summary>
  public class PointerController : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;
    [Tooltip("ポインターとして表示するオブジェクト（未指定なら自動生成）")]
    [SerializeField] private Transform _pointerVisual;
    [Tooltip("自動生成する場合のポインター(球)の半径")]
    [SerializeField] private float _pointerRadius = 5f;

    [Header("画面マッピング方式（UIをまとめて指す場合）")]
    [Tooltip("指先の位置を投影する基準カメラ")]
    [SerializeField] private Camera _referenceCamera;
    [Tooltip("ポインターを対応させるUIのRectTransform（World Space Canvas等）。" +
      "カメラのビューポート座標(0〜1)をそのままこの矩形へ比例配分する。")]
    [SerializeField] private RectTransform _targetUIRect;
    [Tooltip("カメラのビューポート範囲の外を指した場合、矩形の端にクランプするか。falseなら非表示にする")]
    [SerializeField] private bool _clampToUIRect = true;

    [Header("ターゲット判定")]
    [Tooltip("ポインター表示位置の周囲でターゲット(IPointerHoldTarget)を探す半径")]
    [SerializeField] private float _hitTestRadius = 5f;
    [SerializeField] private LayerMask _raycastMask = ~0;

    [Header("3Dフォールバック（UI以外の空間上のターゲットを指す場合）")]
    [Tooltip("参照カメラ/UI矩形が未設定のとき、マウスから3D位置を決めるための最大距離")]
    [SerializeField] private float _maxDistance = 50f;

    [Header("共通")]
    [Tooltip("右手・左手のどちらの人差し指をポインターに使うか。現状は左手を認識させて使っているためデフォルトOFF。")]
    [SerializeField] private bool _useRightHand;
    [Tooltip("ポインター位置の平滑化にかける時間(秒)。0で平滑化なし、値が大きいほど滑らかだが遅延が増える")]
    [SerializeField] private float _smoothingTime = 0.08f;

    [Header("デバッグ")]
    [Tooltip("ONの間は手のトラッキングを無視し、マウスの位置でポインターを操作する。" +
      "手のトラッキングとは無関係にUI/ゲームロジック側の動作確認をするための機能。")]
    [SerializeField] private bool _debugUseMouse;

    private IPointerHoldTarget _currentTarget;
    private Vector3? _smoothedWorldPos;

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
      if (_referenceCamera != null && _targetUIRect != null)
      {
        UpdateUsingScreenMapping();
      }
      else
      {
        UpdateUsing3DPosition();
      }
    }

    /// <summary>
    /// MediaPipeの人差し指先の正規化座標をそのままカメラのビューポート座標として使い（向き不使用）、
    /// その視点座標を通るレイと「UI矩形が乗っている平面」との交点をポインター位置にする。
    /// カメラの投影計算そのものを使うため、UI矩形のサイズに関わらず
    /// 「画面上で見えている位置」と実際にポインターが置かれる位置が必ず一致する。
    /// デバッグ時はマウスのスクリーン座標をそのままビューポート座標として使う。
    /// </summary>
    private void UpdateUsingScreenMapping()
    {
      float viewportX, viewportY;

      if (_debugUseMouse)
      {
        if (!TryGetMouseScreenPosition(out var mousePos))
        {
          SetPointerActive(false);
          return;
        }
        viewportX = mousePos.x / Mathf.Max(1f, Screen.width);
        viewportY = mousePos.y / Mathf.Max(1f, Screen.height);
      }
      else
      {
        if (_handTrackingController == null || !_handTrackingController.TryGetIndexFingertipViewport(_useRightHand, out var viewport))
        {
          SetPointerActive(false);
          return;
        }
        viewportX = viewport.x;
        viewportY = viewport.y;
      }

      if (_clampToUIRect)
      {
        viewportX = Mathf.Clamp01(viewportX);
        viewportY = Mathf.Clamp01(viewportY);
      }
      else if (viewportX < 0f || viewportX > 1f || viewportY < 0f || viewportY > 1f)
      {
        SetPointerActive(false);
        return;
      }

      var ray = _referenceCamera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
      var plane = new Plane(_targetUIRect.forward, _targetUIRect.position);

      if (!plane.Raycast(ray, out var enter) || enter <= 0f)
      {
        SetPointerActive(false);
        return;
      }

      var targetWorldPos = ray.GetPoint(enter);

      SetPointerActive(true);
      ApplySmoothedPosition(targetWorldPos);
      UpdateHoverTarget(FindTargetNear(_smoothedWorldPos.Value));
    }

    /// <summary>
    /// UI矩形/カメラが未設定の場合：指先のワールド座標をそのままポインター表示位置として使う。
    /// デバッグ時はマウスのレイと、指先の代わりに使う固定距離の交点を使う。
    /// </summary>
    private void UpdateUsing3DPosition()
    {
      Vector3 targetPos;

      if (_debugUseMouse)
      {
        var cam = _referenceCamera != null ? _referenceCamera : Camera.main;
        if (cam == null || !TryGetMouseScreenPosition(out var mousePos))
        {
          SetPointerActive(false);
          return;
        }
        var ray = cam.ScreenPointToRay(mousePos);
        targetPos = Physics.Raycast(ray, out var hit, _maxDistance, _raycastMask)
          ? hit.point
          : ray.origin + ray.direction * _maxDistance;
      }
      else
      {
        if (!TryGetFingertipWorldPosition(out targetPos))
        {
          SetPointerActive(false);
          return;
        }
      }

      SetPointerActive(true);
      ApplySmoothedPosition(targetPos);
      UpdateHoverTarget(FindTargetNear(_smoothedWorldPos.Value));
    }

    /// <summary>
    /// 新Input System経由でマウスのスクリーン座標を取得する。
    /// プロジェクトのActive Input Handlingが"Input System Package"のみの場合、
    /// 旧UnityEngine.Input.mousePositionは例外を投げるため使わない。
    /// </summary>
    private bool TryGetMouseScreenPosition(out Vector2 screenPosition)
    {
      var mouse = Mouse.current;
      if (mouse == null)
      {
        screenPosition = default;
        return false;
      }
      screenPosition = mouse.position.ReadValue();
      return true;
    }

    private bool TryGetFingertipWorldPosition(out Vector3 position)
    {
      var rig = _useRightHand ? _handTrackingController?.RightHandInstance : _handTrackingController?.LeftHandInstance;
      var tip = rig != null ? rig.IndexTip : null;
      if (tip == null)
      {
        position = default;
        return false;
      }
      position = tip.position;
      return true;
    }

    private IPointerHoldTarget FindTargetNear(Vector3 worldPos)
    {
      var hits = Physics.OverlapSphere(worldPos, _hitTestRadius, _raycastMask);
      foreach (var hit in hits)
      {
        var target = hit.GetComponentInParent<IPointerHoldTarget>();
        if (target != null) return target;
      }
      return null;
    }

    private void ApplySmoothedPosition(Vector3 targetPos)
    {
      if (_smoothingTime <= 0f || _smoothedWorldPos == null)
      {
        _smoothedWorldPos = targetPos;
      }
      else
      {
        var t = 1f - Mathf.Exp(-Time.deltaTime / _smoothingTime);
        _smoothedWorldPos = Vector3.Lerp(_smoothedWorldPos.Value, targetPos, t);
      }

      PointerWorldPosition = _smoothedWorldPos.Value;
      _pointerVisual.position = _smoothedWorldPos.Value;
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
        _smoothedWorldPos = null;
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
