using System.Collections.Generic;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【耐寒】仕様4.8：3レーン上でレーン移動し、飛んでくる氷塊を回避する。
  /// モーション（手の形）判定は精度が安定しなかったため廃止し、代わりに人差し指先の
  /// カーソル位置（画面を縦に3分割した際、どのゾーンを指しているか）に応じて、
  /// 中央キャラの立ち位置がそのゾーンへ常に追従する方式にしている
  /// （ジェスチャーの発火待ちではなく、毎フレーム指の位置でレーンを決め直す）。
  /// 採用キャラは中央レーンに配置してスタートする。被弾した場合は即ゲームオーバーにはせず、
  /// 数秒のスタン（その間はレーン移動できない）というペナルティにしている。
  /// </summary>
  public class ColdResistMinigame : MinigameBase
  {
    public const int LaneCount = 3;

    [Header("カーソル追従")]
    [Tooltip("人差し指先のビューポート座標を取得するための参照。画面を縦に3分割し、" +
      "指しているゾーン（左/中央/右）へ常時レーンを合わせる。")]
    [SerializeField] private HandTrackingController handTrackingController;
    [Tooltip("どちらの手を優先してカーソルに使うか。指定した手が検出されていない場合はもう片方にフォールバックする。")]
    [SerializeField] private bool useRightHandForCursor = true;

    [Header("走者・レーン")]
    [Tooltip("各レーンの立ち位置。[0]=左, [1]=中央, [2]=右")]
    [SerializeField] private Transform[] lanePositions = new Transform[LaneCount];
    [Tooltip("見下ろし視点にするためlanePositions自体を傾けている場合、操作キャラ(防御役)の見た目まで" +
      "一緒に傾いてしまわないよう、向きだけはこちらを使う。未設定ならlanePositions[1]の向きをそのまま使う" +
      "（従来通り）。")]
    [SerializeField] private Transform defenderFacingReference;

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

    [Tooltip("回避1回あたりのスコア加算＝合計攻撃力×この係数。他ジャンルとスコア水準を揃えるための調整値" +
      "（詳細はSwimMinigame.scoreMultiplierのコメント参照）。")]
    [SerializeField] private float scoreMultiplier = 0.0286f;

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
    private bool _cursorTrackingEnabled;

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
      _cursorTrackingEnabled = true; // 練習中もカーソル追従でレーン移動を試せるようにする
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
      _cursorTrackingEnabled = false;
      _stunTimer = 0f;
      stunIndicator?.SetActive(false);
      ClearIceChunks();
      if (_defenderInstance != null) Destroy(_defenderInstance);
      _defenderInstance = null;
      DespawnLineup(_remainingLineup);
    }

    private void Update()
    {
      if (!_cursorTrackingEnabled || IsStunned) return;
      UpdateLaneFromCursor();
    }

    /// <summary>人差し指先のビューポートX座標（0=左端, 1=右端）を画面の縦3分割ゾーンに変換し、
    /// そのゾーンへ常時レーンを合わせる。指が検出できていない間は直前のレーンを維持する。</summary>
    private void UpdateLaneFromCursor()
    {
      if (handTrackingController == null) return;
      if (!handTrackingController.TryGetIndexFingertipViewport(useRightHandForCursor, out var viewport)) return;

      var lane = viewport.x < (1f / 3f) ? 0 : viewport.x > (2f / 3f) ? 2 : 1;
      if (lane == CurrentLane) return;

      CurrentLane = lane;
      UpdateDefenderPosition();
    }

    /// <summary>中央レーンに立つ1体以外の採用キャラを整列し直す（前回の召喚が残っていれば片付けてから再召喚する）。</summary>
    private void RefreshRemainingLineup()
    {
      DespawnLineup(_remainingLineup);
      _remainingLineup.AddRange(SpawnRemainingLineup(_defender, remainingLineupOrigin, remainingLineupAxis, remainingLineupSpacing));
    }

    /// <summary>レーン移動は<see cref="UpdateLaneFromCursor"/>によるカーソル追従のみで行うため、
    /// ジェスチャー購読は使わない。</summary>
    protected override void OnGestureForMinigame(GestureType type) { }

    private void SpawnDefender()
    {
      if (_defenderInstance != null) Destroy(_defenderInstance);
      if (_defender == null || _defender.characterPrefab == null || lanePositions == null || lanePositions.Length <= 1 || lanePositions[1] == null) return;

      var facing = defenderFacingReference != null ? defenderFacingReference.rotation : lanePositions[1].rotation;
      _defenderInstance = Instantiate(_defender.characterPrefab, lanePositions[1].position, facing);
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
      Score += totalAttackPower * scoreMultiplier;
      UpdateScoreText();
    }

    protected override float GetFinalScore() => Score;

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
