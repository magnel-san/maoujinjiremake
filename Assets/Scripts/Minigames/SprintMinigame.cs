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
  /// 「間に合わせるために全力疾走する」という切迫した状況に変更した。「両腕を振る」で前進する操作自体は
  /// そのまま。城門(<see cref="gatePoint"/>)に間に合えば防衛成功。
  /// </summary>
  public class SprintMinigame : MinigameBase
  {
    [SerializeField] private float _trackLength = 100f;
    [SerializeField] private float _distancePerMotionFactor = 0.5f;

    [Header("走者")]
    [SerializeField] private Transform runnerStartPoint;
    [Tooltip("先回りする先＝城門の位置")]
    [SerializeField] private Transform gatePoint;
    [Tooltip("練習中、腕を振った時に一瞬前へ弾む距離（見た目のフィードバックのみ、実際の距離には影響しない）")]
    [SerializeField] private float practiceBounceDistance = 0.3f;

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

    public float DistanceTravelled { get; private set; }

    // 走者＋残りの整列を自前でスポーンするため、基底クラスの一括召喚は使わない
    // （両方動くと走者が二重に召喚されてしまう）。
    protected override bool SkipGenericCharacterSummon => true;

    protected override void OnRulesShown()
    {
      _runner = PickRandomAssigned();
      SpawnRunner();
      RefreshRemainingLineup();
    }

    protected override void OnMinigameStart()
    {
      DistanceTravelled = 0f;
      SpawnRunner();
      RefreshRemainingLineup();
      UpdateRunnerPosition();
      UpdateDistanceText();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      if (DistanceTravelled >= _trackLength)
      {
        FinishAsDefenseSuccess(); // 城門に間に合った＝通せんぼ成功
      }
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      if (_runnerInstance != null) Destroy(_runnerInstance);
      _runnerInstance = null;
      DespawnLineup(_remainingLineup);
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

      DistanceTravelled = Mathf.Min(_trackLength, DistanceTravelled + totalAttackPower * _distancePerMotionFactor);
      UpdateRunnerPosition();
      UpdateDistanceText();
    }

    /// <summary>練習中：腕振りモーション自体を試せるよう、走者を一瞬弾ませるだけで実際の到達距離には影響しない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.ArmSwingBoth || _runnerInstance == null || runnerStartPoint == null) return;
      _runnerInstance.transform.position = runnerStartPoint.position + runnerStartPoint.forward * practiceBounceDistance;
    }

    private void SpawnRunner()
    {
      if (_runnerInstance != null) Destroy(_runnerInstance);
      if (_runner == null || _runner.characterPrefab == null || runnerStartPoint == null) return;

      _runnerInstance = Instantiate(_runner.characterPrefab, runnerStartPoint.position, runnerStartPoint.rotation);
    }

    private void UpdateRunnerPosition()
    {
      if (_runnerInstance == null || runnerStartPoint == null || gatePoint == null) return;
      var t = _trackLength > 0f ? Mathf.Clamp01(DistanceTravelled / _trackLength) : 0f;
      _runnerInstance.transform.position = Vector3.Lerp(runnerStartPoint.position, gatePoint.position, t);
    }

    private void UpdateDistanceText()
    {
      if (distanceText != null) distanceText.text = $"到達距離: {DistanceTravelled:0}/{_trackLength:0}m";
    }
  }
}
