using System.Collections.Generic;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【飛行】仕様4.2：「腕を翼のように上下に振る」で操作するTPSフラッピーバード風ミニゲーム。
  /// 他のジャンルと違い、ライフ制ではなく「時間内に何本の障害物を通過できたか」がそのままスコアになる。
  /// 墜落しても即ゲームオーバーにはせず、開始時と同じ3秒の猶予（重力OFF）を挟んで何度でも再開できる
  /// （編成攻撃力は復活回数ではなく、通過1本ごとのスコアの重みに使う）。
  ///
  /// 障害物の通過判定は物理コライダーではなく、障害物が一定Z位置(judgeZ)を通過した瞬間の
  /// パイロットの高さと隙間の中心との距離で判定する（コライダー設定を待たずに動作確認できるように）。
  /// </summary>
  public class FlightMinigame : MinigameBase
  {
    private enum FlightPhase { Idle, Practice, Countdown, Playing }

    [Header("パイロット")]
    [Tooltip("パイロット（実際に飛ばすキャラ）を配置する位置・向き")]
    [SerializeField] private Transform pilotSpawnPoint;
    [Tooltip("1回の羽ばたきで得る上昇速度(m/s)")]
    [SerializeField] private float flapLift = 4f;
    [SerializeField] private float gravity = 9f;
    [SerializeField] private float minY = -3f;
    [SerializeField] private float maxY = 3f;

    [Header("その他の採用キャラの整列")]
    [Tooltip("複数採用されている場合、パイロット以外の残りのキャラをここから並べて配置する（見送り役として待機させる想定）")]
    [SerializeField] private Transform remainingLineupOrigin;
    [SerializeField] private LineupAxis remainingLineupAxis = LineupAxis.PositiveX;
    [SerializeField] private float remainingLineupSpacing = 2f;

    [Header("開始/墜落後の待機")]
    [Tooltip("ゲーム開始時・墜落からの再開時、重力OFFで準備できる秒数")]
    [SerializeField] private float startupSeconds = 3f;

    [Header("障害物")]
    [SerializeField] private GameObject obstaclePairPrefab;
    [Tooltip("障害物が出現する位置（プレイヤーから見て奥）")]
    [SerializeField] private Transform obstacleSpawnPoint;
    [Tooltip("障害物がプレイヤーへ向かって進む速度(m/s)")]
    [SerializeField] private float obstacleSpeed = 5f;
    [SerializeField] private float obstacleSpawnInterval = 2f;
    [Tooltip("障害物の隙間（ゲート）の中心Yが取りうる範囲")]
    [SerializeField] private float gapCenterMinY = -1.5f;
    [SerializeField] private float gapCenterMaxY = 1.5f;
    [Tooltip("隙間の高さ。パイロットのYがこの範囲内であれば通過成功")]
    [SerializeField] private float gapHeight = 2.5f;
    [Tooltip("この位置を通過した瞬間に成否判定する（パイロットの立ち位置とだいたい同じZ）")]
    [SerializeField] private float judgeZ;

    [Header("UI")]
    [Tooltip("通過数・スコアを表示するテキスト")]
    [SerializeField] private TMP_Text obstaclesPassedText;

    private CharacterData _pilot;
    private GameObject _pilotInstance;
    private float _pilotVelocityY;
    private FlightPhase _phase = FlightPhase.Idle;
    private float _phaseTimer;
    private float _spawnTimer;

    private readonly struct ObstacleEntry
    {
      public readonly Transform Transform;
      public readonly float GapCenterY;
      public readonly bool Judged;
      public ObstacleEntry(Transform t, float gapCenterY, bool judged) { Transform = t; GapCenterY = gapCenterY; Judged = judged; }
    }
    private readonly List<ObstacleEntry> _obstacles = new List<ObstacleEntry>();
    private readonly List<GameObject> _remainingLineup = new List<GameObject>();

    public int ObstaclesPassed { get; private set; }
    public float Score { get; private set; }

    private void Update()
    {
      // 練習中（本番タイマー開始前）は、ここでだけパイロットの物理を動かす。
      // OnMinigameTickは本番の時間カウント中しか呼ばれないため。
      if (_phase == FlightPhase.Practice)
      {
        UpdatePilotHeight(Time.deltaTime, applyGravity: true);
      }
    }

    protected override void OnRulesShown()
    {
      _pilot = PickRandomAssigned();
      SpawnPilot();
      RefreshRemainingLineup();
      _phase = FlightPhase.Practice;
    }

    protected override void OnRulesHidden()
    {
      _phase = FlightPhase.Idle;
    }

    protected override void OnMinigameStart()
    {
      ObstaclesPassed = 0;
      Score = 0f;
      UpdateScoreText();
      ClearObstacles();
      SpawnPilot();
      RefreshRemainingLineup();
      EnterCountdown();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      if (_phase == FlightPhase.Countdown)
      {
        _phaseTimer -= deltaTime;
        UpdatePilotHeight(deltaTime, applyGravity: false);
        if (_phaseTimer <= 0f) _phase = FlightPhase.Playing;
        return;
      }

      if (_phase != FlightPhase.Playing) return;

      UpdatePilotHeight(deltaTime, applyGravity: true);
      UpdateObstacles(deltaTime);
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      ClearObstacles();
      if (_pilotInstance != null) Destroy(_pilotInstance);
      _pilotInstance = null;
      DespawnLineup(_remainingLineup);
      _phase = FlightPhase.Idle;
    }

    /// <summary>パイロット以外の採用キャラを整列し直す（前回の召喚が残っていれば片付けてから再召喚する）。</summary>
    private void RefreshRemainingLineup()
    {
      DespawnLineup(_remainingLineup);
      _remainingLineup.AddRange(SpawnRemainingLineup(_pilot, remainingLineupOrigin, remainingLineupAxis, remainingLineupSpacing));
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type == GestureType.WingFlap) Flap();
    }

    /// <summary>ルール説明中の練習：何度でも羽ばたいて高さの感覚を確認できる。障害物は出さず、失敗もない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type == GestureType.WingFlap) Flap();
    }

    private void Flap()
    {
      if (_pilotInstance == null) return;
      if (_phase != FlightPhase.Practice && _phase != FlightPhase.Playing) return;
      _pilotVelocityY = flapLift;
    }

    private void SpawnPilot()
    {
      if (_pilotInstance != null) Destroy(_pilotInstance);
      if (_pilot == null || _pilot.characterPrefab == null || pilotSpawnPoint == null) return;

      _pilotInstance = Instantiate(_pilot.characterPrefab, pilotSpawnPoint.position, pilotSpawnPoint.rotation);
      _pilotVelocityY = 0f;
    }

    /// <summary>開始時／墜落後の3秒待機に入る。重力を切ってパイロットを基準の高さへ戻す。</summary>
    private void EnterCountdown()
    {
      _phase = FlightPhase.Countdown;
      _phaseTimer = Mathf.Max(startupSeconds, 0f);
      _pilotVelocityY = 0f;

      if (_pilotInstance != null && pilotSpawnPoint != null)
      {
        var pos = _pilotInstance.transform.position;
        pos.y = pilotSpawnPoint.position.y;
        _pilotInstance.transform.position = pos;
      }
    }

    private void UpdatePilotHeight(float deltaTime, bool applyGravity)
    {
      if (_pilotInstance == null) return;

      if (applyGravity) _pilotVelocityY -= gravity * deltaTime;
      var pos = _pilotInstance.transform.position;
      pos.y = Mathf.Clamp(pos.y + _pilotVelocityY * deltaTime, minY, maxY);
      _pilotInstance.transform.position = pos;
    }

    private void UpdateObstacles(float deltaTime)
    {
      _spawnTimer -= deltaTime;
      if (_spawnTimer <= 0f)
      {
        SpawnObstacle();
        _spawnTimer = Mathf.Max(obstacleSpawnInterval, 0.1f);
      }

      for (var i = _obstacles.Count - 1; i >= 0; i--)
      {
        var entry = _obstacles[i];
        if (entry.Transform == null)
        {
          _obstacles.RemoveAt(i);
          continue;
        }

        entry.Transform.position += Vector3.back * obstacleSpeed * deltaTime;

        if (!entry.Judged && entry.Transform.position.z <= judgeZ)
        {
          JudgeObstacle(entry.GapCenterY);
          _obstacles[i] = new ObstacleEntry(entry.Transform, entry.GapCenterY, true);
          entry = _obstacles[i];

          // 墜落判定でカウントダウンに入った場合、以降このフレームでの障害物移動処理は次フレームへ持ち越す。
          if (_phase == FlightPhase.Countdown) return;
        }

        if (entry.Transform.position.z < judgeZ - 5f)
        {
          Destroy(entry.Transform.gameObject);
          _obstacles.RemoveAt(i);
        }
      }
    }

    private void SpawnObstacle()
    {
      if (obstaclePairPrefab == null || obstacleSpawnPoint == null) return;

      var gapCenterY = Random.Range(gapCenterMinY, gapCenterMaxY);
      var spawnPos = obstacleSpawnPoint.position;
      spawnPos.y = gapCenterY;
      var instance = Instantiate(obstaclePairPrefab, spawnPos, obstacleSpawnPoint.rotation);
      _obstacles.Add(new ObstacleEntry(instance.transform, gapCenterY, false));
    }

    private void JudgeObstacle(float gapCenterY)
    {
      if (_pilotInstance == null) return;

      var pilotY = _pilotInstance.transform.position.y;
      var passed = Mathf.Abs(pilotY - gapCenterY) <= gapHeight * 0.5f;

      if (passed)
      {
        ObstaclesPassed++;
        Score += totalAttackPower;
        UpdateScoreText();
      }
      else
      {
        // ライフ制ではないので即ゲームオーバーにはしない。3秒の猶予を挟んで再開する。
        EnterCountdown();
      }
    }

    private void ClearObstacles()
    {
      foreach (var entry in _obstacles)
      {
        if (entry.Transform != null) Destroy(entry.Transform.gameObject);
      }
      _obstacles.Clear();
    }

    private void UpdateScoreText()
    {
      if (obstaclesPassedText != null) obstaclesPassedText.text = $"通過数: {ObstaclesPassed}（スコア {Score:0}）";
    }
  }
}
