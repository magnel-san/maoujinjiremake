using System.Collections.Generic;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【耐寒】仕様4.8：3レーン上で「右手を握る(グー)→右移動」「左手を握る(グー)→左移動」でレーン移動し、
  /// 飛んでくる氷塊を回避する。上腕トラッキング(PoseTrackingController)を使わず、手の形（静止ポーズ）
  /// だけで判定するため、肩・肘のカメラフレーミングに依存しない。左右どちらの手を握ったかで
  /// 移動方向が決まるため、両方向が同時に誤発火することもない。
  /// 採用キャラは中央レーンに配置してスタートする。被弾した場合は即ゲームオーバーにはせず、
  /// 数秒のスタン（その間はレーン移動できない）というペナルティにしている。
  /// </summary>
  public class ColdResistMinigame : MinigameBase
  {
    public const int LaneCount = 3;

    [Header("走者・レーン")]
    [Tooltip("各レーンの立ち位置。[0]=左, [1]=中央, [2]=右")]
    [SerializeField] private Transform[] lanePositions = new Transform[LaneCount];

    [Header("その他の採用キャラの整列")]
    [Tooltip("複数採用されている場合、中央レーンに立つ1体以外の残りのキャラをここから並べて配置する")]
    [SerializeField] private Transform remainingLineupOrigin;
    [SerializeField] private LineupAxis remainingLineupAxis = LineupAxis.PositiveX;
    [SerializeField] private float remainingLineupSpacing = 2f;

    [Header("氷塊オブジェクト")]
    [SerializeField] private GameObject iceChunkPrefab;
    [Tooltip("各レーンの出現位置（プレイヤーから見て奥）。[0]=左, [1]=中央, [2]=右。" +
      "対応するlanePositionsへ向かうベクトルを進行方向として自動計算するため、Z座標を手打ちで" +
      "揃える必要はない（配置がどの向きでも正しく機能する）。")]
    [SerializeField] private Transform[] iceSpawnPoints = new Transform[LaneCount];
    [SerializeField] private float iceSpeed = 5f;
    [SerializeField] private float iceSpawnInterval = 1.2f;
    [Tooltip("lanePositionsを通過してから何m先で自動的に消すか")]
    [SerializeField] private float despawnDistancePastLane = 5f;

    [Header("被弾ペナルティ")]
    [SerializeField] private float stunSeconds = 2f;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;
    [Tooltip("スタン中に表示するUI（任意）")]
    [SerializeField] private GameObject stunIndicator;

    private CharacterData _defender;
    private GameObject _defenderInstance;
    private readonly List<GameObject> _remainingLineup = new List<GameObject>();
    private readonly List<(Transform transform, int lane, Vector3 direction, bool judged)> _iceChunks = new List<(Transform, int, Vector3, bool)>();
    private float _spawnTimer;
    private float _stunTimer;

    public int CurrentLane { get; private set; } = 1; // 0=左,1=中央,2=右
    public float Score { get; private set; }
    public bool IsStunned => _stunTimer > 0f;

    // 防御役＋残りの整列を自前でスポーンするため、基底クラスの一括召喚は使わない
    // （両方動くと防御役が二重に召喚されてしまう）。
    protected override bool SkipGenericCharacterSummon => true;

    protected override void OnRulesShown()
    {
      _defender = PickRandomAssigned();
      SpawnDefender();
      RefreshRemainingLineup();
    }

    protected override void OnMinigameStart()
    {
      CurrentLane = 1;
      Score = 0f;
      _stunTimer = 0f;
      _spawnTimer = 0f;
      ClearIceChunks();
      SpawnDefender();
      RefreshRemainingLineup();
      UpdateDefenderPosition();
      UpdateScoreText();
      stunIndicator?.SetActive(false);
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      if (_stunTimer > 0f)
      {
        _stunTimer -= deltaTime;
        if (_stunTimer <= 0f) stunIndicator?.SetActive(false);
      }

      _spawnTimer -= deltaTime;
      if (_spawnTimer <= 0f)
      {
        SpawnIceChunk();
        _spawnTimer = Mathf.Max(iceSpawnInterval, 0.1f);
      }

      UpdateIceChunks(deltaTime);
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      ClearIceChunks();
      if (_defenderInstance != null) Destroy(_defenderInstance);
      _defenderInstance = null;
      DespawnLineup(_remainingLineup);
    }

    /// <summary>中央レーンに立つ1体以外の採用キャラを整列し直す（前回の召喚が残っていれば片付けてから再召喚する）。</summary>
    private void RefreshRemainingLineup()
    {
      DespawnLineup(_remainingLineup);
      _remainingLineup.AddRange(SpawnRemainingLineup(_defender, remainingLineupOrigin, remainingLineupAxis, remainingLineupSpacing));
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (IsStunned) return; // スタン中はレーン移動できない

      switch (type)
      {
        case GestureType.LeftHandFist:
          CurrentLane = Mathf.Max(0, CurrentLane - 1);
          UpdateDefenderPosition();
          break;
        case GestureType.RightHandFist:
          CurrentLane = Mathf.Min(LaneCount - 1, CurrentLane + 1);
          UpdateDefenderPosition();
          break;
      }
    }

    /// <summary>練習中もレーン移動自体は試せるようにする（氷塊は出ないので被弾しない）。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      switch (type)
      {
        case GestureType.LeftHandFist:
          CurrentLane = Mathf.Max(0, CurrentLane - 1);
          UpdateDefenderPosition();
          break;
        case GestureType.RightHandFist:
          CurrentLane = Mathf.Min(LaneCount - 1, CurrentLane + 1);
          UpdateDefenderPosition();
          break;
      }
    }

    private void SpawnDefender()
    {
      if (_defenderInstance != null) Destroy(_defenderInstance);
      if (_defender == null || _defender.characterPrefab == null || lanePositions == null || lanePositions.Length <= 1 || lanePositions[1] == null) return;

      _defenderInstance = Instantiate(_defender.characterPrefab, lanePositions[1].position, lanePositions[1].rotation);
    }

    private void UpdateDefenderPosition()
    {
      if (_defenderInstance == null || lanePositions == null || CurrentLane >= lanePositions.Length) return;
      var lane = lanePositions[CurrentLane];
      if (lane == null) return;
      _defenderInstance.transform.position = lane.position;
    }

    private void SpawnIceChunk()
    {
      if (iceChunkPrefab == null || iceSpawnPoints == null || iceSpawnPoints.Length == 0 || lanePositions == null) return;

      var lane = Random.Range(0, iceSpawnPoints.Length);
      var point = iceSpawnPoints[lane];
      var lanePoint = lane < lanePositions.Length ? lanePositions[lane] : null;
      if (point == null || lanePoint == null) return;

      // レーンの出現位置から立ち位置へ向かうベクトルを進行方向にする（Z座標の手打ちに頼らないため、
      // 出現位置がプレイヤーからどちらの向きに置かれていても正しく接近してくる）。
      var direction = (lanePoint.position - point.position).normalized;
      var instance = Instantiate(iceChunkPrefab, point.position, Quaternion.LookRotation(direction, Vector3.up));
      _iceChunks.Add((instance.transform, lane, direction, false));
    }

    private void UpdateIceChunks(float deltaTime)
    {
      for (var i = _iceChunks.Count - 1; i >= 0; i--)
      {
        var entry = _iceChunks[i];
        if (entry.transform == null || lanePositions == null || entry.lane >= lanePositions.Length || lanePositions[entry.lane] == null)
        {
          if (entry.transform != null) Destroy(entry.transform.gameObject);
          _iceChunks.RemoveAt(i);
          continue;
        }

        entry.transform.position += entry.direction * (iceSpeed * deltaTime);

        // 対応するレーンの立ち位置を通過した瞬間に命中判定する。
        var distancePastLane = Vector3.Dot(entry.transform.position - lanePositions[entry.lane].position, entry.direction);

        if (!entry.judged && distancePastLane >= 0f)
        {
          JudgeIceChunk(entry.lane);
          entry = (entry.transform, entry.lane, entry.direction, true);
          _iceChunks[i] = entry;
          distancePastLane = Vector3.Dot(entry.transform.position - lanePositions[entry.lane].position, entry.direction);
        }

        if (distancePastLane >= Mathf.Max(despawnDistancePastLane, 0.1f))
        {
          Destroy(entry.transform.gameObject);
          _iceChunks.RemoveAt(i);
        }
      }
    }

    private void JudgeIceChunk(int lane)
    {
      if (lane == CurrentLane)
      {
        RegisterHit();
      }
      else
      {
        RegisterDodge();
      }
    }

    /// <summary>回避成功時に呼ばれる。</summary>
    private void RegisterDodge()
    {
      Score += totalAttackPower;
      UpdateScoreText();
    }

    /// <summary>被弾時に呼ばれる。即ゲームオーバーにはせず、数秒スタンさせる。</summary>
    private void RegisterHit()
    {
      _stunTimer = Mathf.Max(stunSeconds, 0f);
      stunIndicator?.SetActive(true);
    }

    private void ClearIceChunks()
    {
      foreach (var entry in _iceChunks)
      {
        if (entry.transform != null) Destroy(entry.transform.gameObject);
      }
      _iceChunks.Clear();
    }

    private void UpdateScoreText()
    {
      if (scoreText != null) scoreText.text = $"回避スコア: {Score:0}";
    }
  }
}
