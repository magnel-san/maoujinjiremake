using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
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
  /// </summary>
  public class GestureRecognizer : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;

    [Header("共通しきい値")]
    [Tooltip("同一ジェスチャーの連続発火を防ぐクールダウン秒数")]
    [SerializeField] private float _defaultCooldown = 0.4f;
    [Tooltip("拳（グー）と判定する、指先→手首の平均距離のしきい値（m）")]
    [SerializeField] private float _fistDistanceThreshold = 0.09f;
    [Tooltip("速い動きとみなす速度のしきい値（m/s）")]
    [SerializeField] private float _fastVelocityThreshold = 1.2f;
    [Tooltip("近接（輪っか・タッチ等）とみなす距離のしきい値（m）")]
    [SerializeField] private float _touchDistanceThreshold = 0.04f;
    [Tooltip("頭上とみなす、手首の腰基準高さ（m）")]
    [SerializeField] private float _overheadHeight = 0.35f;
    [Tooltip("HandsTogether判定に必要な保持秒数")]
    [SerializeField] private float _handsTogetherHoldSeconds = 0.3f;

    public event Action<GestureType> OnGestureDetected;

    private readonly Dictionary<GestureType, float> _cooldownUntil = new Dictionary<GestureType, float>();

    private HandFrame _left = HandFrame.Empty;
    private HandFrame _right = HandFrame.Empty;
    private HandFrame _prevLeft = HandFrame.Empty;
    private HandFrame _prevRight = HandFrame.Empty;

    private float _handsTogetherTimer;
    private float _lastAltPunchHandSign;
    private float _altPunchCooldownUntil;

    private struct HandFrame
    {
      public bool valid;
      public Vector3 wrist;
      public Vector3[] fingerTips; // Thumb, Index, Middle, Ring, Pinky
      public float timestamp;

      public static HandFrame Empty => new HandFrame { valid = false, fingerTips = new Vector3[5] };

      public float AverageFistDistance()
      {
        var sum = 0f;
        for (var i = 0; i < fingerTips.Length; i++) sum += Vector3.Distance(fingerTips[i], wrist);
        return sum / fingerTips.Length;
      }
    }

    private void OnEnable()
    {
      if (_handTrackingController != null)
      {
        _handTrackingController.OnHandLandmarkerResult += HandleResult;
      }
    }

    private void OnDisable()
    {
      if (_handTrackingController != null)
      {
        _handTrackingController.OnHandLandmarkerResult -= HandleResult;
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
        var frame = BuildFrame(result.handWorldLandmarks[i]);
        if (isRight) newRight = frame; else newLeft = frame;
      }

      _left = newLeft.valid ? newLeft : HandFrame.Empty;
      _right = newRight.valid ? newRight : HandFrame.Empty;

      Evaluate();
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

    private HandFrame BuildFrame(Landmarks worldLandmarks)
    {
      if (worldLandmarks.landmarks == null || worldLandmarks.landmarks.Count < 21) return HandFrame.Empty;

      var lm = worldLandmarks.landmarks;
      Vector3 V(int i) => new Vector3(lm[i].x, lm[i].y, lm[i].z);

      return new HandFrame
      {
        valid = true,
        wrist = V(0),
        fingerTips = new[] { V(4), V(8), V(12), V(16), V(20) },
        timestamp = Time.time,
      };
    }

    private void Evaluate()
    {
      var dt = Mathf.Max(Time.deltaTime, 0.0001f);

      DetectHoopBothHands();
      DetectBigCircleOverhead();
      DetectRightFistPunchOut(dt);
      DetectArmsCross();
      DetectClapNarrow(dt);
      DetectHorizontalSwipes(dt);
      DetectWingFlap(dt);
      DetectArmSwingBoth(dt);
      DetectHammerSwingDown(dt);
      DetectAlternatingPunch(dt);
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

    // 両手で親指と人差し指で輪をつくる
    private void DetectHoopBothHands()
    {
      if (!_left.valid || !_right.valid) return;
      var leftHoop = Vector3.Distance(_left.fingerTips[0], _left.fingerTips[1]) < _touchDistanceThreshold;
      var rightHoop = Vector3.Distance(_right.fingerTips[0], _right.fingerTips[1]) < _touchDistanceThreshold;
      if (leftHoop && rightHoop) TryFire(GestureType.HoopBothHands);
    }

    // 頭の上で大きな丸をつくる（片手または両手が頭上で高速に動いていることで近似）
    private void DetectBigCircleOverhead()
    {
      var overheadY = _overheadHeight;
      var leftOverheadFast = _left.valid && _left.wrist.y > overheadY && Velocity(_left, _prevLeft, Time.deltaTime).magnitude > _fastVelocityThreshold;
      var rightOverheadFast = _right.valid && _right.wrist.y > overheadY && Velocity(_right, _prevRight, Time.deltaTime).magnitude > _fastVelocityThreshold;
      if (leftOverheadFast || rightOverheadFast) TryFire(GestureType.BigCircleOverhead);
    }

    // 右手をグーで思いっきり突き出す
    private void DetectRightFistPunchOut(float dt)
    {
      if (!IsFist(_right)) return;
      var vel = Velocity(_right, _prevRight, dt);
      if (vel.z > _fastVelocityThreshold) TryFire(GestureType.RightFistPunchOut);
    }

    // 胸の前で腕をクロス（左右の手首が体の中心線をまたいで入れ替わる）
    private void DetectArmsCross()
    {
      if (!_left.valid || !_right.valid) return;
      if (_left.wrist.x > _right.wrist.x + 0.02f) TryFire(GestureType.ArmsCross);
    }

    // 拍手のように両手を胸の前で近づけ離す（一定距離まで急接近した瞬間を1回とする）
    private void DetectClapNarrow(float dt)
    {
      if (!_left.valid || !_right.valid || !_prevLeft.valid || !_prevRight.valid) return;
      var dist = Vector3.Distance(_left.wrist, _right.wrist);
      var prevDist = Vector3.Distance(_prevLeft.wrist, _prevRight.wrist);
      var closingFast = (prevDist - dist) / dt > _fastVelocityThreshold * 0.5f;
      if (closingFast && dist < _touchDistanceThreshold * 3f) TryFire(GestureType.ClapNarrow);
    }

    // 手で横に払う／左右への腕振り（速度の水平成分の向きで判定）
    private void DetectHorizontalSwipes(float dt)
    {
      EvaluateSwipe(_right, _prevRight, dt);
      EvaluateSwipe(_left, _prevLeft, dt);
    }

    private void EvaluateSwipe(in HandFrame hand, in HandFrame prev, float dt)
    {
      var vel = Velocity(hand, prev, dt);
      if (Mathf.Abs(vel.x) < _fastVelocityThreshold) return;
      if (Mathf.Abs(vel.x) < Mathf.Abs(vel.y) || Mathf.Abs(vel.x) < Mathf.Abs(vel.z)) return;

      TryFire(GestureType.SwipeSideways);
      if (vel.x > 0) TryFire(GestureType.SwipeLeftToRight);
      else TryFire(GestureType.SwipeRightToLeft);
    }

    // 腕を翼のように上下に振る（両手首の垂直速度が同符号で大きい）
    private void DetectWingFlap(float dt)
    {
      if (!_left.valid || !_right.valid) return;
      var lv = Velocity(_left, _prevLeft, dt);
      var rv = Velocity(_right, _prevRight, dt);
      if (Mathf.Abs(lv.y) > _fastVelocityThreshold && Mathf.Abs(rv.y) > _fastVelocityThreshold && Mathf.Sign(lv.y) == Mathf.Sign(rv.y))
      {
        TryFire(GestureType.WingFlap);
      }
    }

    // 両腕を振る（前後方向、ランニングのように腕を振る動き）
    private void DetectArmSwingBoth(float dt)
    {
      if (!_left.valid || !_right.valid) return;
      var lv = Velocity(_left, _prevLeft, dt);
      var rv = Velocity(_right, _prevRight, dt);
      if (Mathf.Abs(lv.z) > _fastVelocityThreshold && Mathf.Abs(rv.z) > _fastVelocityThreshold)
      {
        TryFire(GestureType.ArmSwingBoth);
      }
    }

    // 腕を振り下ろす（ハンマー打ち）：上方にあった手が急速に下降
    private void DetectHammerSwingDown(float dt)
    {
      EvaluateHammer(_right, _prevRight, dt);
      EvaluateHammer(_left, _prevLeft, dt);
    }

    private void EvaluateHammer(in HandFrame hand, in HandFrame prev, float dt)
    {
      if (!hand.valid || !prev.valid) return;
      var vel = Velocity(hand, prev, dt);
      if (prev.wrist.y > 0.1f && vel.y < -_fastVelocityThreshold)
      {
        TryFire(GestureType.HammerSwingDown);
      }
    }

    // 両拳を交互に突き出す（パンチ）：左右どちらかの拳が交互に前方へ突き出る
    private void DetectAlternatingPunch(float dt)
    {
      if (Time.time < _altPunchCooldownUntil) return;

      var rightPunch = IsFist(_right) && Velocity(_right, _prevRight, dt).z > _fastVelocityThreshold;
      var leftPunch = IsFist(_left) && Velocity(_left, _prevLeft, dt).z > _fastVelocityThreshold;

      float sign = 0f;
      if (rightPunch) sign = 1f;
      else if (leftPunch) sign = -1f;
      if (sign == 0f) return;

      if (TryFire(GestureType.AlternatingPunch))
      {
        _lastAltPunchHandSign = sign;
        _altPunchCooldownUntil = Time.time + _defaultCooldown;
      }
    }

    // 胸の前でぐるぐるバルブを回す：片手が胸の高さで速く円運動（水平面の速度が大きい）
    private void DetectValveSpin(float dt)
    {
      EvaluateValve(_right, _prevRight, dt);
      EvaluateValve(_left, _prevLeft, dt);
    }

    private void EvaluateValve(in HandFrame hand, in HandFrame prev, float dt)
    {
      if (!hand.valid || !prev.valid) return;
      if (hand.wrist.y < -0.1f || hand.wrist.y > 0.3f) return; // 胸の高さ付近のみ
      var vel = Velocity(hand, prev, dt);
      var horizontalSpeed = new Vector2(vel.x, vel.y).magnitude;
      if (horizontalSpeed > _fastVelocityThreshold)
      {
        TryFire(GestureType.ValveSpin);
      }
    }

    // 両手を前で合わせる：両手首が近接した状態を一定時間保持
    private void DetectHandsTogether(float dt)
    {
      if (!_left.valid || !_right.valid)
      {
        _handsTogetherTimer = 0f;
        return;
      }

      var dist = Vector3.Distance(_left.wrist, _right.wrist);
      if (dist < _touchDistanceThreshold * 2f)
      {
        _handsTogetherTimer += dt;
        if (_handsTogetherTimer >= _handsTogetherHoldSeconds)
        {
          if (TryFire(GestureType.HandsTogether)) _handsTogetherTimer = 0f;
        }
      }
      else
      {
        _handsTogetherTimer = 0f;
      }
    }
  }
}
