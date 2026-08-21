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
    [Tooltip("波オブジェクトが自動的に消えるまでの秒数")]
    [SerializeField] private float waveLifetime = 2f;

    [Header("UI")]
    [Tooltip("ゲームオーバーになるまでの距離を示すスクロールバー。ハンドルに勇者アイコンを設定する想定。")]
    [SerializeField] private Slider heroProximitySlider;
    [Tooltip("勇者へ与えた合計ダメージ（スコア）を表示するテキスト")]
    [SerializeField] private TMP_Text heroDamageText;

    public float TotalDamage { get; private set; }
    public float HeroProximity01 { get; private set; } // 0=遠い, 1=目前

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

      SpawnWave();
      TotalDamage += totalAttackPower;
      HeroProximity01 = Mathf.Clamp01(HeroProximity01 - _heroPushBackPerHit);
      UpdateHeroVisual();
    }

    /// <summary>ルール説明中の練習用：波は出すが、ダメージ・接近度には影響させない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type != GestureType.SwipeSideways) return;
      SpawnWave();
    }

    private void SpawnWave()
    {
      if (wavePrefab == null) return;
      var point = waveSpawnPoint != null ? waveSpawnPoint : transform;
      var instance = Instantiate(wavePrefab, point.position, point.rotation);
      Destroy(instance, Mathf.Max(waveLifetime, 0.01f));
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
