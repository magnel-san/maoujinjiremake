using System;
using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Unity.Sample;
using UnityEngine;
using ImageProcessingOptions = Mediapipe.Tasks.Vision.Core.ImageProcessingOptions;
using VisionRunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;
using Landmark = Mediapipe.Tasks.Components.Containers.Landmark;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// MediaPipe HandLandmarkerを起動し、検出結果（21ランドマーク×左右手）を
  /// 魔王の手モデル（HandBoneRig）にリターゲットする。
  /// 左手モデルのみをプレハブとして持ち、右手はスケールX反転でミラー生成する。
  ///
  /// リターゲット方式（回転駆動、レストポーズ基準）:
  /// 各ボーンの「レスト状態（Awake時点の回転・ワールド前方向）」をキャッシュしておき、
  /// 毎フレーム、そのレスト状態からの絶対回転として目標回転を再計算する。
  /// 過去に動作実績のある実装（WritingHandBoneRig）と同じ考え方を採用している。
  /// 相対回転を毎フレーム積み上げる方式だと、FromToRotationが軸まわりの回転(ロール)を
  /// 拘束しないため誤差が蓄積して指が捻れていくが、常にレスト状態基準で計算し直すことで
  /// 誤差が蓄積しない。
  ///
  /// 検出結果は<see cref="OnHandLandmarkerResult"/>イベントとしても公開し、
  /// GestureRecognizer等が同じ検出結果を購読できるようにする（パイプラインは1本化）。
  /// </summary>
  public class HandTrackingController : MonoBehaviour
  {
    [Header("モデル")]
    [Tooltip("左手モデルのプレハブ。ルートにHandBoneRigがアタッチされていること")]
    [SerializeField] private HandBoneRig _leftHandPrefab;
    [Tooltip("右手モデルを生成する親（未指定ならこのTransform配下）")]
    [SerializeField] private Transform _handsParent;

    [Header("MediaPipe設定")]
    [SerializeField] private BaseOptions.Delegate _delegate =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
      BaseOptions.Delegate.CPU;
#else
      BaseOptions.Delegate.GPU;
#endif
    [SerializeField] private string _modelAssetPath = "hand_landmarker.bytes";
    [SerializeField] private int _numHands = 2;
    [SerializeField, Range(0f, 1f)] private float _minHandDetectionConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float _minHandPresenceConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float _minTrackingConfidence = 0.5f;

    [Header("リターゲット（位置・共通）")]
    [Tooltip("MediaPipeワールド座標(m)から手モデルへのスケール係数（トラッキングカメラ未設定時のみ使用）")]
    [SerializeField] private float _worldLandmarkScale = 1f;
    [Tooltip("魔王の手モデルの表示可否（ゲーム世界に入るまでは非表示）")]
    [SerializeField] private bool _handsVisible;

    [Header("手首位置トラッキング（画面と同じ範囲を直接マッピング）")]
    [Tooltip("指定すると、指の正規化座標をこのカメラのビューポート座標へ直接マッピングして手首位置を決める" +
      "（指を画面端まで動かせば手首も画面端まで動く）。未設定ならワールドランドマークを単純スケールして使う。")]
    [SerializeField] private Camera _trackingCamera;
    [Tooltip("トラッキングカメラからの距離。手首を配置したい距離に合わせる。")]
    [SerializeField] private float _trackingDistanceFromCamera = 1.5f;

    [Header("軸の向き（見た目が反転・上下逆に見える場合はここを調整）")]
    [Tooltip("MediaPipeのX(右方向が正)を反転するか。カメラ映像のミラー設定と揃える。" +
      "左右の判定自体はhandedness側で別途補正済みなので、これはfalseで捻れが無いことを確認済み。")]
    [SerializeField] private bool _mirrorX;
    [Tooltip("MediaPipeのY(下方向が正)の反転を無効化するか。実機確認の結果trueでモデルが正しくなることを確認済み。")]
    [SerializeField] private bool _mirrorY = true;
    [Tooltip("奥行き(Z)の向きを反転するか。指を握る/伸ばす向きが逆に見える場合に切り替える。" +
      "実機確認の結果trueでモデルが正しくなることを確認済み。")]
    [SerializeField] private bool _mirrorZ = true;

    [Header("スムージング")]
    [Tooltip("ボーン回転の追従の速さ（1に近いほど即座に追従）。レスト基準で毎フレーム再計算するため値を大きくしても捻れない。")]
    [SerializeField, Range(0.01f, 1f)] private float _rotationSmoothing = 0.6f;
    [Tooltip("手首位置の追従の速さ。")]
    [SerializeField, Range(0.01f, 1f)] private float _positionSmoothing = 0.6f;

    [Header("前腕の遅延追従（任意、HandBoneRig.forearmBoneが設定されている場合のみ有効）")]
    [Tooltip("前腕の回転が手首の回転に追いつく速さ（大きいほど速く追従＝遅延が少ない）。" +
      "小さくすると手首だけが素早く曲がり、前腕がゆっくり追いかけてくるように見える。")]
    [SerializeField, Range(0f, 20f)] private float _forearmFollowSpeed = 6f;

    [Header("MediaPipe初期化")]
    [Tooltip("シーンに\"Bootstrap\"という名前のGameObjectが無い場合に生成するプレハブ。" +
      "Assets/MediaPipeUnity/Samples/Resources/Bootstrap.prefab を指定する。")]
    [SerializeField] private GameObject _bootstrapPrefab;

    private HandLandmarker _taskApi;
    private HandBoneRig _leftHandInstance;
    private HandBoneRig _rightHandInstance;
    private HandRetargetState _leftState;
    private HandRetargetState _rightState;
    private TextureFramePool _textureFramePool;
    private Coroutine _runCoroutine;

    /// <summary>MediaPipeの検出結果が届くたびに発火する。GestureRecognizer等が購読する。</summary>
    public event Action<HandLandmarkerResult> OnHandLandmarkerResult;

    public HandBoneRig LeftHandInstance => _leftHandInstance;
    public HandBoneRig RightHandInstance => _rightHandInstance;

    public bool HandsVisible
    {
      get => _handsVisible;
      set
      {
        _handsVisible = value;
        if (_leftHandInstance != null) _leftHandInstance.gameObject.SetActive(value);
        if (_rightHandInstance != null) _rightHandInstance.gameObject.SetActive(value);
      }
    }

    private void Awake()
    {
      var parent = _handsParent != null ? _handsParent : transform;

      if (_leftHandPrefab != null)
      {
        _leftHandInstance = Instantiate(_leftHandPrefab, parent);
        _leftHandInstance.isRightHand = false;
        _leftHandInstance.name = "MaouHand_Left";
        _leftState = new HandRetargetState(_leftHandInstance);

        _rightHandInstance = Instantiate(_leftHandPrefab, parent);
        _rightHandInstance.isRightHand = true;
        _rightHandInstance.name = "MaouHand_Right";
        var s = _rightHandInstance.transform.localScale;
        _rightHandInstance.transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
        _rightState = new HandRetargetState(_rightHandInstance);
      }

      HandsVisible = _handsVisible;
    }

    private void OnEnable()
    {
      _runCoroutine = StartCoroutine(Run());
    }

    private void OnDisable()
    {
      if (_runCoroutine != null)
      {
        StopCoroutine(_runCoroutine);
        _runCoroutine = null;
      }
      _taskApi?.Close();
      _taskApi = null;
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    private IEnumerator Run()
    {
      var bootstrap = FindOrCreateBootstrap();
      yield return new WaitUntil(() => bootstrap.isFinished);

      yield return AssetLoader.PrepareAssetAsync(_modelAssetPath);

      // NOTE: VIDEOモード＋TryDetectForVideoによる同期取得を使う（過去に動作実績のある方式）。
      // LIVE_STREAM＋非同期コールバックは、コールバックがどのスレッドで呼ばれるかに気を配る必要があり、
      // 不要な複雑さの原因になっていた。
      var options = new HandLandmarkerOptions(
        new BaseOptions(_delegate, modelAssetPath: _modelAssetPath),
        runningMode: VisionRunningMode.VIDEO,
        numHands: _numHands,
        minHandDetectionConfidence: _minHandDetectionConfidence,
        minHandPresenceConfidence: _minHandPresenceConfidence,
        minTrackingConfidence: _minTrackingConfidence);

      _taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);

      var imageSource = ImageSourceProvider.ImageSource;
      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("[HandTrackingController] Failed to start ImageSource.");
        yield break;
      }

      _textureFramePool = new TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      var stopwatch = Stopwatch.StartNew();
      var result = HandLandmarkerResult.Alloc(_numHands);

      while (true)
      {
        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return new WaitForEndOfFrame();
          continue;
        }

        var req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
        yield return new WaitUntil(() => req.done);

        if (req.hasError)
        {
          Debug.LogWarning("[HandTrackingController] Failed to read texture from the image source.");
          continue;
        }

        var image = textureFrame.BuildCPUImage();
        textureFrame.Release();

        if (_taskApi.TryDetectForVideo(image, stopwatch.ElapsedMilliseconds, imageProcessingOptions, ref result))
        {
          OnHandLandmarkerResult?.Invoke(result);
          RetargetToBones(result);
        }
      }
    }

    private Bootstrap FindOrCreateBootstrap()
    {
      var obj = GameObject.Find("Bootstrap");
      if (obj == null)
      {
        if (_bootstrapPrefab == null)
        {
          Debug.LogError("[HandTrackingController] Bootstrapがシーンに無く、_bootstrapPrefabも未設定です。" +
            "Assets/MediaPipeUnity/Samples/Resources/Bootstrap.prefab をインスペクタで指定してください。");
        }
        obj = Instantiate(_bootstrapPrefab);
        obj.name = "Bootstrap";
        DontDestroyOnLoad(obj);
      }
      return obj.GetComponent<Bootstrap>();
    }

    // 人差し指先（ランドマーク8番）の正規化座標を、手モデルのボーン計算を一切経由せずに
    // 直接キャッシュしておく。ポインターの位置決めはこれだけを使う（回転計算のブレの影響を受けない）。
    private Vector2? _leftIndexTipViewport;
    private Vector2? _rightIndexTipViewport;

    /// <summary>
    /// 人差し指先のビューポート座標（0〜1、X:右が1, Y:上が1）をMediaPipeの生データから直接取得する。
    /// 手モデルのボーン・回転計算を一切経由しないため、ポインターの位置決めに使うと安定する。
    /// 指定した方の手のデータが今無い場合は、もう片方の手のデータがあればそちらにフォールバックする
    /// （固定の左右設定と、実際にどちらの手を映しているかがズレていても追従できるようにするため）。
    /// </summary>
    public bool TryGetIndexFingertipViewport(bool preferRightHand, out Vector2 viewport)
    {
      var preferred = preferRightHand ? _rightIndexTipViewport : _leftIndexTipViewport;
      if (preferred.HasValue)
      {
        viewport = preferred.Value;
        return true;
      }

      var fallback = preferRightHand ? _leftIndexTipViewport : _rightIndexTipViewport;
      if (fallback.HasValue)
      {
        viewport = fallback.Value;
        return true;
      }

      viewport = default;
      return false;
    }

    private void RetargetToBones(HandLandmarkerResult result)
    {
      // その回検出されなかった手のキャッシュは必ずクリアする。クリアしないと、
      // 一度でも検出された手のデータがいつまでも残り続け、後から別の手だけを映しても
      // 古いキャッシュの方が優先され続けてしまう（フォールバックが機能しない）。
      _leftIndexTipViewport = null;
      _rightIndexTipViewport = null;

      if (result.handWorldLandmarks == null) return;

      for (var i = 0; i < result.handWorldLandmarks.Count; i++)
      {
        var isRight = IsRightHand(result, i);

        NormalizedLandmark? normalizedWrist = null;
        NormalizedLandmark? normalizedIndexTip = null;
        if (result.handLandmarks != null && i < result.handLandmarks.Count &&
            result.handLandmarks[i].landmarks != null && result.handLandmarks[i].landmarks.Count > 8)
        {
          var landmarks = result.handLandmarks[i].landmarks;
          normalizedWrist = landmarks[0];
          normalizedIndexTip = landmarks[8];
        }

        if (normalizedIndexTip.HasValue)
        {
          var viewport = ToViewport(normalizedIndexTip.Value);
          if (isRight) _rightIndexTipViewport = viewport; else _leftIndexTipViewport = viewport;
        }

        var rig = isRight ? _rightHandInstance : _leftHandInstance;
        var state = isRight ? _rightState : _leftState;
        if (rig == null || state == null || rig.wristRoot == null) continue;

        var worldLandmarks = result.handWorldLandmarks[i];
        if (worldLandmarks.landmarks == null || worldLandmarks.landmarks.Count < 21) continue;

        ApplyPose(rig, state, worldLandmarks, normalizedWrist);
      }
    }

    /// <summary>
    /// MediaPipeの正規化座標(X:右が1,Y:下が1)を、ビューポート座標(X:右が1,Y:上が1)に変換する。
    /// Y方向の反転はUnityのビューポート規約に合わせるための固定変換であり、
    /// 3Dボーンの回転計算に使う_mirrorYとは無関係（そちらは3D方向ベクトル再構成用の別設定）。
    /// ここで_mirrorYを共有すると、3D側のために_mirrorYをtrueにした途端、
    /// ポインターや手首位置の上下が逆になってしまうため、意図的に別扱いにしている。
    /// </summary>
    private Vector2 ToViewport(NormalizedLandmark lm)
    {
      var x = _mirrorX ? 1f - lm.x : lm.x;
      var y = 1f - lm.y;
      return new Vector2(x, y);
    }

    /// <summary>
    /// MediaPipeのhandedness分類は「入力画像が鏡像（フロントカメラの自撮り表示）である」ことを前提に
    /// Left/Rightを出力する仕様。このプロジェクトのパイプラインはその前提通りに鏡像化されていないため、
    /// 実際の手と分類結果が左右逆になる。そのため判定を反転させている。
    /// </summary>
    private bool IsRightHand(HandLandmarkerResult result, int index)
    {
      if (result.handedness == null || index >= result.handedness.Count) return false;
      var classifications = result.handedness[index];
      if (classifications.categories == null || classifications.categories.Count == 0) return false;
      return classifications.categories[0].categoryName == "Left";
    }

    private static readonly int[][] FingerLandmarkIndices =
    {
      new[] { 0, 1, 2, 3, 4 },     // Thumb: Wrist, CMC, MCP, IP, TIP
      new[] { 0, 5, 6, 7, 8 },     // Index: Wrist, MCP, PIP, DIP, TIP
      new[] { 0, 9, 10, 11, 12 },  // Middle
      new[] { 0, 13, 14, 15, 16 }, // Ring
      new[] { 0, 17, 18, 19, 20 }, // Pinky
    };

    private void ApplyPose(HandBoneRig rig, HandRetargetState state, Landmarks worldLandmarks, NormalizedLandmark? normalizedWrist)
    {
      var lm = worldLandmarks.landmarks;

      // --- 手首位置 ---
      if (_trackingCamera != null && normalizedWrist.HasValue)
      {
        var viewport = ToViewport(normalizedWrist.Value);
        var viewportPoint = new Vector3(viewport.x, viewport.y, _trackingDistanceFromCamera);
        var targetWorldPos = _trackingCamera.ViewportToWorldPoint(viewportPoint);
        rig.wristRoot.position = Vector3.Lerp(rig.wristRoot.position, targetWorldPos, _positionSmoothing);
      }
      else
      {
        var wristTarget = ConvertToUnityVector(new Vector3(lm[0].x, lm[0].y, lm[0].z)) * _worldLandmarkScale;
        rig.wristRoot.localPosition = Vector3.Lerp(rig.wristRoot.localPosition, wristTarget, _positionSmoothing);
      }

      // --- 手首の回転（傾き・ひねり） ---
      ApplyWristRotation(rig, state, lm);

      // --- 前腕（任意）：位置は手首にしっかり追従、回転だけ遅らせて手首が曲がって見えるようにする ---
      ApplyForearmFollow(rig);

      // --- 各指の回転（レスト基準の絶対回転） ---
      var chains = new[] { rig.thumb, rig.index, rig.middle, rig.ring, rig.pinky };
      for (var c = 0; c < chains.Length; c++)
      {
        var chain = chains[c];
        if (chain?.bones == null) continue;

        var indices = FingerLandmarkIndices[c];
        var segmentCount = Mathf.Min(chain.bones.Length, indices.Length - 1);

        for (var b = 0; b < segmentCount; b++)
        {
          var bone = chain.bones[b];
          if (bone == null) continue;

          var from = ToVector3(lm[indices[b]]);
          var to = ToVector3(lm[indices[b + 1]]);
          var worldDir = ConvertToUnityVector(to - from);
          if (worldDir.sqrMagnitude < 1e-8f) continue;

          var restForward = state.CurrentWristDelta * state.RestForwardWorld[c][b];
          var restRotation = state.CurrentWristDelta * state.RestRotations[c][b];
          var targetRotation = Quaternion.FromToRotation(restForward, worldDir.normalized) * restRotation;
          bone.rotation = Quaternion.Slerp(bone.rotation, targetRotation, _rotationSmoothing);
        }
      }
    }

    /// <summary>
    /// 手首ボーン自体の回転を、手首→中指付け根（前方向）と
    /// 人差し指付け根→小指付け根の外積（手のひらが向く方向）の2軸から求める。
    /// </summary>
    private void ApplyWristRotation(HandBoneRig rig, HandRetargetState state, System.Collections.Generic.List<Landmark> lm)
    {
      var wrist = ToVector3(lm[0]);
      var middleMcp = ToVector3(lm[9]);
      var indexMcp = ToVector3(lm[5]);
      var pinkyMcp = ToVector3(lm[17]);

      var forwardLandmark = middleMcp - wrist;
      var palmLandmark = Vector3.Cross(indexMcp - wrist, pinkyMcp - wrist);

      // 人差し指→小指の並び順は左右の手で鏡写しになるため、外積の符号も左右で反転する。
      // （左手のときにflipする。過去に動作実績のある実装と同じ条件。）
      var shouldFlip = !rig.isRightHand;
      if (rig.invertPalmDirection) shouldFlip = !shouldFlip;
      if (shouldFlip) palmLandmark = -palmLandmark;

      var worldForward = ConvertToUnityVector(forwardLandmark);
      var worldPalm = ConvertToUnityVector(palmLandmark);
      if (worldForward.sqrMagnitude < 1e-8f || worldPalm.sqrMagnitude < 1e-8f) return;

      var currentBasis = Quaternion.LookRotation(worldPalm, worldForward);
      var targetRotation = currentBasis * state.WristRestOffset;
      rig.wristRoot.rotation = Quaternion.Slerp(rig.wristRoot.rotation, targetRotation, _rotationSmoothing);

      state.CurrentWristDelta = rig.wristRoot.rotation * Quaternion.Inverse(state.WristRestRotationRaw);
    }

    /// <summary>
    /// 前腕ボーン（任意）の追従処理。MediaPipeのHandLandmarkerは肘の情報を持たないため、
    /// 前腕を独立して正しく曲げることはできない。代わりに、位置は手首にしっかり追従させて
    /// 関節が離れて見えないようにしつつ、回転だけ手首より遅らせて追従させることで、
    /// 「手首の部分で曲がっている」ように見える演出にする。
    /// </summary>
    private void ApplyForearmFollow(HandBoneRig rig)
    {
      if (rig.forearmBone == null || rig.wristRoot == null) return;

      rig.forearmBone.position = rig.wristRoot.position + rig.forearmPositionOffset;

      var t = 1f - Mathf.Exp(-_forearmFollowSpeed * Time.deltaTime);
      rig.forearmBone.rotation = Quaternion.Slerp(rig.forearmBone.rotation, rig.wristRoot.rotation, t);
    }

    private static Vector3 ToVector3(Landmark landmark) => new Vector3(landmark.x, landmark.y, landmark.z);

    /// <summary>
    /// MediaPipeランドマーク空間のベクトルを、Unityワールド空間の方向ベクトルに変換する。
    /// MediaPipeはX:右が正, Y:下が正、Zは値が小さいほどカメラに近い、という規約。
    /// UnityはY-upなのでYはデフォルトで反転する。X/Zは見た目がおかしい場合にインスペクタで切り替える。
    /// </summary>
    private Vector3 ConvertToUnityVector(Vector3 landmarkVector)
    {
      var x = _mirrorX ? -landmarkVector.x : landmarkVector.x;
      var y = _mirrorY ? landmarkVector.y : -landmarkVector.y;
      var z = _mirrorZ ? -landmarkVector.z : landmarkVector.z;
      return new Vector3(x, y, z);
    }

    /// <summary>
    /// 1つの手モデルのレストポーズ（Awake時点の回転）をキャッシュし、
    /// 毎フレームの絶対回転計算の基準として使う。
    /// </summary>
    private class HandRetargetState
    {
      public Quaternion[][] RestRotations;
      public Vector3[][] RestForwardWorld;
      public Quaternion WristRestRotationRaw;
      public Quaternion WristRestOffset;
      public Quaternion CurrentWristDelta = Quaternion.identity;

      public HandRetargetState(HandBoneRig rig)
      {
        var chains = new[] { rig.thumb, rig.index, rig.middle, rig.ring, rig.pinky };
        RestRotations = new Quaternion[chains.Length][];
        RestForwardWorld = new Vector3[chains.Length][];

        for (var c = 0; c < chains.Length; c++)
        {
          var boneCount = chains[c]?.bones?.Length ?? 0;
          RestRotations[c] = new Quaternion[boneCount];
          RestForwardWorld[c] = new Vector3[boneCount];

          for (var b = 0; b < boneCount; b++)
          {
            var bone = chains[c].bones[b];
            if (bone == null) continue;

            RestRotations[c][b] = bone.rotation;
            RestForwardWorld[c][b] = bone.TransformDirection(rig.boneLocalForwardAxis);
          }
        }

        if (rig.wristRoot != null)
        {
          WristRestRotationRaw = rig.wristRoot.rotation;
          var restForward = rig.wristRoot.TransformDirection(rig.boneLocalForwardAxis);
          var restPalm = rig.wristRoot.TransformDirection(rig.wristLocalPalmAxis);
          var basis = Quaternion.LookRotation(restPalm, restForward);
          WristRestOffset = Quaternion.Inverse(basis) * rig.wristRoot.rotation;
        }
      }
    }
  }
}
