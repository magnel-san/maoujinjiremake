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

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// MediaPipe HandLandmarkerを起動し、検出結果（21ランドマーク×左右手）を
  /// 魔王の手モデル（HandBoneRig）にリターゲットする。
  /// 左手モデルのみをプレハブとして持ち、右手はスケールX反転でミラー生成する。
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

    [Header("リターゲット")]
    [Tooltip("MediaPipeワールド座標(m)から手モデルへのスケール係数")]
    [SerializeField] private float _worldLandmarkScale = 1f;
    [Tooltip("魔王の手モデルの表示可否（ゲーム世界に入るまでは非表示）")]
    [SerializeField] private bool _handsVisible;

    [Header("MediaPipe初期化")]
    [Tooltip("シーンに\"Bootstrap\"という名前のGameObjectが無い場合に生成するプレハブ。" +
      "Assets/MediaPipeUnity/Samples/Resources/Bootstrap.prefab を指定する。")]
    [SerializeField] private GameObject _bootstrapPrefab;

    private HandLandmarker _taskApi;
    private HandBoneRig _leftHandInstance;
    private HandBoneRig _rightHandInstance;
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

        _rightHandInstance = Instantiate(_leftHandPrefab, parent);
        _rightHandInstance.isRightHand = true;
        _rightHandInstance.name = "MaouHand_Right";
        var s = _rightHandInstance.transform.localScale;
        _rightHandInstance.transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
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

      var options = new HandLandmarkerOptions(
        new BaseOptions(_delegate, modelAssetPath: _modelAssetPath),
        runningMode: VisionRunningMode.LIVE_STREAM,
        numHands: _numHands,
        minHandDetectionConfidence: _minHandDetectionConfidence,
        minHandPresenceConfidence: _minHandPresenceConfidence,
        minTrackingConfidence: _minTrackingConfidence,
        resultCallback: OnMediaPipeResult);

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

      var startMillisec = (long)(Time.realtimeSinceStartupAsDouble * 1000);

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

        var timestampMillisec = (long)(Time.realtimeSinceStartupAsDouble * 1000) - startMillisec;
        _taskApi.DetectAsync(image, timestampMillisec, imageProcessingOptions);
      }
    }

    private void OnMediaPipeResult(HandLandmarkerResult result, Image image, long timestampMillisec)
    {
      // MediaPipeのコールバックはワーカースレッドから来る可能性があるため、
      // メインスレッドで処理するようにキューイングする。
      _pendingResult = result;
      _hasPendingResult = true;
    }

    private HandLandmarkerResult _pendingResult;
    private volatile bool _hasPendingResult;

    private void Update()
    {
      if (!_hasPendingResult) return;
      _hasPendingResult = false;

      OnHandLandmarkerResult?.Invoke(_pendingResult);
      RetargetToBones(_pendingResult);
    }

    private void RetargetToBones(HandLandmarkerResult result)
    {
      if (result.handWorldLandmarks == null) return;

      for (var i = 0; i < result.handWorldLandmarks.Count; i++)
      {
        var isRight = IsRightHand(result, i);
        var rig = isRight ? _rightHandInstance : _leftHandInstance;
        if (rig == null || rig.wristRoot == null) continue;

        ApplyLandmarksToRig(rig, result.handWorldLandmarks[i]);
      }
    }

    /// <summary>
    /// MediaPipeのhandedness分類は「画面に映る手」基準のため、
    /// セルフィー視点（フロントカメラ相当）を前提に判定する。
    /// </summary>
    private bool IsRightHand(HandLandmarkerResult result, int index)
    {
      if (result.handedness == null || index >= result.handedness.Count) return false;
      var classifications = result.handedness[index];
      if (classifications.categories == null || classifications.categories.Count == 0) return false;
      return classifications.categories[0].categoryName == "Right";
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

    private static readonly int[][] FingerLandmarkIndices =
    {
      new[] { 1, 2, 3, 4 },   // Thumb
      new[] { 5, 6, 7, 8 },   // Index
      new[] { 9, 10, 11, 12 },  // Middle
      new[] { 13, 14, 15, 16 }, // Ring
      new[] { 17, 18, 19, 20 }, // Pinky
    };

    private void ApplyLandmarksToRig(HandBoneRig rig, Landmarks worldLandmarks)
    {
      if (worldLandmarks.landmarks == null || worldLandmarks.landmarks.Count == 0) return;

      var wrist = worldLandmarks.landmarks[0];
      rig.wristRoot.localPosition = new Vector3(wrist.x, wrist.y, wrist.z) * _worldLandmarkScale;

      for (var f = 0; f < 5; f++)
      {
        var finger = rig.GetFinger(f);
        var indices = FingerLandmarkIndices[f];
        for (var b = 0; b < finger.bones.Length; b++)
        {
          var bone = finger.bones[b];
          if (bone == null) continue;
          var lm = worldLandmarks.landmarks[indices[b]];
          bone.localPosition = new Vector3(lm.x, lm.y, lm.z) * _worldLandmarkScale;
        }
      }
    }
  }
}
