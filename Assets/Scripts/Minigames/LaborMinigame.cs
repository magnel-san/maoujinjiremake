using System.Collections.Generic;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【労働】仕様4.4：「腕を振り下ろす（ハンマー）」1回＝合計攻撃力分ゲージ蓄積。
  /// ゲージが満了するたびに3Dオブジェクトを箱の中に1つ召喚して積み上げていき、
  /// 積み上げた回数（＝ゲージを満たした回数）ごとに背景も変える。
  /// </summary>
  public class LaborMinigame : MinigameBase
  {
    [SerializeField] private float _gaugeMax = 100f;
    [Tooltip("ハンマー1回あたりのスコア加算＝合計攻撃力×この係数。積み上げ演出用のCurrentGauge/" +
      "CompletedGaugeCount（合計攻撃力がgaugeMaxに対して大きいと1振りで何段も一気に積み上がる、" +
      "見た目のペース調整用の値）とは切り離してあり、スコアは他ジャンル同様「振った回数×合計攻撃力」で" +
      "決まる（詳細はSwimMinigame.scoreMultiplierのコメント参照）。ゲージ経由にすると、合計攻撃力が" +
      "高いほど1振りあたりの完了回数も増えてスコアが合計攻撃力の2乗に比例してしまうため、あえて分けている。")]
    [SerializeField] private float scoreMultiplier = 0.025f;

    public float Score { get; private set; }

    [Header("ゲージUI")]
    [SerializeField] private Slider gaugeSlider;
    [SerializeField] private TMP_Text completedCountText;

    [Header("積み上げオブジェクト")]
    [Tooltip("ゲージが満了するたびに箱の中へ召喚するオブジェクト")]
    [SerializeField] private GameObject stackObjectPrefab;
    [Tooltip("積み上げの基準位置（箱の底の中心あたり）")]
    [SerializeField] private Transform stackBasePoint;
    [SerializeField] private float stackHeightPerObject = 0.3f;

    [Header("背景")]
    [Tooltip("満了回数に応じて切り替える背景。0回目=[0]、1回目=[1]…最後の要素より多い場合は最後の要素のままにする。")]
    [SerializeField] private Renderer backgroundRenderer;
    [SerializeField] private Material[] backgroundStages;

    public float CurrentGauge { get; private set; }
    public int CompletedGaugeCount { get; private set; }

    private readonly List<GameObject> _stackedInstances = new List<GameObject>();

    protected override void OnMinigameStart()
    {
      Score = 0f;
      CurrentGauge = 0f;
      CompletedGaugeCount = 0;
      ClearStack();
      UpdateGaugeUI();
      UpdateBackground();
    }

    protected override float GetFinalScore() => Score;

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      // 積み上げた成果物は演出として残す（片付けない）。次にこのミニゲームを実行する際、
      // OnMinigameStartのClearStack()で改めて片付けてから再開する。
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.HammerSwingDown) return;

      Score += totalAttackPower * scoreMultiplier;

      CurrentGauge += totalAttackPower;
      UpdateGaugeUI();

      if (CurrentGauge >= _gaugeMax)
      {
        CurrentGauge -= _gaugeMax;
        CompletedGaugeCount++;
        SpawnStackObject();
        UpdateBackground();
        UpdateGaugeUI();
      }
    }

    private void SpawnStackObject()
    {
      if (stackObjectPrefab == null || stackBasePoint == null) return;

      var position = stackBasePoint.position + Vector3.up * (stackHeightPerObject * _stackedInstances.Count);
      var instance = Instantiate(stackObjectPrefab, position, stackBasePoint.rotation);
      _stackedInstances.Add(instance);
    }

    private void ClearStack()
    {
      foreach (var instance in _stackedInstances)
      {
        if (instance != null) Destroy(instance);
      }
      _stackedInstances.Clear();
    }

    private void UpdateBackground()
    {
      if (backgroundRenderer == null || backgroundStages == null || backgroundStages.Length == 0) return;
      var index = Mathf.Min(CompletedGaugeCount, backgroundStages.Length - 1);
      backgroundRenderer.material = backgroundStages[index];
    }

    private void UpdateGaugeUI()
    {
      if (gaugeSlider != null) gaugeSlider.value = _gaugeMax > 0f ? CurrentGauge / _gaugeMax : 0f;
      if (completedCountText != null) completedCountText.text = $"完了回数: {CompletedGaugeCount}";
    }
  }
}
