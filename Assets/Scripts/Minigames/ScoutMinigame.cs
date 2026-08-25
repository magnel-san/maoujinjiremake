using System.Collections.Generic;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【偵察】仕様4.5：3D上空視点のマップ内に次々と現れる勇者拠点をポインターで指して発見する。
  /// 最大3つまで同時に存在し、1秒おきに新しい拠点が生成される。発見すると消滅し、また補充される。
  /// このミニゲームはジェスチャーではなくポインター保持で進行するため、
  /// <see cref="OnGestureForMinigame"/>は使用しない（<see cref="ScoutBaseTarget"/>から呼ばれる）。
  /// </summary>
  public class ScoutMinigame : MinigameBase
  {
    [Header("拠点の自動生成")]
    [SerializeField] private GameObject baseTargetPrefab;
    [Tooltip("拠点が出現しうる位置の一覧。同時に使われている位置には重ねて生成しない。")]
    [SerializeField] private Transform[] baseSpawnPoints;
    [SerializeField] private int maxConcurrentBases = 3;
    [SerializeField] private float spawnInterval = 1f;
    [Tooltip("発見1回あたりのスコア加算＝合計攻撃力×この係数。他ジャンルとスコア水準を揃えるための調整値" +
      "（詳細はSwimMinigame.scoreMultiplierのコメント参照）。")]
    [SerializeField] private float scoreMultiplier = 0.0333f;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;

    [Header("カーソル")]
    [Tooltip("このミニゲーム中だけ使う専用カーソル（虫眼鏡等）。未設定ならデフォルトのままにする。")]
    [SerializeField] private PointerController pointerController;
    [SerializeField] private GameObject customPointerVisualPrefab;

    public float Score { get; private set; }
    public int BasesFound { get; private set; }

    private readonly List<(GameObject instance, Transform point)> _activeBases = new List<(GameObject, Transform)>();
    private float _spawnTimer;

    protected override void OnRulesShown()
    {
      if (pointerController != null) pointerController.SetPointerVisual(customPointerVisualPrefab);
    }

    protected override void OnMinigameStart()
    {
      Score = 0f;
      BasesFound = 0;
      ClearBases();
      _spawnTimer = 0f;
      UpdateScoreText();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      _activeBases.RemoveAll(b => b.instance == null); // 発見されて消滅した分をここで反映する

      _spawnTimer -= deltaTime;
      if (_spawnTimer <= 0f)
      {
        if (_activeBases.Count < maxConcurrentBases) SpawnBase();
        _spawnTimer = Mathf.Max(spawnInterval, 0.05f);
      }
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      ClearBases();
      if (pointerController != null) pointerController.ResetPointerVisual();
    }

    /// <summary>シーン上の<see cref="ScoutBaseTarget"/>が発見判定した際に呼ばれる。</summary>
    public void RegisterBaseDiscovered()
    {
      if (!IsRunning) return;
      Score += totalAttackPower * scoreMultiplier;
      BasesFound++;
      UpdateScoreText();
    }

    protected override float GetFinalScore() => Score;

    protected override void OnGestureForMinigame(GestureType type) { }

    private void SpawnBase()
    {
      if (baseTargetPrefab == null || baseSpawnPoints == null || baseSpawnPoints.Length == 0) return;

      var used = new HashSet<Transform>();
      foreach (var b in _activeBases)
      {
        if (b.instance != null) used.Add(b.point);
      }

      var available = new List<Transform>();
      foreach (var point in baseSpawnPoints)
      {
        if (point != null && !used.Contains(point)) available.Add(point);
      }
      if (available.Count == 0) return;

      var chosen = available[Random.Range(0, available.Count)];
      var instance = Instantiate(baseTargetPrefab, chosen.position, chosen.rotation);
      var target = instance.GetComponent<ScoutBaseTarget>();
      target?.Initialize(this);

      _activeBases.Add((instance, chosen));
    }

    private void ClearBases()
    {
      foreach (var b in _activeBases)
      {
        if (b.instance != null) Destroy(b.instance);
      }
      _activeBases.Clear();
    }

    private void UpdateScoreText()
    {
      if (scoreText != null) scoreText.text = $"発見数: {BasesFound}（スコア {Score:0}）";
    }
  }
}
