using System.Collections.Generic;
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
    [Tooltip("命中1回あたりのスコア加算＝合計攻撃力×この係数。他ジャンルとスコア水準を揃えるための調整値" +
      "（詳細はSwimMinigame.scoreMultiplierのコメント参照）。")]
    [SerializeField] private float scoreMultiplier = 0.025f;

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
    [Tooltip("こぶしオブジェクトが自動的に消えるまでの秒数（勇者に当たらなかった場合の保険）")]
    [SerializeField] private float fistLifetime = 1.5f;
    [Tooltip("同時に存在できるこぶしオブジェクトの最大数。連続発火で大量発生するのを防ぐ安全弁。")]
    [SerializeField] private int maxConcurrentFists = 3;
    [Tooltip("こぶしが前進する速度(m/s)")]
    [SerializeField] private float fistSpeed = 8f;

    [Header("UI")]
    [Tooltip("ゲームオーバーになるまでの距離を示すスクロールバー。ハンドルに勇者アイコンを設定する想定。")]
    [SerializeField] private Slider heroProximitySlider;
    [Tooltip("勇者へ与えた合計ダメージ（スコア）を表示するテキスト")]
    [SerializeField] private TMP_Text heroDamageText;

    public float TotalDamage { get; private set; }
    public float HeroProximity01 { get; private set; }

    private readonly List<GameObject> _activeFists = new List<GameObject>();

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
      SpawnFist(isPractice: false);
    }

    /// <summary>練習中の練習用：こぶしは前進して飛んでいくが、勇者に当たってもダメージ・接近度には影響させない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.AlternatingPunch) return;
      SpawnFist(isPractice: true);
    }

    /// <summary>こぶしが実際に勇者へ命中した瞬間に呼ばれる（FistProjectile経由）。</summary>
    private void HandleFistHit()
    {
      TotalDamage += totalAttackPower * scoreMultiplier;
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 - _heroPushBackPerHit);
      UpdateHeroVisual();
    }

    protected override float GetFinalScore() => TotalDamage;

    private void SpawnFist(bool isPractice)
    {
      if (fistPrefab == null) return;

      _activeFists.RemoveAll(f => f == null);
      if (_activeFists.Count >= Mathf.Max(maxConcurrentFists, 1)) return; // 同時出現数の上限に達していたら抑制する

      var point = fistSpawnPoint != null ? fistSpawnPoint : transform;
      var instance = Instantiate(fistPrefab, point.position, point.rotation);
      _activeFists.Add(instance);
      Destroy(instance, Mathf.Max(fistLifetime, 0.01f)); // 当たらなかった場合の保険としての自動消滅

      var projectile = instance.GetComponent<FistProjectile>();
      if (projectile != null)
      {
        if (isPractice) projectile.Initialize(fistSpeed, null, null);
        else projectile.Initialize(fistSpeed, heroTransform, HandleFistHit);
      }
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
