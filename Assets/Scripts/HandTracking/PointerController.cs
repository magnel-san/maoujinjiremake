using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 指の「向き」は使わず、手モデルの人差し指先ボーンの「位置」をそのままポインター位置に変換する。
  /// 位置ベースなので、向き（角度）を経由する方式のように検出ノイズが角度計算で増幅されることがない。
  ///
  /// UIのRectTransform(<see cref="_targetUIRect"/>)と参照カメラ(<see cref="_referenceCamera"/>)が
  /// 設定されている場合：指先のワールド座標をカメラのビューポート座標に投影し（<see cref="Camera.WorldToViewportPoint"/>）、
  /// その座標をそのままUI矩形へ比例配分する。「カメラ画面に映る指先の位置＝UI上のポインター位置」という、
  /// 見たままの対応になる。
  ///
  /// 未設定の場合は、指先のワールド座標をそのままポインターの表示位置として使う（3D空間のターゲット用）。
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
    [Tooltip("右手・左手のどちらの人差し指をポインターに使うか")]
    [SerializeField] private bool _useRightHand = true;
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
    /// 指先の位置をカメラのビューポート座標に投影し、そのままUI矩形へ比例配分する（位置ベース、向き不使用）。
    /// デバッグ時はマウスのスクリーン座標をそのままビューポート座標として使う。
    /// </summary>
    private void UpdateUsingScreenMapping()
    {
      float viewportX, viewportY;

      if (_debugUseMouse)
      {
        viewportX = Input.mousePosition.x / Mathf.Max(1f, Screen.width);
        viewportY = Input.mousePosition.y / Mathf.Max(1f, Screen.height);
      }
      else
      {
        if (!TryGetFingertipWorldPosition(out var fingertipWorldPos))
        {
          SetPointerActive(false);
          return;
        }

        var viewportPoint = _referenceCamera.WorldToViewportPoint(fingertipWorldPos);
        if (viewportPoint.z <= 0f)
        {
          // カメラの後ろ側
          SetPointerActive(false);
          return;
        }
        viewportX = viewportPoint.x;
        viewportY = viewportPoint.y;
      }

      // -1(左/下端)〜+1(右/上端)、中央が0になる正規化座標。
      var u = (viewportX - 0.5f) * 2f;
      var v = (viewportY - 0.5f) * 2f;

      if (_clampToUIRect)
      {
        u = Mathf.Clamp(u, -1f, 1f);
        v = Mathf.Clamp(v, -1f, 1f);
      }
      else if (Mathf.Abs(u) > 1f || Mathf.Abs(v) > 1f)
      {
        SetPointerActive(false);
        return;
      }

      var corners = new Vector3[4];
      _targetUIRect.GetWorldCorners(corners); // 0:左下 1:左上 2:右上 3:右下
      var center = (corners[0] + corners[2]) * 0.5f;
      var halfRight = (corners[2] - corners[1]) * 0.5f;
      var halfUp = (corners[1] - corners[0]) * 0.5f;

      var targetWorldPos = center + halfRight * u + halfUp * v;

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
        if (cam == null)
        {
          SetPointerActive(false);
          return;
        }
        var ray = cam.ScreenPointToRay(Input.mousePosition);
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
