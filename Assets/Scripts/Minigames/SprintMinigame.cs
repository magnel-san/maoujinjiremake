using System.Collections.Generic;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【俊足】：城に迫る勇者より先に城門へ回り込み、通せんぼして足止めする。
  /// 「勇者と100m走で競争する」は、攻めてくる側がのんびり競走に応じる状況として不自然なため、
  /// 「間に合わせるために全力疾走する」という切迫した状況に変更した。城門(<see cref="gatePoint"/>)に
  /// 間に合えば防衛成功。
  ///
  /// 走者は常時<see cref="_baseSpeed"/>で自動的に走り続け、「両腕を振る」たびに編成の合計攻撃力に
  /// 比例したブースト速度が加算される（一定時間かけて基礎速度まで減衰する）。ジェスチャー自体で
  /// 瞬間移動させる方式はやめ、「常に走っているキャラをモーションで加速する」という手触りにしている。
  /// </summary>
  public class SprintMinigame : MinigameBase
  {
    [SerializeField] private float _trackLength = 100f;
    [Tooltip("腕を振らなくても常時出ている基礎速度(m/s)")]
    [SerializeField] private float _baseSpeed = 3f;
    [Tooltip("腕を振るたびに加算されるブースト速度＝合計攻撃力×この係数(m/s)")]
    [SerializeField] private float _boostPerAttackPower = 0.2f;
    [Tooltip("ブースト速度が基礎速度まで減衰していく速さ(m/s毎秒)")]
    [SerializeField] private float _boostDecayPerSecond = 4f;

    [Header("走者")]
    [SerializeField] private Transform runnerStartPoint;
    [Tooltip("先回りする先＝城門の位置")]
    [SerializeField] private Transform gatePoint;

    [Header("その他の採用キャラの整列")]
    [Tooltip("複数採用されている場合、走者以外の残りのキャラをここから並べて配置する")]
    [SerializeField] private Transform remainingLineupOrigin;
    [SerializeField] private LineupAxis remainingLineupAxis = LineupAxis.PositiveX;
    [SerializeField] private float remainingLineupSpacing = 2f;

    [Header("UI")]
    [SerializeField] private TMP_Text distanceText;

    private CharacterData _runner;
    private GameObject _runnerInstance;
    private readonly List<GameObject> _remainingLineup = new List<GameObject>();
    private bool _practiceActive;
    private float _boostSpeed;
    private Vector3 _baseCameraPosition;

    public float DistanceTravelled { get; private set; }

    /// <summary>合計攻撃力×到達距離の割合（城門に着けば満額）。他ジャンルのように「1回のアクションに
    /// 固定額」ではなく進捗の割合で決まるのは、俊足の目標が「回数をこなす」ではなく「時間内に城門まで
    /// 辿り着けたか」という単発の到達目標だから（詳細はSwimMinigame.scoreMultiplierのコメント参照）。
    /// 城門に間に合わなくても、進んだ分だけスコアが入る。</summary>
    public float Score => _trackLength > 0f ? totalAttackPower * Mathf.Clamp01(DistanceTravelled / _trackLength) : 0f;

    // 走者＋残りの整列を自前でスポーンするため、基底クラスの一括召喚は使わない
    // （両方動くと走者が二重に召喚されてしまう）。
    protected override bool SkipGenericCharacterSummon => true;

    /// <summary>実際に走る方向（runnerStartPoint→gatePoint）。runnerStartPointの向き(rotation)を
    /// 手動でgatePoint側に合わせなくても、常に正しい方向を自動的に求める。</summary>
    private Vector3 RunDirection =>
      runnerStartPoint != null && gatePoint != null
        ? (gatePoint.position - runnerStartPoint.position).normalized
        : Vector3.forward;

    protected override void OnRulesShown()
    {
      // この時点でplayerRoot(カメラ)はMinigameBaseのワープ処理により既にwarpTargetの位置にいるため、
      // それを「追従の基準位置」として覚えておく。以降は走者がrunnerStartPointから動いた分だけ
      // カメラも同じだけ動かし、シーンで設定した相対位置関係（走者を後ろから追う構図等）を保つ。
      _baseCameraPosition = playerRoot != null ? playerRoot.position : Vector3.zero;

      _runner = PickRandomAssigned();
      SpawnRunner();
      RefreshRemainingLineup();
      DistanceTravelled = 0f;
      _boostSpeed = 0f;
      UpdateRunnerPosition();
      UpdateDistanceText();
      _practiceActive = true; // 練習中も常時走る手触りを試せるようにする
    }

    protected override void OnRulesHidden()
    {
      _practiceActive = false;
    }

    protected override void OnMinigameStart()
    {
      DistanceTravelled = 0f;
      _boostSpeed = 0f;
      SpawnRunner();
      RefreshRemainingLineup();
      UpdateRunnerPosition();
      UpdateDistanceText();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      Advance(deltaTime, isPractice: false);
    }

    protected override float GetFinalScore() => Score;

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      if (_runnerInstance != null) Destroy(_runnerInstance);
      _runnerInstance = null;
      DespawnLineup(_remainingLineup);
    }

    private void Update()
    {
      // 練習中（本番タイマー開始前）は、ここでだけ常時走行を進める。
      // OnMinigameTickは本番の時間カウント中しか呼ばれないため。
      if (_practiceActive) Advance(Time.deltaTime, isPractice: true);
    }

    /// <summary>常時走行＋ブースト減衰を進め、走者の見た目位置を更新する。
    /// 本番中に城門(_trackLength)へ到達したら防衛成功。練習中は到達したら最初へループさせ、
    /// 何度でも「常時走る＋ブーストする」感覚を試せるようにする。</summary>
    private void Advance(float deltaTime, bool isPractice)
    {
      _boostSpeed = Mathf.Max(0f, _boostSpeed - _boostDecayPerSecond * deltaTime);
      DistanceTravelled += (_baseSpeed + _boostSpeed) * deltaTime;

      if (DistanceTravelled >= _trackLength)
      {
        if (isPractice)
        {
          DistanceTravelled = 0f;
        }
        else
        {
          DistanceTravelled = _trackLength;
          FinishAsDefenseSuccess(); // 城門に間に合った＝通せんぼ成功
        }
      }

      UpdateRunnerPosition();
      UpdateDistanceText();
    }

    /// <summary>走者以外の採用キャラを整列し直す（前回の召喚が残っていれば片付けてから再召喚する）。</summary>
    private void RefreshRemainingLineup()
    {
      DespawnLineup(_remainingLineup);
      _remainingLineup.AddRange(SpawnRemainingLineup(_runner, remainingLineupOrigin, remainingLineupAxis, remainingLineupSpacing));
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.ArmSwingBoth) return;
      _boostSpeed += totalAttackPower * _boostPerAttackPower;
    }

    /// <summary>練習中も同じブーストを試せるようにする（実際の到達距離は練習終了時にリセットされる）。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.ArmSwingBoth) return;
      _boostSpeed += totalAttackPower * _boostPerAttackPower;
    }

    /// <summary>走者をrunnerStartPointの位置に、gatePointの方を向かせて召喚する。runnerStartPoint自体の
    /// 向き(rotation)には依存しない（向きが移動方向と合っていないと後ろ向きに走っているように見えるため）。</summary>
    private void SpawnRunner()
    {
      if (_runnerInstance != null) Destroy(_runnerInstance);
      if (_runner == null || _runner.characterPrefab == null || runnerStartPoint == null) return;

      var rotation = gatePoint != null ? Quaternion.LookRotation(RunDirection) : runnerStartPoint.rotation;
      _runnerInstance = Instantiate(_runner.characterPrefab, runnerStartPoint.position, rotation);
    }

    private void UpdateRunnerPosition()
    {
      if (_runnerInstance == null || runnerStartPoint == null || gatePoint == null) return;
      var t = _trackLength > 0f ? Mathf.Clamp01(DistanceTravelled / _trackLength) : 0f;
      var runnerPos = Vector3.Lerp(runnerStartPoint.position, gatePoint.position, t);
      _runnerInstance.transform.position = runnerPos;
      UpdateCameraFollow(runnerPos);
    }

    /// <summary>カメラ(playerRoot)を、走者がrunnerStartPointから動いた分だけ同じように動かして追従させる。
    /// 生の距離ではなく走者の実位置との差分を使うため、trackLengthと実際のワールド距離が
    /// 一致していなくても走者とカメラの相対位置は常に一定に保たれる。</summary>
    private void UpdateCameraFollow(Vector3 runnerPos)
    {
      if (playerRoot == null || runnerStartPoint == null) return;
      playerRoot.position = _baseCameraPosition + (runnerPos - runnerStartPoint.position);
    }

    private void UpdateDistanceText()
    {
      if (distanceText != null) distanceText.text = $"到達距離: {DistanceTravelled:0}/{_trackLength:0}m";
    }
  }
}
