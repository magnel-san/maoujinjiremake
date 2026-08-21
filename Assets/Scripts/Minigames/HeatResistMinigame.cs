using System.Collections.Generic;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【耐熱】仕様4.9：「胸の前でぐるぐるバルブを回す」でマグマを放出する。
  /// GestureRecognizerのValveSpinは実際に回転した量に比例して発火するため、1発火＝1周分として扱う。
  /// 回すたびに箱の中へマグマオブジェクトを1つ召喚するが、大量召喚で重くならないよう、
  /// <see cref="magmaPerPack"/>個たまるたびにそれらをまとめて1つの「ある程度たまった」オブジェクトに置き換える。
  /// </summary>
  public class HeatResistMinigame : MinigameBase
  {
    [Header("マグマオブジェクト")]
    [Tooltip("バルブを1周回すたびに召喚する小さいマグマオブジェクト")]
    [SerializeField] private GameObject magmaObjectPrefab;
    [Tooltip("magmaPerPack個たまった時に、それらの代わりに置く「まとまった」マグマオブジェクト")]
    [SerializeField] private GameObject magmaPackPrefab;
    [SerializeField] private Transform magmaBoxCenter;
    [SerializeField] private float magmaBoxRadius = 1f;
    [SerializeField] private int magmaPerPack = 10;

    [Header("背景")]
    [Tooltip("まとめた回数（パック数）に応じて切り替える背景")]
    [SerializeField] private Renderer backgroundRenderer;
    [SerializeField] private Material[] backgroundStages;

    [Header("UI")]
    [SerializeField] private TMP_Text magmaGaugeText;

    public float TotalMagma { get; private set; }

    private readonly List<GameObject> _pendingMagmaObjects = new List<GameObject>();
    private int _packCount;

    protected override void OnMinigameStart()
    {
      TotalMagma = 0f;
      _packCount = 0;
      ClearPendingMagma();
      UpdateGaugeText();
      UpdateBackground();
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      ClearPendingMagma();
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.ValveSpin) return;

      TotalMagma += totalAttackPower;
      SpawnMagmaObject();
      UpdateGaugeText();
    }

    /// <summary>練習中：バルブ回しの動作自体は試せるが、マグマは実際には蓄積されない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.ValveSpin || magmaObjectPrefab == null || magmaBoxCenter == null) return;

      var offset = Random.insideUnitSphere * magmaBoxRadius;
      offset.y = Mathf.Abs(offset.y);
      var instance = Instantiate(magmaObjectPrefab, magmaBoxCenter.position + offset, Quaternion.identity);
      Destroy(instance, 1f); // 練習用の一時的な見た目なのですぐ消す
    }

    private void SpawnMagmaObject()
    {
      if (magmaObjectPrefab == null || magmaBoxCenter == null) return;

      var offset = Random.insideUnitSphere * magmaBoxRadius;
      offset.y = Mathf.Abs(offset.y); // 箱の底より上、積み上がるイメージ
      var instance = Instantiate(magmaObjectPrefab, magmaBoxCenter.position + offset, Quaternion.identity);
      _pendingMagmaObjects.Add(instance);

      if (_pendingMagmaObjects.Count >= magmaPerPack)
      {
        ConsumeIntoPack();
      }
    }

    /// <summary>小さいマグマオブジェクトがmagmaPerPack個たまったら、まとめて1つの「まとまった」表現に置き換える。
    /// 大量のオブジェクトを個別に残し続けると重くなるための対策。</summary>
    private void ConsumeIntoPack()
    {
      foreach (var obj in _pendingMagmaObjects)
      {
        if (obj != null) Destroy(obj);
      }
      _pendingMagmaObjects.Clear();

      _packCount++;
      UpdateBackground();

      if (magmaPackPrefab != null && magmaBoxCenter != null)
      {
        Instantiate(magmaPackPrefab, magmaBoxCenter.position, Quaternion.identity, magmaBoxCenter);
      }
    }

    private void ClearPendingMagma()
    {
      foreach (var obj in _pendingMagmaObjects)
      {
        if (obj != null) Destroy(obj);
      }
      _pendingMagmaObjects.Clear();
    }

    private void UpdateBackground()
    {
      if (backgroundRenderer == null || backgroundStages == null || backgroundStages.Length == 0) return;
      var index = Mathf.Min(_packCount, backgroundStages.Length - 1);
      backgroundRenderer.material = backgroundStages[index];
    }

    private void UpdateGaugeText()
    {
      if (magmaGaugeText != null) magmaGaugeText.text = $"マグマ量: {TotalMagma:0}（{_pendingMagmaObjects.Count}/{magmaPerPack}）";
    }
  }
}
