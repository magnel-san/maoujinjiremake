using System;
using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Unity.Sample;
using UnityEngine;
using ImageProcessingOptions = Mediapipe.Tasks.Vision.Core.ImageProcessingOptions;
using VisionRunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// MediaPipe PoseLandmarkerを起動し、肩・肘・手首の検出結果を公開する。
  /// 3Dボーンへのリターゲットは行わない（肩/肘の見た目のリグは持たないため、生データをそのまま公開する）。
  ///
  /// 追加理由：パンチ（不採用）判定を手首のワールドZ位置や画面内サイズの拡大速度だけに頼ると、
  /// カメラへ向かって突き出す動きはMediaPipeのHandLandmarkerが最も苦手とする軸になってしまい、
  /// 検出漏れが解消しなかった。肩-肘-手首の「肘の伸展角度」は、プレイヤーがカメラからどれだけ
  /// 離れて座っているかに影響されない（角度は距離に対してスケール不変）ため、より頑健な特徴量になる。
  ///
  /// HandTrackingControllerと同じImageSourceを共有し、同じVIDEOモード＋TryDetectForVideoの
  /// 同期取得方式で動かす（過去に動作実績のある方式に統一）。
  ///
  /// 注意：肩・肘が映るよう、カメラのフレーミングを手だけでなく上半身が入るように調整する必要がある。
  /// </summary>
  public class PoseTrackingController : MonoBehaviour
  {
    [Header("MediaPipe設定")]
    [SerializeField] private BaseOptions.Delegate _delegate =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
      BaseOptions.Delegate.CPU;
#else
      BaseOptions.Delegate.GPU;
#endif
    [Tooltip("軽量(lite)・標準(full)・高精度(heavy)のいずれか。PC向けにはfullが精度/速度のバランスが良い。")]
    [SerializeField] private string _modelAssetPath = "pose_landmarker_full.bytes";
    [SerializeField, Range(0f, 1f)] private float _minPoseDetectionConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float _minPosePresenceConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float _minTrackingConfidence = 0.5f;

    [Header("MediaPipe初期化")]
    [Tooltip("シーンに\"Bootstrap\"という名前のGameObjectが無い場合に生成するプレハブ。" +
      "HandTrackingController側で既に生成されていれば、そちらが流用される。")]
    [SerializeField] private GameObject _bootstrapPrefab;

    private PoseLandmarker _taskApi;
    private TextureFramePool _textureFramePool;
    private Coroutine _runCoroutine;

    /// <summary>MediaPipeの検出結果が届くたびに発火する。GestureRecognizer等が購読する。</summary>
    public event Action<PoseLandmarkerResult> OnPoseLandmarkerResult;

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

      var options = new PoseLandmarkerOptions(
        new BaseOptions(_delegate, modelAssetPath: _modelAssetPath),
        runningMode: VisionRunningMode.VIDEO,
        numPoses: 1,
        minPoseDetectionConfidence: _minPoseDetectionConfidence,
        minPosePresenceConfidence: _minPosePresenceConfidence,
        minTrackingConfidence: _minTrackingConfidence);

      _taskApi = PoseLandmarker.CreateFromOptions(options, GpuManager.GpuResources);

      var imageSource = ImageSourceProvider.ImageSource;
      // HandTrackingController側で既に再生されていても、Play()の再呼び出しは無害
      // （準備済みなら即座に返る実装になっている）。
      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("[PoseTrackingController] Failed to start ImageSource.");
        yield break;
      }

      _textureFramePool = new TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      var stopwatch = Stopwatch.StartNew();
      var result = PoseLandmarkerResult.Alloc(1);

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
          Debug.LogWarning("[PoseTrackingController] Failed to read texture from the image source.");
          continue;
        }

        var image = textureFrame.BuildCPUImage();
        textureFrame.Release();

        if (_taskApi.TryDetectForVideo(image, stopwatch.ElapsedMilliseconds, imageProcessingOptions, ref result))
        {
          OnPoseLandmarkerResult?.Invoke(result);
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
          Debug.LogError("[PoseTrackingController] Bootstrapがシーンに無く、_bootstrapPrefabも未設定です。" +
            "Assets/MediaPipeUnity/Samples/Resources/Bootstrap.prefab をインスペクタで指定してください。");
        }
        obj = Instantiate(_bootstrapPrefab);
        obj.name = "Bootstrap";
        DontDestroyOnLoad(obj);
      }
      return obj.GetComponent<Bootstrap>();
    }
  }
}
