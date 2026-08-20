using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>履歴書の場面で、現在どのポーズを認識しているか。</summary>
  public enum ResumePose
  {
    /// <summary>どれにも当てはまらない（未認識）</summary>
    None,
    /// <summary>腕を胸の前でクロス → 不採用</summary>
    Reject,
    /// <summary>両手を挙げて輪をつくる → 採用</summary>
    Hire,
    /// <summary>両手で輪っかをつくり顔（目）の近くに構える → 履歴書の裏を見る</summary>
    FlipPage,
  }

  /// <summary>
  /// 履歴書の場面専用：3つの静止ポーズだけで「不採用／採用／裏を見る」を判定する。
  /// 速度・軌跡など「動き」には一切頼らない設計。腕を振ったり指を動かしたりする瞬間的な動作では
  /// 発火しようがないため、動きベースの判定で悩まされてきた誤検出が構造的に起こらない。
  ///
  /// 判定は正規化された画面内座標(X, Y)だけで行う（奥行き(Z)は使わない）。
  /// 3つのポーズはいずれも見た目のシルエットだけで区別できるため、奥行き情報は不要。
  ///
  /// 確定方式：毎フレーム今のポーズを分類し、同じポーズを<see cref="_holdSeconds"/>秒間
  /// 保持し続けたら確定する（dwell方式）。ポーズが変わった瞬間に保持タイマーをリセットする。
  /// 現在認識しているポーズ（4状態：なし／不採用／採用／裏を見る）と保持の進捗は
  /// <see cref="CurrentPose"/>/<see cref="HoldProgress01"/>として毎フレーム参照でき、UI側で常時表示できる。
  ///
  /// 3ポーズはいずれも両手が映っている前提の判定なので、片手しか映っていない時は
  /// 自動的に<see cref="ResumePose.None"/>になる（片手だけの状況での誤認識を構造的に防ぐ）。
  /// </summary>
  public class ResumePoseRecognizer : MonoBehaviour
  {
    [SerializeField] private HandTrackingController _handTrackingController;

    [Header("共通")]
    [Tooltip("同じポーズを保持し続けて確定とみなすまでの秒数")]
    [SerializeField] private float _holdSeconds = 2f;
    [Tooltip("信号平滑化の強さ（0〜1）。大きいほど直近の値に素早く追従するが、その分ノイズも拾いやすくなる。")]
    [SerializeField, Range(0.05f, 1f)] private float _smoothing = 0.25f;

    [Header("腕クロス（不採用）")]
    [Tooltip("腕がクロスしているとみなす、左右の手首の画面内X座標の差（0〜1正規化座標）")]
    [SerializeField] private float _armsCrossOffset = 0.03f;

    [Header("両手を挙げて輪（採用）")]
    [Tooltip("挙げているとみなす、カメラ画面内での高さ（0=下端 1=上端）")]
    [SerializeField, Range(0f, 1f)] private float _raisedViewportY = 0.6f;
    [Tooltip("両手が触れ合っているとみなす、中指先端同士の画面内距離（0〜1正規化座標）")]
    [SerializeField] private float _handsTogetherDistance = 0.12f;

    [Header("両手で輪っか（裏を見る）")]
    [Tooltip("輪っかポーズとみなす、画面内での親指先端⇔人差し指先端の距離（0〜1正規化座標）")]
    [SerializeField] private float _hoopScreenDistance = 0.06f;
    [Tooltip("顔の近くとみなす、カメラ画面内での高さ（0=下端 1=上端）")]
    [SerializeField, Range(0f, 1f)] private float _faceHeightViewportY = 0.5f;

    /// <summary>いずれかのポーズが2秒保持され確定した時に発火。</summary>
    public event Action<ResumePose> OnPoseConfirmed;

    private bool _capturing;

    private float? _leftWristXEma, _rightWristXEma;
    private float? _leftViewportYEma, _rightViewportYEma;
    private float? _handsDistEma;
    private float? _leftHoopEma, _rightHoopEma;
    private float? _leftRingYEma, _rightRingYEma;

    public ResumePose CurrentPose { get; private set; } = ResumePose.None;
    public float HoldProgress01 => _holdSeconds > 0f ? Mathf.Clamp01(_holdTimer / _holdSeconds) : 0f;

    private float _holdTimer;

    private void OnEnable()
    {
      if (_handTrackingController != null) _handTrackingController.OnHandLandmarkerResult += HandleResult;
    }

    private void OnDisable()
    {
      if (_handTrackingController != null) _handTrackingController.OnHandLandmarkerResult -= HandleResult;
    }

    /// <summary>履歴書が開いている間だけtrueにする。閉じている間は完全に無反応にする。</summary>
    public void SetCapturing(bool capturing)
    {
      _capturing = capturing;
      ResetSmoothing();
      SetPose(ResumePose.None);
    }

    private void HandleResult(HandLandmarkerResult result)
    {
      if (!_capturing) return;
      SetPose(ClassifyCurrentPose(result));
    }

    private void SetPose(ResumePose pose)
    {
      if (pose != CurrentPose)
      {
        CurrentPose = pose;
        _holdTimer = 0f; // ポーズが切り替わったら保持タイマーもリセット（連続保持のみ有効にするため）
      }

      if (CurrentPose == ResumePose.None) return;

      _holdTimer += Time.deltaTime;
      if (_holdTimer >= _holdSeconds)
      {
        _holdTimer = 0f;
        var confirmed = CurrentPose;
        CurrentPose = ResumePose.None; // 確定後は一旦Noneに戻し、保持し続けても連続確定しないようにする
        ResetSmoothing();
        OnPoseConfirmed?.Invoke(confirmed);
      }
    }

    private void ResetSmoothing()
    {
      _leftWristXEma = _rightWristXEma = null;
      _leftViewportYEma = _rightViewportYEma = null;
      _handsDistEma = null;
      _leftHoopEma = _rightHoopEma = null;
      _leftRingYEma = _rightRingYEma = null;
    }

    private ResumePose ClassifyCurrentPose(HandLandmarkerResult result)
    {
      if (result.handLandmarks == null) return ResumePose.None;

      List<NormalizedLandmark> left = null;
      List<NormalizedLandmark> right = null;
      for (var i = 0; i < result.handLandmarks.Count; i++)
      {
        var lm = result.handLandmarks[i].landmarks;
        if (lm == null || lm.Count < 21) continue;
        if (IsRightHand(result, i)) right = lm; else left = lm;
      }

      // 3ポーズはいずれも両手が必要。片手だけ映っている状況ではここで自動的にNoneになる。
      if (left == null || right == null)
      {
        ResetSmoothing();
        return ResumePose.None;
      }

      var leftX = Smooth(ref _leftWristXEma, left[0].x);
      var rightX = Smooth(ref _rightWristXEma, right[0].x);
      var leftY = Smooth(ref _leftViewportYEma, 1f - left[0].y);
      var rightY = Smooth(ref _rightViewportYEma, 1f - right[0].y);
      // 中指の先端同士の距離＝「両手が触れ合っているか」の基準にする。手首同士の距離だと、
      // 頭上で腕を弧状に曲げて手を合わせるポーズ（肘が外に開き、手首は離れたまま指先だけが
      // 触れ合う）で実際より遠いと判定されてしまうため。
      var handsDist = Smooth(ref _handsDistEma, Distance2D(left[12], right[12]));
      var leftHoop = Smooth(ref _leftHoopEma, Distance2D(left[4], left[8]));
      var rightHoop = Smooth(ref _rightHoopEma, Distance2D(right[4], right[8]));

      // 腕クロス（不採用）：高さの制約なし、胸の前でも顔の高さでもよい。
      if (leftX > rightX + _armsCrossOffset)
      {
        return ResumePose.Reject;
      }

      // 両手を挙げて輪（採用）：両手とも高い位置にあり、指先同士が触れ合うくらい近い。
      if (leftY > _raisedViewportY && rightY > _raisedViewportY && handsDist < _handsTogetherDistance)
      {
        return ResumePose.Hire;
      }

      // 両手で輪っか、顔の近く（裏を見る）：両手とも親指・人差し指で輪をつくり、顔の高さにある。
      // 高さの判定には手首(0)ではなく輪っか自体（親指先端・人差し指先端の中点）の高さを使う。
      // 顔の近くに構えると、手首は頬や顎の高さに留まったまま指先だけが目の高さまで上がるため、
      // 手首基準だと実際は顔の近くにあるのに判定が通らない、という食い違いが起きるため。
      var leftRingY = Smooth(ref _leftRingYEma, 1f - (left[4].y + left[8].y) * 0.5f);
      var rightRingY = Smooth(ref _rightRingYEma, 1f - (right[4].y + right[8].y) * 0.5f);
      if (leftHoop < _hoopScreenDistance && rightHoop < _hoopScreenDistance
        && leftRingY > _faceHeightViewportY && rightRingY > _faceHeightViewportY)
      {
        return ResumePose.FlipPage;
      }

      return ResumePose.None;
    }

    private float Smooth(ref float? state, float value)
    {
      if (!state.HasValue)
      {
        state = value;
        return value;
      }
      state = Mathf.Lerp(state.Value, value, _smoothing);
      return state.Value;
    }

    private static float Distance2D(NormalizedLandmark a, NormalizedLandmark b)
    {
      var dx = a.x - b.x;
      var dy = a.y - b.y;
      return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// MediaPipeのhandedness分類は「入力画像が鏡像である」ことを前提とするため、
    /// このプロジェクトのパイプラインでは実際の手と左右が逆になる（他のHandTracking系クラスと同じ扱い）。
    private static bool IsRightHand(HandLandmarkerResult result, int index)
    {
      if (result.handedness == null || index >= result.handedness.Count) return false;
      var classifications = result.handedness[index];
      if (classifications.categories == null || classifications.categories.Count == 0) return false;
      return classifications.categories[0].categoryName == "Left";
    }
  }
}
