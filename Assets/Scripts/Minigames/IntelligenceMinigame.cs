using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【知性】仕様4.7：円周上に並んだ5つのノード（五芒星の頂点）を、指定された一筆書きの順番で
  /// 人差し指でなぞり、両手を合わせて起動する。
  ///
  /// なぞり順は、円周上に並んだ順の各頂点(0〜4)を2つ飛ばしで結ぶ五芒星の一筆書き順
  /// （0→2→4→1→3→0）に固定している。<see cref="nodes"/>は円周上に並んだ順（0〜4）で
  /// 設定する前提。正しい順に全て触れてから両手を合わせるとスコア加算、途中で順番を間違えた場合は
  /// 両手を合わせても加算されず、線がリセットされて最初からやり直しになる
  /// （ミスに気付いた時点で即失敗にはせず、両手を合わせるまでは猶予がある）。
  /// </summary>
  public class IntelligenceMinigame : MinigameBase
  {
    private static readonly int[] PentagramOrder = { 0, 2, 4, 1, 3 };

    [Header("ノード（円周上に並んだ順で0〜4を設定する）")]
    [SerializeField] private MagicCircleNode[] nodes = new MagicCircleNode[5];

    [Header("案内線")]
    [Tooltip("正しいなぞり順を示すガイド線。未設定なら実行時に自動生成する。")]
    [SerializeField] private LineRenderer guideLine;
    [Tooltip("案内線に使うマテリアル。未設定の場合、実行時にURP向けのUnlitマテリアルを自動生成する" +
      "（Built-in用の\"Sprites/Default\"シェーダーはURPでは正しく描画されず紫/ピンク色になるため使わない）。")]
    [SerializeField] private Material guideLineMaterial;
    [SerializeField] private float guideLineWidth = 0.03f;
    [SerializeField] private Color guideLineColor = Color.cyan;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;

    [Header("カーソル")]
    [Tooltip("このミニゲーム中だけ使う専用カーソル（指先アイコン等）。未設定ならデフォルトのままにする。")]
    [SerializeField] private PointerController pointerController;
    [SerializeField] private GameObject customPointerVisualPrefab;

    public float Score { get; private set; }
    public int CompletionCount { get; private set; }

    private int _nextExpectedStep;
    private bool _mistakeMade;

    protected override void OnRulesShown()
    {
      if (pointerController != null) pointerController.SetPointerVisual(customPointerVisualPrefab);
    }

    protected override void OnMinigameStart()
    {
      Score = 0f;
      CompletionCount = 0;
      ResetTrace();
      UpdateScoreText();
      DrawGuideLine();
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      if (guideLine != null) guideLine.gameObject.SetActive(false);
      if (pointerController != null) pointerController.ResetPointerVisual();
    }

    /// <summary>シーン上の<see cref="MagicCircleNode"/>が接続判定した際に呼ばれる。</summary>
    public void RegisterNodeTouched(int nodeIndex)
    {
      if (!IsRunning) return;

      if (_nextExpectedStep < PentagramOrder.Length && nodeIndex == PentagramOrder[_nextExpectedStep])
      {
        _nextExpectedStep++;
        Debug.Log($"[Intelligence] node {nodeIndex} 正解（{_nextExpectedStep}/{PentagramOrder.Length}）");
      }
      else
      {
        // すぐには失敗にせず、両手を合わせた時点で判定する（ミスに気付いてもそこで終わりにしない）。
        _mistakeMade = true;
        Debug.Log($"[Intelligence] node {nodeIndex} ミス（次に期待していたのは {(_nextExpectedStep < PentagramOrder.Length ? PentagramOrder[_nextExpectedStep].ToString() : "なし（既に全部触れている）")}）");
      }
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.HandsTogether) return;

      // HandsTogether自体は届いているが、ゲーム内で何も起きていないように見える場合の切り分け用ログ。
      Debug.Log($"[Intelligence] HandsTogether受信: 進捗={_nextExpectedStep}/{PentagramOrder.Length} ミス={_mistakeMade}");

      if (!_mistakeMade && _nextExpectedStep >= PentagramOrder.Length)
      {
        Score += totalAttackPower;
        CompletionCount++;
        UpdateScoreText();
        Debug.Log($"[Intelligence] 成功！ スコア+{totalAttackPower} 合計={Score}");
      }
      else
      {
        Debug.Log("[Intelligence] 不成立のためリセット（順番を最後まで正しく触れていない）");
      }

      ResetTrace();
    }

    private void ResetTrace()
    {
      _nextExpectedStep = 0;
      _mistakeMade = false;
      foreach (var node in nodes)
      {
        node?.ResetNode();
      }
    }

    private void DrawGuideLine()
    {
      if (nodes == null || nodes.Length < PentagramOrder.Length) return;

      if (guideLine == null)
      {
        var go = new GameObject("MagicCircleGuideLine");
        go.transform.SetParent(transform, false);
        guideLine = go.AddComponent<LineRenderer>();
        guideLine.useWorldSpace = true;
        guideLine.loop = true;
        guideLine.widthMultiplier = guideLineWidth;
        guideLine.material = guideLineMaterial != null ? guideLineMaterial : CreateFallbackLineMaterial();
        guideLine.startColor = guideLineColor;
        guideLine.endColor = guideLineColor;
      }

      guideLine.gameObject.SetActive(true);
      guideLine.positionCount = PentagramOrder.Length;
      for (var i = 0; i < PentagramOrder.Length; i++)
      {
        var node = nodes[PentagramOrder[i]];
        if (node != null) guideLine.SetPosition(i, node.transform.position);
      }
    }

    /// <summary>guideLineMaterial未設定時のフォールバック。Built-inの"Sprites/Default"はURPでは
    /// 正しく描画できず紫/ピンク色になってしまうため、URP向けのUnlitシェーダーを優先して探す。
    /// どれも見つからない場合のみ、最終手段としてSprites/Defaultを使う。</summary>
    private static Material CreateFallbackLineMaterial()
    {
      var shader = Shader.Find("Universal Render Pipeline/Unlit")
        ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
        ?? Shader.Find("Sprites/Default");
      return shader != null ? new Material(shader) : null;
    }

    private void UpdateScoreText()
    {
      if (scoreText != null) scoreText.text = $"起動回数: {CompletionCount}（スコア {Score:0}）";
    }
  }
}
