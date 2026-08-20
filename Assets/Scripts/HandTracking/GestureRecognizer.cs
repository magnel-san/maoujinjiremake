using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 両手のワールドランドマークからランドマーク間の角度・距離・速度（フレーム差分）を計算し、
  /// 仕様書1.2の全ジェスチャーをステートマシンで検出してイベント発行する。
  /// 各ミニゲーム／UI側は<see cref="OnGestureDetected"/>を購読するだけでよく、
  /// HandTrackingControllerやMediaPipeの詳細を知る必要はない（疎結合）。
  ///
  /// NOTE: ここでの判定はヒューリスティックな近似実装であり、閾値は全てインスペクタで
  /// 調整可能にしてある。実機での動作確認をしながらチューニングすることを前提とする。
  ///
  /// 座標変換は<see cref="HandTrackingController.ConvertToUnityVector"/>を必ず経由する。
  /// 手モデルのボーン回転計算と同じ変換を通さないと、ミラー設定（Z軸反転等）が
  /// 手モデルとジェスチャー判定で食い違ってしまう。
  ///
  /// 誤検出・連続発火対策は大きく2段構えにしている。
  /// 1. 信号平滑化：<see cref="OneEuroFilter"/>で手首等の座標を平滑化してから速度・角度を計算する。
  ///    MediaPipeの生ランドマークは細かくジッターするため、平滑化しないとノイズが一瞬の「速い動き」
  ///    として誤検出される。動き出しの追従を犠牲にしないよう、静止時ほど強く平滑化する適応型フィルタを使う。
  /// 2. ステートマシン：
  ///    - 「ポーズが成立している間」を条件にする判定（輪っか・腕クロス・頭上・両手を合わせる）は、
  ///      一定時間の保持(dwell)を要求し、発火後はポーズが崩れる(release)まで再発火しないエッジトリガー方式。
  ///    - 「振る」系の一発物（拍手・スワイプ・羽ばたき・両腕振り・ハンマー・パンチ）は<see cref="SwingDetector"/>で、
  ///      速度がしきい値を超えて立ち上がり、ピークを迎えて下降し始めた瞬間に1回だけ発火し、
  ///      手が収まる(exit)まで再アームしない（単発の閾値超えでの即時発火・連続発火を防ぐ）。
  ///    - 連続回転（バルブ回し）だけは「止まるまで再発火しない」方式と相性が悪いため、
  ///      <see cref="RotationTracker"/>で実際に回転した角度を積算し、一定角度ごとに繰り返し発火する。
  ///
  /// パンチ（不採用の意思表示）だけは特殊で、手首の速度・画面内サイズの拡大速度など
  /// カメラへ向かう動き自体を直接測る方式を色々試したが、いずれもMediaPipeが不得意とする軸
  /// （奥行き、または見た目のサイズはプレイヤーがカメラからどれだけ離れて座っているかに依存する）に
  /// 頼らざるを得なかった。代わりに<see cref="PoseTrackingController"/>から肩・肘・手首を取得し、
  /// 「肘の伸展角度（肩-肘-手首のなす角）」の変化速度で判定する。角度はプレイヤーの座る位置に
  /// 依存しないスケール不変な特徴量であり、より頑健。
  /// </summary>
  public class GestureRecognizer : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;
    [Tooltip("パンチ（不採用）判定の肘伸展角度に使う。未設定の場合、パンチは検出されなくなる。")]
    [SerializeField] private PoseTrackingController _poseTrackingController;

    [Header("信号平滑化（One Euro Filter）")]
    [Tooltip("静止時のジッター抑制の強さ。小さいほど滑らかになるが、動き出しの追従が遅れる。")]
    [SerializeField] private float _filterMinCutoff = 1.0f;
    [Tooltip("速い動きへの追従性。大きいほど素早い動きの遅延が減るが、その分ノイズ抑制が弱まる。")]
    [SerializeField] private float _filterBeta = 0.5f;

    [Header("共通しきい値")]
    [Tooltip("同一ジェスチャーの連続発火を防ぐクールダウン秒数")]
    [SerializeField] private float _defaultCooldown = 0.4f;
    [Tooltip("拳（グー）と判定する、指先→手首の平均距離のしきい値（m）")]
    [SerializeField] private float _fistDistanceThreshold = 0.09f;
    [Tooltip("速い動きとみなす速度のしきい値（m/s）")]
    [SerializeField] private float _fastVelocityThreshold = 1.2f;
    [Tooltip("近接（タッチ等）とみなす距離のしきい値（m）")]
    [SerializeField] private float _touchDistanceThreshold = 0.04f;
    [Tooltip("胸の高さとみなす、カメラ画面内での高さの範囲（0=下端 1=上端）")]
    [SerializeField, Range(0f, 1f)] private float _chestViewportYMin = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _chestViewportYMax = 0.65f;
    [Tooltip("ハンマーの「振り上げ済み」とみなす、カメラ画面内での高さ（0=下端 1=上端）")]
    [SerializeField, Range(0f, 1f)] private float _hammerRaisedViewportY = 0.55f;
    [Tooltip("HandsTogether判定に必要な保持秒数")]
    [SerializeField] private float _handsTogetherHoldSeconds = 0.3f;

    [Header("腕クロス（不採用の意思表示）")]
    [Tooltip("腕クロスと判定するまでの保持秒数（誤検出防止）")]
    [SerializeField] private float _armsCrossHoldSeconds = 0.25f;

    [Header("パンチ（右手突き出し／両拳交互）：肘の伸展角度で判定")]
    [Tooltip("肘の角度増加を測る時間窓（秒）")]
    [SerializeField] private float _punchWindowSeconds = 0.3f;
    [Tooltip("パンチとみなす、肘が伸びる速さ（度/秒）。数値を上げるほど、より素早く突き出さないと発火しなくなる。" +
      "反応しない場合はまずこの値を下げて確認する。" +
      "手首の奥行き(Z)や見た目のサイズはMediaPipeの推定誤差やプレイヤーの座る距離に影響されるため使わず、" +
      "肩-肘-手首のなす角（距離に依存しないスケール不変な特徴量）の変化速度で判定する。")]
    [SerializeField] private float _punchAngleSpeedThreshold = 250f;
    [Tooltip("パンチとみなす、時間窓内での肘の角度の最小増加量（度）。" +
      "速度だけでなく実際に大きく腕を伸ばしたことも要求することで、小さな動きでの誤発火を防ぐ。")]
    [SerializeField] private float _punchMinAngleIncrease = 35f;

    [Header("スイング系ジェスチャー共通（速度ピーク検出）")]
    [Tooltip("拍手・スワイプ・羽ばたき・両腕振り・ハンマー・パンチに共通の再アーム条件。" +
      "各しきい値に対する比率（0〜1）。ピーク検出後、速度がこの比率まで収まって初めて次の発火を受け付ける。" +
      "大きいほど「振り切って完全に止まる」ことを要求するようになり、小さいほど素早い連続動作を許可しやすくなる。")]
    [SerializeField, Range(0.05f, 0.9f)] private float _swingExitRatio = 0.35f;

    [Header("バルブ回し（連続回転）")]
    [Tooltip("1回発火とみなす累積回転角度（度）。小さいほど速く連射され、大きいほどしっかり回す必要がある。")]
    [SerializeField] private float _valveRotationDegreesToFire = 300f;
    [Tooltip("回転とみなす最低速度（m/s）。これより遅い動きは角度を積算しない（静止時のノイズ対策）")]
    [SerializeField] private float _valveMinSpeed = 0.3f;

    public event Action<GestureType> OnGestureDetected;

    private readonly Dictionary<GestureType, float> _cooldownUntil = new Dictionary<GestureType, float>();

    private HandFrame _left = HandFrame.Empty;
    private HandFrame _right = HandFrame.Empty;
    private HandFrame _prevLeft = HandFrame.Empty;
    private HandFrame _prevRight = HandFrame.Empty;

    private HandFilterSet _leftFilter;
    private HandFilterSet _rightFilter;

    private PoseFrame _pose = PoseFrame.Empty;
    private PoseFrame _prevPose = PoseFrame.Empty;
    private OneEuroFilter _rightElbowAngleFilter;
    private OneEuroFilter _leftElbowAngleFilter;

    private float _handsTogetherTimer;
    private bool _handsTogetherArmed = true;

    private float _armsCrossHoldTimer;
    private bool _armsCrossArmed = true;

    private readonly AngleTrace _rightElbowTrace = new AngleTrace();
    private readonly AngleTrace _leftElbowTrace = new AngleTrace();

    private SwingDetector _rightSwipeDetector;
    private SwingDetector _leftSwipeDetector;
    private SwingDetector _wingFlapDetector;
    private SwingDetector _armSwingDetector;
    private SwingDetector _rightHammerDetector;
    private SwingDetector _leftHammerDetector;
    private SwingDetector _rightPunchDetector;
    private SwingDetector _leftPunchDetector;
    private RotationTracker _rightValveTracker;
    private RotationTracker _leftValveTracker;

    private void Awake()
    {
      _leftFilter = new HandFilterSet(_filterMinCutoff, _filterBeta);
      _rightFilter = new HandFilterSet(_filterMinCutoff, _filterBeta);
      _rightElbowAngleFilter = new OneEuroFilter(_filterMinCutoff, _filterBeta);
      _leftElbowAngleFilter = new OneEuroFilter(_filterMinCutoff, _filterBeta);

      _rightSwipeDetector = new SwingDetector(_fastVelocityThreshold, _swingExitRatio);
      _leftSwipeDetector = new SwingDetector(_fastVelocityThreshold, _swingExitRatio);
      _wingFlapDetector = new SwingDetector(_fastVelocityThreshold, _swingExitRatio);
      _armSwingDetector = new SwingDetector(_fastVelocityThreshold, _swingExitRatio);
      _rightHammerDetector = new SwingDetector(_fastVelocityThreshold, _swingExitRatio);
      _leftHammerDetector = new SwingDetector(_fastVelocityThreshold, _swingExitRatio);
      _rightPunchDetector = new SwingDetector(_punchAngleSpeedThreshold, _swingExitRatio);
      _leftPunchDetector = new SwingDetector(_punchAngleSpeedThreshold, _swingExitRatio);
      _rightValveTracker = new RotationTracker(_valveMinSpeed, _valveRotationDegreesToFire);
      _leftValveTracker = new RotationTracker(_valveMinSpeed, _valveRotationDegreesToFire);
    }

    /// <summary>直近の短い時間窓での肘の伸展角度を記録し、実際にどれだけ増加したかを求める。
    /// 瞬間の角速度だけで判定すると、小さな腕の動きでも一瞬だけ角速度が出て誤発火するため、
    /// 「大きく腕を伸ばす」動作を要求するパンチ判定にはこちらを使う。</summary>
    private class AngleTrace
    {
      private readonly List<(float angle, float time)> _points = new List<(float, float)>();

      public void Add(float angle, float time, float windowSeconds)
      {
        _points.Add((angle, time));
        while (_points.Count > 1 && time - _points[0].time > windowSeconds)
        {
          _points.RemoveAt(0);
        }
      }

      public void Clear() => _points.Clear();

      /// <summary>時間窓内での肘角度の増加量（度）。</summary>
      public float GetIncrease()
      {
        if (_points.Count < 2) return 0f;
        return _points[_points.Count - 1].angle - _points[0].angle;
      }
    }

    /// <summary>
    /// One Euro Filter（適応型ローパスフィルタ）。MediaPipeランドマークの細かいジッターを抑えつつ、
    /// 実際に速く動いた時は遅延を増やさず追従する（静止時は強く平滑化、動き出したら追従優先に切り替わる）。
    /// 速度・角度の計算前段でこれを通すことで、ノイズによる瞬間的なスパイクが誤発火の原因になるのを防ぐ。
    /// </summary>
    private class OneEuroFilter
    {
      private readonly float _minCutoff;
      private readonly float _beta;
      private readonly float _dCutoff;
      private bool _initialized;
      private float _prevValue;
      private float _prevDerivative;
      private float _prevTime;

      public OneEuroFilter(float minCutoff, float beta, float dCutoff = 1f)
      {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
      }

      public float Filter(float value, float time)
      {
        if (!_initialized)
        {
          _initialized = true;
          _prevValue = value;
          _prevDerivative = 0f;
          _prevTime = time;
          return value;
        }

        var dt = Mathf.Max(time - _prevTime, 0.0001f);
        var derivative = (value - _prevValue) / dt;
        var dAlpha = Alpha(_dCutoff, dt);
        var smoothedDerivative = dAlpha * derivative + (1f - dAlpha) * _prevDerivative;

        var cutoff = _minCutoff + _beta * Mathf.Abs(smoothedDerivative);
        var alpha = Alpha(cutoff, dt);
        var filtered = alpha * value + (1f - alpha) * _prevValue;

        _prevValue = filtered;
        _prevDerivative = smoothedDerivative;
        _prevTime = time;
        return filtered;
      }

      private static float Alpha(float cutoff, float dt)
      {
        var tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 0.0001f));
        return 1f / (1f + tau / dt);
      }
    }

    /// <summary>OneEuroFilterをXYZ独立に適用するVector3版。2D値（画面座標）はzに0を渡して流用する。</summary>
    private class Vector3Filter
    {
      private readonly OneEuroFilter _x;
      private readonly OneEuroFilter _y;
      private readonly OneEuroFilter _z;

      public Vector3Filter(float minCutoff, float beta)
      {
        _x = new OneEuroFilter(minCutoff, beta);
        _y = new OneEuroFilter(minCutoff, beta);
        _z = new OneEuroFilter(minCutoff, beta);
      }

      public Vector3 Filter(Vector3 v, float time) =>
        new Vector3(_x.Filter(v.x, time), _y.Filter(v.y, time), _z.Filter(v.z, time));
    }

    /// <summary>片手ぶんの平滑化フィルタ一式。手首・指先・画面内高さを持つ。</summary>
    private class HandFilterSet
    {
      public readonly Vector3Filter Wrist;
      public readonly Vector3Filter[] FingerTips;
      public readonly OneEuroFilter ViewportY;

      public HandFilterSet(float minCutoff, float beta)
      {
        Wrist = new Vector3Filter(minCutoff, beta);
        FingerTips = new[]
        {
          new Vector3Filter(minCutoff, beta), new Vector3Filter(minCutoff, beta), new Vector3Filter(minCutoff, beta),
          new Vector3Filter(minCutoff, beta), new Vector3Filter(minCutoff, beta),
        };
        ViewportY = new OneEuroFilter(minCutoff, beta);
      }
    }

    /// <summary>
    /// 「振る」系ジェスチャー共通のステートマシン。特徴量（速度等、常に0以上を想定）がしきい値を超えて
    /// 立ち上がり、ピークを迎えて下降し始めた瞬間に1回だけtrueを返す。発火後は特徴量が退場しきい値まで
    /// 収まる（＝手が実質止まる/戻る）まで再アームしない。単発の閾値超えで即発火する方式と違い、
    /// ノイズによる瞬間的なスパイクにも、1回の振りの間に複数回発火することにも強い。
    /// </summary>
    private class SwingDetector
    {
      private readonly float _threshold;
      private readonly float _exitThreshold;
      private bool _rising;
      private bool _armed = true;
      private float _peak;

      public SwingDetector(float threshold, float exitRatio)
      {
        _threshold = threshold;
        _exitThreshold = threshold * Mathf.Clamp01(exitRatio);
      }

      /// <summary>毎フレーム特徴量を渡す。canEnterがfalseの間は新規の立ち上がりを開始しない
      /// （例：拳の形でない、振り上げ済みでない、等の追加条件をかけるのに使う）。</summary>
      public bool Update(float feature, bool canEnter = true)
      {
        if (!_armed)
        {
          if (feature < _exitThreshold) _armed = true;
          return false;
        }

        if (!_rising)
        {
          if (canEnter && feature > _threshold)
          {
            _rising = true;
            _peak = feature;
          }
          return false;
        }

        if (feature >= _peak)
        {
          _peak = feature;
          return false;
        }

        // ピークを超えて下降し始めた＝1回の振りが完了した瞬間。
        _rising = false;
        _armed = false;
        return true;
      }

      public void Reset()
      {
        _rising = false;
        _armed = true;
        _peak = 0f;
      }
    }

    /// <summary>
    /// 連続回転（バルブ回し）用。速度ベクトルの向きの変化を積算し、一定角度分回転したら1回発火する。
    /// スイング系のように「一度止まるまで再発火しない」方式は、回し続ける動作とは相性が悪い（止まらないため）
    /// ので、代わりに実際に回転した量に応じて繰り返し発火する方式にしている。
    /// </summary>
    private class RotationTracker
    {
      private readonly float _minSpeed;
      private readonly float _degreesToFire;
      private bool _hasPrevAngle;
      private float _prevAngle;
      private float _accumulatedDegrees;

      public RotationTracker(float minSpeed, float degreesToFire)
      {
        _minSpeed = minSpeed;
        _degreesToFire = Mathf.Max(degreesToFire, 10f);
      }

      public bool Update(Vector2 velocity)
      {
        if (velocity.magnitude < _minSpeed)
        {
          _hasPrevAngle = false; // 遅い/静止時は角度追跡をリセットし、ノイズでの積算を防ぐ
          return false;
        }

        var angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        if (!_hasPrevAngle)
        {
          _prevAngle = angle;
          _hasPrevAngle = true;
          return false;
        }

        _accumulatedDegrees += Mathf.Abs(Mathf.DeltaAngle(_prevAngle, angle));
        _prevAngle = angle;

        if (_accumulatedDegrees >= _degreesToFire)
        {
          _accumulatedDegrees = 0f;
          return true;
        }
        return false;
      }

      public void Reset()
      {
        _hasPrevAngle = false;
        _accumulatedDegrees = 0f;
      }
    }

    private struct HandFrame
    {
      public bool valid;
      public Vector3 wrist;
      public Vector3[] fingerTips; // Thumb, Index, Middle, Ring, Pinky
      /// <summary>カメラ画面内での手首の高さ（0=下端 1=上端）。頭上/胸の高さ等、絶対的な高さの判定に使う。
      /// ワールドランドマークは手自体を原点とする相対座標のため、絶対的な高さの判定には使えない。</summary>
      public float viewportY;
      public float timestamp;

      public static HandFrame Empty => new HandFrame { valid = false, fingerTips = new Vector3[5] };

      public float AverageFistDistance()
      {
        var sum = 0f;
        for (var i = 0; i < fingerTips.Length; i++) sum += Vector3.Distance(fingerTips[i], wrist);
        return sum / fingerTips.Length;
      }
    }

    /// <summary>PoseLandmarkerから抽出した、パンチ判定に必要な最小限のデータ（左右の肘伸展角度のみ）。</summary>
    private struct PoseFrame
    {
      public bool valid;
      public bool rightArmValid;
      public float rightElbowAngle;
      public bool leftArmValid;
      public float leftElbowAngle;
      public float timestamp;

      public static PoseFrame Empty => new PoseFrame { valid = false };
    }

    private void OnEnable()
    {
      if (_handTrackingController != null)
      {
        _handTrackingController.OnHandLandmarkerResult += HandleResult;
      }
      if (_poseTrackingController != null)
      {
        _poseTrackingController.OnPoseLandmarkerResult += HandlePoseResult;
      }
    }

    private void OnDisable()
    {
      if (_handTrackingController != null)
      {
        _handTrackingController.OnHandLandmarkerResult -= HandleResult;
      }
      if (_poseTrackingController != null)
      {
        _poseTrackingController.OnPoseLandmarkerResult -= HandlePoseResult;
      }
    }

    private void HandleResult(HandLandmarkerResult result)
    {
      if (result.handWorldLandmarks == null) return;

      _prevLeft = _left;
      _prevRight = _right;

      var newLeft = HandFrame.Empty;
      var newRight = HandFrame.Empty;

      for (var i = 0; i < result.handWorldLandmarks.Count; i++)
      {
        var isRight = IsRightHand(result, i);

        List<NormalizedLandmark> normalizedLandmarks = null;
        if (result.handLandmarks != null && i < result.handLandmarks.Count)
        {
          normalizedLandmarks = result.handLandmarks[i].landmarks;
        }

        var frame = BuildFrame(result.handWorldLandmarks[i], normalizedLandmarks, isRight ? _rightFilter : _leftFilter);
        if (isRight) newRight = frame; else newLeft = frame;
      }

      var leftWasValid = _left.valid;
      var rightWasValid = _right.valid;

      _left = newLeft.valid ? newLeft : HandFrame.Empty;
      _right = newRight.valid ? newRight : HandFrame.Empty;

      // 手がロストした時は「振り」の途中状態を持ち越さない（再検出時に誤ってピーク完了扱いされるのを防ぐ）。
      if (leftWasValid && !_left.valid)
      {
        _leftSwipeDetector.Reset();
        _wingFlapDetector.Reset();
        _armSwingDetector.Reset();
        _leftHammerDetector.Reset();
        _leftPunchDetector.Reset();
        _leftValveTracker.Reset();
      }
      if (rightWasValid && !_right.valid)
      {
        _rightSwipeDetector.Reset();
        _wingFlapDetector.Reset();
        _armSwingDetector.Reset();
        _rightHammerDetector.Reset();
        _rightPunchDetector.Reset();
        _rightValveTracker.Reset();
      }

      Evaluate();
    }

    /// <summary>
    /// PoseLandmarkerの検出結果が届くたびに呼ばれる。HandLandmarker側とは別の非同期ループなので、
    /// パンチ判定（<see cref="DetectAlternatingPunch"/>）はここで駆動する
    /// （<see cref="Evaluate"/>はHandLandmarker側のフレームレートで動くため）。
    /// </summary>
    private void HandlePoseResult(PoseLandmarkerResult result)
    {
      _prevPose = _pose;
      _pose = BuildPoseFrame(result);

      if (_pose.rightArmValid) _rightElbowTrace.Add(_pose.rightElbowAngle, _pose.timestamp, _punchWindowSeconds);
      else { _rightElbowTrace.Clear(); _rightPunchDetector.Reset(); }

      if (_pose.leftArmValid) _leftElbowTrace.Add(_pose.leftElbowAngle, _pose.timestamp, _punchWindowSeconds);
      else { _leftElbowTrace.Clear(); _leftPunchDetector.Reset(); }

      DetectAlternatingPunch();
    }

    private PoseFrame BuildPoseFrame(PoseLandmarkerResult result)
    {
      if (result.poseLandmarks == null || result.poseLandmarks.Count == 0) return PoseFrame.Empty;

      var lm = result.poseLandmarks[0].landmarks;
      if (lm == null || lm.Count < 17) return PoseFrame.Empty;

      var time = Time.time;
      var frame = new PoseFrame { valid = true, timestamp = time };

      // MediaPipeのPose関節ラベルは「入力画像が鏡像である」ことを前提にLeft/Rightが割り振られている。
      // HandLandmarkerの左右反転（IsRightHand参照）と同じ理由で、このパイプラインは鏡像化していないため、
      // MediaPipeが"left_*"と呼ぶ側（11:肩,13:肘,15:手首）が実際のプレイヤーの右腕になる。
      if (TryComputeElbowAngle(lm, 11, 13, 15, out var rawRight))
      {
        frame.rightArmValid = true;
        frame.rightElbowAngle = _rightElbowAngleFilter.Filter(rawRight, time);
      }
      if (TryComputeElbowAngle(lm, 12, 14, 16, out var rawLeft))
      {
        frame.leftArmValid = true;
        frame.leftElbowAngle = _leftElbowAngleFilter.Filter(rawLeft, time);
      }

      return frame;
    }

    /// <summary>肩-肘-手首のなす角（度）を、画面内の正規化座標(X,Y)だけから求める。
    /// 奥行き(Z)を使わないため、MediaPipeのZ推定誤差の影響を受けない。</summary>
    private static bool TryComputeElbowAngle(List<NormalizedLandmark> lm, int shoulderIdx, int elbowIdx, int wristIdx, out float angleDegrees)
    {
      var shoulder = new Vector2(lm[shoulderIdx].x, lm[shoulderIdx].y);
      var elbow = new Vector2(lm[elbowIdx].x, lm[elbowIdx].y);
      var wrist = new Vector2(lm[wristIdx].x, lm[wristIdx].y);

      var toShoulder = shoulder - elbow;
      var toWrist = wrist - elbow;
      if (toShoulder.sqrMagnitude < 1e-8f || toWrist.sqrMagnitude < 1e-8f)
      {
        angleDegrees = 0f;
        return false;
      }

      angleDegrees = Vector2.Angle(toShoulder, toWrist);
      return true;
    }

    /// MediaPipeのhandedness分類は「入力画像が鏡像である」ことを前提とするため、
    /// このプロジェクトのパイプラインでは実際の手と左右が逆になる。HandTrackingControllerと
    /// 同じく判定を反転させ、両者で左右の扱いを一致させる。
    private bool IsRightHand(HandLandmarkerResult result, int index)
    {
      if (result.handedness == null || index >= result.handedness.Count) return false;
      var classifications = result.handedness[index];
      if (classifications.categories == null || classifications.categories.Count == 0) return false;
      return classifications.categories[0].categoryName == "Left";
    }

    private HandFrame BuildFrame(Landmarks worldLandmarks, List<NormalizedLandmark> normalizedLandmarks, HandFilterSet filters)
    {
      if (worldLandmarks.landmarks == null || worldLandmarks.landmarks.Count < 21) return HandFrame.Empty;
      if (_handTrackingController == null) return HandFrame.Empty;

      var lm = worldLandmarks.landmarks;
      Vector3 V(int i) => _handTrackingController.ConvertToUnityVector(new Vector3(lm[i].x, lm[i].y, lm[i].z));

      var time = Time.time;
      var frame = new HandFrame
      {
        valid = true,
        wrist = filters.Wrist.Filter(V(0), time),
        fingerTips = new[]
        {
          filters.FingerTips[0].Filter(V(4), time),
          filters.FingerTips[1].Filter(V(8), time),
          filters.FingerTips[2].Filter(V(12), time),
          filters.FingerTips[3].Filter(V(16), time),
          filters.FingerTips[4].Filter(V(20), time),
        },
        viewportY = 0.5f,
        timestamp = time,
      };

      if (normalizedLandmarks != null && normalizedLandmarks.Count > 0)
      {
        frame.viewportY = filters.ViewportY.Filter(1f - normalizedLandmarks[0].y, time);
      }

      return frame;
    }

    private void Evaluate()
    {
      var dt = Mathf.Max(Time.deltaTime, 0.0001f);

      DetectArmsCross(dt);
      DetectHorizontalSwipes(dt);
      DetectWingFlap(dt);
      DetectArmSwingBoth(dt);
      DetectHammerSwingDown(dt);
      // DetectAlternatingPunchはHandlePoseResult側（PoseLandmarkerの更新頻度）で駆動する。
      DetectValveSpin(dt);
      DetectHandsTogether(dt);
    }

    private bool TryFire(GestureType type)
    {
      if (_cooldownUntil.TryGetValue(type, out var until) && Time.time < until) return false;
      _cooldownUntil[type] = Time.time + _defaultCooldown;
      OnGestureDetected?.Invoke(type);
      return true;
    }

    private bool IsFist(in HandFrame hand) => hand.valid && hand.AverageFistDistance() < _fistDistanceThreshold;

    private Vector3 Velocity(in HandFrame current, in HandFrame previous, float dt)
    {
      if (!current.valid || !previous.valid) return Vector3.zero;
      return (current.wrist - previous.wrist) / dt;
    }

    // 胸の前で腕をクロス（左右の手首が体の中心線をまたいで入れ替わる）。
    // 保持時間(dwell)＋崩れるまで再発火しない(release)方式で誤検出・連続発火を防ぐ。
    private void DetectArmsCross(float dt)
    {
      if (!_left.valid || !_right.valid)
      {
        _armsCrossHoldTimer = 0f;
        _armsCrossArmed = true;
        return;
      }

      var crossed = _left.wrist.x > _right.wrist.x + 0.02f;
      if (!crossed)
      {
        _armsCrossHoldTimer = 0f;
        _armsCrossArmed = true;
        return;
      }

      _armsCrossHoldTimer += dt;
      if (_armsCrossArmed && _armsCrossHoldTimer >= _armsCrossHoldSeconds)
      {
        if (TryFire(GestureType.ArmsCross)) _armsCrossArmed = false;
      }
    }

    // 手で横に払う／左右への腕振り。水平速度が主成分としてピークを迎えた瞬間を1回とする。
    private void DetectHorizontalSwipes(float dt)
    {
      EvaluateSwipe(_right, _prevRight, dt, _rightSwipeDetector);
      EvaluateSwipe(_left, _prevLeft, dt, _leftSwipeDetector);
    }

    private void EvaluateSwipe(in HandFrame hand, in HandFrame prev, float dt, SwingDetector detector)
    {
      if (!hand.valid || !prev.valid)
      {
        detector.Reset();
        return;
      }

      var vel = Velocity(hand, prev, dt);
      var dominant = Mathf.Abs(vel.x) > Mathf.Abs(vel.y) && Mathf.Abs(vel.x) > Mathf.Abs(vel.z);

      if (detector.Update(Mathf.Abs(vel.x), dominant))
      {
        TryFire(GestureType.SwipeSideways);
        if (vel.x > 0) TryFire(GestureType.SwipeLeftToRight);
        else TryFire(GestureType.SwipeRightToLeft);
      }
    }

    // 腕を翼のように上下に振る。両手首の垂直速度が同符号で、そのピークを迎えた瞬間を1回とする。
    private void DetectWingFlap(float dt)
    {
      if (!_left.valid || !_right.valid)
      {
        _wingFlapDetector.Reset();
        return;
      }

      var lv = Velocity(_left, _prevLeft, dt);
      var rv = Velocity(_right, _prevRight, dt);
      var matchedDirection = Mathf.Sign(lv.y) == Mathf.Sign(rv.y);
      var feature = matchedDirection ? Mathf.Min(Mathf.Abs(lv.y), Mathf.Abs(rv.y)) : 0f;

      if (_wingFlapDetector.Update(feature))
      {
        TryFire(GestureType.WingFlap);
      }
    }

    // 両腕を振る（前後方向、ランニングのように腕を振る動き）。ピークを迎えた瞬間を1回とする。
    private void DetectArmSwingBoth(float dt)
    {
      if (!_left.valid || !_right.valid)
      {
        _armSwingDetector.Reset();
        return;
      }

      var lv = Velocity(_left, _prevLeft, dt);
      var rv = Velocity(_right, _prevRight, dt);
      var feature = Mathf.Min(Mathf.Abs(lv.z), Mathf.Abs(rv.z));

      if (_armSwingDetector.Update(feature))
      {
        TryFire(GestureType.ArmSwingBoth);
      }
    }

    // 腕を振り下ろす（ハンマー打ち）：振り上げていた（画面上部にあった）手が急速に下降し、
    // 下降速度がピークを迎えた瞬間を1回とする。
    private void DetectHammerSwingDown(float dt)
    {
      EvaluateHammer(_right, _prevRight, dt, _rightHammerDetector);
      EvaluateHammer(_left, _prevLeft, dt, _leftHammerDetector);
    }

    private void EvaluateHammer(in HandFrame hand, in HandFrame prev, float dt, SwingDetector detector)
    {
      if (!hand.valid || !prev.valid)
      {
        detector.Reset();
        return;
      }

      var vel = Velocity(hand, prev, dt);
      var downwardSpeed = Mathf.Max(-vel.y, 0f);
      var wasRaised = prev.viewportY > _hammerRaisedViewportY;
      // 縦方向優勢（横/奥行き方向より下向きが勝っている）motionだけをハンマーとして受け付ける。
      // これが無いと、前方への突き出し（パンチ）に多少の下向き成分が混ざっただけで
      // ハンマー側も同時に反応してしまう。
      var dominantlyVertical = Mathf.Abs(vel.y) >= Mathf.Abs(vel.z);
      var canEnter = wasRaised && dominantlyVertical;

      if (detector.Update(downwardSpeed, canEnter))
      {
        TryFire(GestureType.HammerSwingDown);
      }
    }

    // 両拳を交互に突き出す（パンチ）：拳の形で肘が伸びる速さ（角速度）がピークを迎えた瞬間を1回とし、
    // さらに直近の短い時間で実際に大きく腕を伸ばしたこと（角度の増加量）も要求する。
    //
    // カメラへ向かう動き（Z方向）はMediaPipeのワールド座標のZ(奥行き)推定が特に乱れやすい
    // （速い動きで縮む/伸びる既知の問題がある）ため使わない。画面内サイズの拡大速度も
    // プレイヤーがカメラからどれだけ離れて座っているかに応じて感度が変わってしまうため採用しなかった。
    // 代わりに肩-肘-手首のなす角（PoseTrackingController経由）を使う。角度は距離に対してスケール不変
    // （プレイヤーがどれだけ離れて座っていても同じ角度になる）なため、より頑健な唯一の特徴量にできる。
    // PoseLandmarkerの更新はHandLandmarkerとは非同期のため、HandlePoseResultから駆動する。
    private void DetectAlternatingPunch()
    {
      var rightFired = EvaluatePunch(_right, _prevRight, _pose.rightArmValid, _pose.rightElbowAngle,
        _prevPose.rightArmValid, _prevPose.rightElbowAngle, _rightPunchDetector, _rightElbowTrace);
      var leftFired = EvaluatePunch(_left, _prevLeft, _pose.leftArmValid, _pose.leftElbowAngle,
        _prevPose.leftArmValid, _prevPose.leftElbowAngle, _leftPunchDetector, _leftElbowTrace);

      if (rightFired || leftFired)
      {
        TryFire(GestureType.AlternatingPunch);
      }
    }

    private bool EvaluatePunch(in HandFrame hand, in HandFrame prevHand, bool armValid, float angle, bool prevArmValid, float prevAngle,
      SwingDetector detector, AngleTrace trace)
    {
      if (!armValid || !prevArmValid)
      {
        detector.Reset();
        return false;
      }

      var dt = Mathf.Max(_pose.timestamp - _prevPose.timestamp, 0.0001f);
      var angleSpeed = Mathf.Max((angle - prevAngle) / dt, 0f);

      // ハンマー（縦振り）との取り違え防止：手が縦方向優勢に下降している最中はパンチの立ち上がりを許可しない。
      // 肘の伸展角度だけでは「振り下ろして腕が伸びる」と「突き出して腕が伸びる」を区別できないため、
      // 生の手首移動方向で補強する。
      var dominantlyDownward = hand.valid && prevHand.valid
        && Mathf.Abs(hand.wrist.y - prevHand.wrist.y) > Mathf.Abs(hand.wrist.z - prevHand.wrist.z)
        && hand.wrist.y < prevHand.wrist.y;
      var canEnter = IsFist(hand) && !dominantlyDownward;

      if (!detector.Update(angleSpeed, canEnter)) return false;

      var angleIncreaseOk = trace.GetIncrease() > _punchMinAngleIncrease;
      trace.Clear();
      return angleIncreaseOk;
    }

    // 胸の前でぐるぐるバルブを回す：胸の高さで実際に回転した角度を積算し、一定角度ごとに繰り返し発火する。
    private void DetectValveSpin(float dt)
    {
      EvaluateValve(_right, _prevRight, dt, _rightValveTracker);
      EvaluateValve(_left, _prevLeft, dt, _leftValveTracker);
    }

    private void EvaluateValve(in HandFrame hand, in HandFrame prev, float dt, RotationTracker tracker)
    {
      if (!hand.valid || !prev.valid || hand.viewportY < _chestViewportYMin || hand.viewportY > _chestViewportYMax)
      {
        tracker.Reset();
        return;
      }

      var vel = Velocity(hand, prev, dt);
      if (tracker.Update(new Vector2(vel.x, vel.y)))
      {
        // 回転量そのものが実際の動作量に比例した自然なレート制限になっているため、
        // 他ジェスチャー共通のクールダウン（TryFire）は経由せず直接発火する。
        // 共通クールダウンを通すと、素早く回している時に上限を掛けてしまい「速く回すほど発火が増える」感触を損なう。
        OnGestureDetected?.Invoke(GestureType.ValveSpin);
      }
    }

    // 両手を前で合わせる：両手首が近接した状態を一定時間保持。
    // 保持時間(dwell)＋崩れるまで再発火しない(release)方式で連続発火を防ぐ。
    private void DetectHandsTogether(float dt)
    {
      if (!_left.valid || !_right.valid)
      {
        _handsTogetherTimer = 0f;
        _handsTogetherArmed = true;
        return;
      }

      var dist = Vector3.Distance(_left.wrist, _right.wrist);
      if (dist < _touchDistanceThreshold * 2f)
      {
        _handsTogetherTimer += dt;
        if (_handsTogetherArmed && _handsTogetherTimer >= _handsTogetherHoldSeconds)
        {
          if (TryFire(GestureType.HandsTogether)) _handsTogetherArmed = false;
        }
      }
      else
      {
        _handsTogetherTimer = 0f;
        _handsTogetherArmed = true;
      }
    }
  }
}
