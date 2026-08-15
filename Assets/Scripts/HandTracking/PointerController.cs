using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 指の向き（人差し指の付け根→指先ベクトル）を、参照カメラ(<see cref="_referenceCamera"/>)の
  /// 視野角(FOV)に対する角度の割合に変換し、UIの矩形(<see cref="_targetUIRect"/>)へ
  /// そのまま比例配分してポインターを表示する。
  /// 「カメラ画面の中央＝UIの中央、カメラ画面の端＝UIの端」という対応になるため、
  /// 指先の位置ノイズに影響されにくく、UIがカメラと一緒に移動しても自動的に追従する。
  /// カメラ／UI矩形が未設定の場合は、通常の3Dコライダーへのレイキャストにフォールバックする
  /// （履歴書オブジェクトや偵察拠点など、UI以外の3D空間上のターゲットを指す場合に使用）。
  /// </summary>
  public class PointerController : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;
    [Tooltip("ポインターとして表示するオブジェクト（未指定なら自動生成）")]
    [SerializeField] private Transform _pointerVisual;
    [Tooltip("自動生成する場合のポインター(球)の半径")]
    [SerializeField] private float _pointerRadius = 5f;

    [Header("画面マッピング方式（UIをまとめて指す場合）")]
    [Tooltip("プレイヤーの見ているカメラ（画面中央=UI中央の基準にする）")]
    [SerializeField] private Camera _referenceCamera;
    [Tooltip("ポインターを対応させるUIのRectTransform（World Space Canvas等）。" +
      "カメラ画面の端がこの矩形の端に対応する。")]
    [SerializeField] private RectTransform _targetUIRect;
    [Tooltip("カメラの視野角の外を指した場合、矩形の端にクランプするか。falseなら非表示にする")]
    [SerializeField] private bool _clampToUIRect = true;

    [Header("3Dフォールバック（UI以外の空間上のターゲットを指す場合）")]
    [SerializeField] private LayerMask _raycastMask = ~0;
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
      Vector3 origin, direction;

      if (_debugUseMouse)
      {
        if (!TryGetMouseRay(out origin, out direction))
        {
          SetPointerActive(false);
          return;
        }
      }
      else if (!TryGetHandRay(out origin, out direction))
      {
        SetPointerActive(false);
        return;
      }

      if (_referenceCamera != null && _targetUIRect != null)
      {
        UpdateUsingScreenMapping(origin, direction);
      }
      else
      {
        UpdateUsingColliderRaycast(origin, direction);
      }
    }

    /// <summary>
    /// 手首→指先のベクトルから向きを求める。付け根(MCP)→指先より基線が長く、
    /// 検出ノイズによる角度のブレが小さいため、手首を起点にしている。
    /// </summary>
    private bool TryGetHandRay(out Vector3 origin, out Vector3 direction)
    {
      origin = default;
      direction = default;

      var rig = _useRightHand ? _handTrackingController?.RightHandInstance : _handTrackingController?.LeftHandInstance;
      var tip = rig != null ? rig.IndexTip : null;
      var wrist = rig != null ? rig.wristRoot : null;
      if (tip == null || wrist == null) return false;

      origin = tip.position;
      direction = (tip.position - wrist.position).normalized;
      return direction.sqrMagnitude > 0.0001f;
    }

    private bool TryGetMouseRay(out Vector3 origin, out Vector3 direction)
    {
      origin = default;
      direction = default;

      var cam = _referenceCamera != null ? _referenceCamera : Camera.main;
      if (cam == null) return false;

      var ray = cam.ScreenPointToRay(Input.mousePosition);
      origin = ray.origin;
      direction = ray.direction;
      return true;
    }

    /// <summary>
    /// 「カメラ画面の中央=UI矩形の中央、カメラ画面の端=UI矩形の端」となるよう、
    /// 指の向きをカメラのFOVに対する角度の割合に変換してUI矩形へ比例配分する。
    /// </summary>
    private void UpdateUsingScreenMapping(Vector3 origin, Vector3 direction)
    {
      var camTransform = _referenceCamera.transform;

      // カメラのローカル軸(右・上・前)に対する方向ベクトルの成分を求める。
      var localDir = new Vector3(
        Vector3.Dot(direction, camTransform.right),
        Vector3.Dot(direction, camTransform.up),
        Vector3.Dot(direction, camTransform.forward));

      if (localDir.z <= 0.0001f)
      {
        // カメラの後ろ側を指している場合は無効
        SetPointerActive(false);
        return;
      }

      var halfVFov = _referenceCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
      var halfHFov = Mathf.Atan(Mathf.Tan(halfVFov) * _referenceCamera.aspect);

      // u,v はそれぞれ画面中央を0、左右/上下端を±1とする正規化座標。
      var u = Mathf.Atan2(localDir.x, localDir.z) / halfHFov;
      var v = Mathf.Atan2(localDir.y, localDir.z) / halfVFov;

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

      UpdateHoverTarget(RaycastForTarget(origin, direction));
    }

    /// <summary>_referenceCamera / _targetUIRect未設定時の、通常の3Dコライダーへのレイキャスト方式。</summary>
    private void UpdateUsingColliderRaycast(Vector3 origin, Vector3 direction)
    {
      if (Physics.Raycast(origin, direction, out var hit, _maxDistance, _raycastMask))
      {
        SetPointerActive(true);
        ApplySmoothedPosition(hit.point);
        UpdateHoverTarget(hit.collider.GetComponentInParent<IPointerHoldTarget>());
      }
      else
      {
        SetPointerActive(true);
        ApplySmoothedPosition(origin + direction * _maxDistance);
        UpdateHoverTarget(null);
      }
    }

    private IPointerHoldTarget RaycastForTarget(Vector3 origin, Vector3 direction)
    {
      if (Physics.Raycast(origin, direction, out var hit, _maxDistance, _raycastMask))
      {
        return hit.collider.GetComponentInParent<IPointerHoldTarget>();
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
