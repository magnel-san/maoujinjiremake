using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【戦闘】仕様4.6：「両拳を交互に突き出す（パンチ）」で衝撃波（こぶしのオブジェクト）を飛ばし、
  /// 迫る勇者たちと乱闘する。遊泳と同じ「接近度」構造で、迫る勇者を押し戻す。
  /// </summary>
  public class CombatMinigame : MinigameBase
  {
    [SerializeField] private float _heroApproachSpeed = 0.05f; // 0(遠い)〜1(目前)
    [SerializeField] private float _heroPushBackPerHit = 0.12f;

    [Header("勇者オブジェクト")]
    [Tooltip("勇者本体。接近度(0〜1)に応じてheroFarPoint〜heroNearPointの間を移動させる。")]
    [SerializeField] private Transform heroTransform;
    [SerializeField] private Transform heroFarPoint;
    [SerializeField] private Transform heroNearPoint;

    [Header("こぶしオブジェクト")]
    [Tooltip("パンチのたびに召喚するこぶしのプレハブ")]
    [SerializeField] private GameObject fistPrefab;
    [Tooltip("こぶしを召喚する位置・向き（未設定ならこのオブジェクトの位置を使う）")]
    [SerializeField] private Transform fistSpawnPoint;
    [SerializeField] private float fistLifetime = 1.5f;

    [Header("UI")]
    [Tooltip("ゲームオーバーになるまでの距離を示すスクロールバー。ハンドルに勇者アイコンを設定する想定。")]
    [SerializeField] private Slider heroProximitySlider;
    [Tooltip("勇者へ与えた合計ダメージ（スコア）を表示するテキスト")]
    [SerializeField] private TMP_Text heroDamageText;

    public float TotalDamage { get; private set; }
    public float HeroProximity01 { get; private set; }

    protected override void OnMinigameStart()
    {
      TotalDamage = 0f;
      HeroProximity01 = 0f;
      UpdateHeroVisual();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 + _heroApproachSpeed * deltaTime);
      UpdateHeroVisual();

      if (HeroProximity01 >= 1f)
      {
        FinishAsGameOver();
      }
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type != GestureType.AlternatingPunch) return;

      SpawnFist();
      TotalDamage += totalAttackPower;
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 - _heroPushBackPerHit);
      UpdateHeroVisual();
    }

    /// <summary>練習中の練習用：こぶしは飛ばすが、ダメージ・接近度には影響させない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.AlternatingPunch) return;
      SpawnFist();
    }

    private void SpawnFist()
    {
      if (fistPrefab == null) return;
      var point = fistSpawnPoint != null ? fistSpawnPoint : transform;
      var instance = Instantiate(fistPrefab, point.position, point.rotation);
      Destroy(instance, Mathf.Max(fistLifetime, 0.01f));
    }

    private void UpdateHeroVisual()
    {
      if (heroTransform != null && heroFarPoint != null && heroNearPoint != null)
      {
        heroTransform.position = Vector3.Lerp(heroFarPoint.position, heroNearPoint.position, HeroProximity01);
      }

      if (heroProximitySlider != null) heroProximitySlider.value = HeroProximity01;
      if (heroDamageText != null) heroDamageText.text = $"勇者へのダメージ: {TotalDamage:0}";
    }
  }
}
