using System.Collections.Generic;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.Minigames
{
  /// <summary>【遊泳】仕様4.1：「手を横に払う」で波を召喚し、正面から接近する勇者を押し戻す。</summary>
  public class SwimMinigame : MinigameBase
  {
    [SerializeField] private float _heroApproachSpeed = 0.05f; // 0(遠い)〜1(目前)
    [SerializeField] private float _heroPushBackPerHit = 0.15f;
    [Tooltip("命中1回あたりのスコア加算＝合計攻撃力×この係数。制限時間内に達成できる命中回数の目安(約40回)の" +
      "逆数にしてあり、ヒット数がジェスチャーのクールダウンだけで頭打ちになる遊泳/戦闘/労働系ジャンルの" +
      "スコアを、他ジャンルと同じ「合計攻撃力を基準値とした水準」に揃えるためのもの。")]
    [SerializeField] private float scoreMultiplier = 0.025f;

    [Header("勇者オブジェクト")]
    [Tooltip("勇者本体。接近度(0〜1)に応じてheroFarPoint〜heroNearPointの間を移動させる。")]
    [SerializeField] private Transform heroTransform;
    [SerializeField] private Transform heroFarPoint;
    [SerializeField] private Transform heroNearPoint;

    [Header("波オブジェクト")]
    [Tooltip("横に払うたびに召喚する波のプレハブ")]
    [SerializeField] private GameObject wavePrefab;
    [Tooltip("波を召喚する位置・向き（未設定ならこのオブジェクトの位置を使う）")]
    [SerializeField] private Transform waveSpawnPoint;
    [Tooltip("波オブジェクトが自動的に消えるまでの秒数（勇者に当たらなかった場合の保険）")]
    [SerializeField] private float waveLifetime = 2f;
    [Tooltip("同時に存在できる波オブジェクトの最大数。連続発火で大量発生するのを防ぐ安全弁。")]
    [SerializeField] private int maxConcurrentWaves = 3;
    [Tooltip("波が前進する速度(m/s)")]
    [SerializeField] private float waveSpeed = 5f;

    [Header("UI")]
    [Tooltip("ゲームオーバーになるまでの距離を示すスクロールバー。ハンドルに勇者アイコンを設定する想定。")]
    [SerializeField] private Slider heroProximitySlider;
    [Tooltip("勇者へ与えた合計ダメージ（スコア）を表示するテキスト")]
    [SerializeField] private TMP_Text heroDamageText;

    public float TotalDamage { get; private set; }
    public float HeroProximity01 { get; private set; } // 0=遠い, 1=目前

    private readonly List<GameObject> _activeWaves = new List<GameObject>();

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
      if (type != GestureType.SwipeSideways) return;
      PlayMotionSfx();
      SpawnWave(isPractice: false);
    }

    /// <summary>ルール説明中の練習用：波は前進して飛んでいくが、勇者に当たってもダメージ・接近度には影響させない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.SwipeSideways) return;
      PlayMotionSfx();
      SpawnWave(isPractice: true);
    }

    /// <summary>波が実際に勇者へ命中した瞬間に呼ばれる（WaveProjectile経由）。</summary>
    private void HandleWaveHit()
    {
      TotalDamage += totalAttackPower * scoreMultiplier;
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 - _heroPushBackPerHit);
      UpdateHeroVisual();
    }

    protected override float GetFinalScore() => TotalDamage;

    private void SpawnWave(bool isPractice)
    {
      if (wavePrefab == null) return;

      _activeWaves.RemoveAll(w => w == null);
      if (_activeWaves.Count >= Mathf.Max(maxConcurrentWaves, 1)) return; // 同時出現数の上限に達していたら抑制する

      var point = waveSpawnPoint != null ? waveSpawnPoint : transform;
      var instance = Instantiate(wavePrefab, point.position, point.rotation);
      _activeWaves.Add(instance);
      Destroy(instance, Mathf.Max(waveLifetime, 0.01f)); // 当たらなかった場合の保険としての自動消滅

      var projectile = instance.GetComponent<WaveProjectile>();
      if (projectile != null)
      {
        if (isPractice) projectile.Initialize(waveSpeed, null, null);
        else projectile.Initialize(waveSpeed, heroTransform, HandleWaveHit);
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
