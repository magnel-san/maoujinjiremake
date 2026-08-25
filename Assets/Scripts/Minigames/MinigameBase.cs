using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using DemonLordHR.UI;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  public enum MinigameResult
  {
    None,
    DefenseSuccess,
    GameOver,
  }

  /// <summary>
  /// 9ジャンル共通のミニゲームライフサイクルを提供する抽象基底クラス。
  /// プレイヤーのワープ→採用キャラの召喚→ルール説明→「準備完了」ゲージ→制限時間カウント→終了判定、
  /// という流れをテンプレートメソッドで共通化し、各ジャンル固有のロジック（購読するジェスチャー、
  /// スコア/ダメージ計算、勝敗条件）は派生クラスで実装する。
  ///
  /// 採用キャラの合計攻撃力(<see cref="totalAttackPower"/>)は各派生クラスのスコア/ダメージ計算に
  /// そのまま使われており（例：ヒット1回につきtotalAttackPower分加算）、これが実質的な
  /// 「編成の強さ＝スコアの伸びやすさ」になっている。<see cref="attackPowerText"/>はその数値を
  /// プレイヤーに常時見せるための表示。
  ///
  /// 移動はカメラ単体ではなく「プレイヤー本体」（Main Camera・WorldUICameraを子に持つ親オブジェクト）を
  /// 位置だけ瞬間移動（ワープ）させる方式にしている。カメラだけを動かすと世界UIカメラとズレるため、
  /// 親ごと動かして両方のカメラの相対位置関係を保つ。向き(回転)は変更しない（プレイヤーが今向いている
  /// 方向のまま、立ち位置だけが変わる）。手のトラッキング(<see cref="HandTrackingController"/>)は
  /// 毎フレームカメラの現在位置を基準に計算し直すため、特別な追従処理をしなくても自動的についてくる。
  /// </summary>
  public abstract class MinigameBase : MonoBehaviour
  {
    [SerializeField] protected GameSettings settings;
    [SerializeField] protected GestureRecognizer gestureRecognizer;
    [Tooltip("ルール説明画像を閉じて練習操作に入るためのゲージボタン。")]
    [SerializeField] protected CircularHoldButton closeRuleButton;
    [Tooltip("練習を終えてミニゲームを本番開始するためのゲージボタン。")]
    [SerializeField] protected CircularHoldButton readyButton;
    [SerializeField] protected float timeLimitOverride = -1f;

    [Header("採用キャラの召喚")]
    [Tooltip("採用キャラを召喚する位置。先頭から順に1体ずつ使う。採用人数が位置の数より多い場合は" +
      "最後の位置を使い回す（重なって表示される）。")]
    [SerializeField] protected Transform[] characterSpawnPoints;

    [Header("プレイヤーのワープ")]
    [Tooltip("このミニゲームの場所へワープさせるプレイヤー本体（Main Camera・WorldUICameraの親）。" +
      "未設定ならワープしない。")]
    [SerializeField] protected Transform playerRoot;
    [Tooltip("ワープ先の位置。既定では位置だけを使い、向き(回転)はplayerRootの現在の向きのまま変更しない" +
      "（applyWarpRotationがtrueの場合のみ、向きもこのTransformに合わせる）。")]
    [SerializeField] protected Transform warpTarget;
    [Tooltip("trueの場合、ワープ時にプレイヤーの向きもwarpTargetの向きに合わせる（横視点にしたい飛行等で使う）。" +
      "終了時に元の向きへ自動的に戻す。falseなら従来通り向きは変更しない。")]
    [SerializeField] protected bool applyWarpRotation;

    [Header("UI")]
    [Tooltip("編成（このジャンルで採用したキャラ）の合計攻撃力を表示するテキスト")]
    [SerializeField] protected TMP_Text attackPowerText;
    [Tooltip("ルール説明画像を表示するUIのルート。中身の画像はジャンルごとにInspectorで設定する。" +
      "closeRuleButtonのゲージに合わせるまで表示し、閉じると同時に練習操作が有効になる。")]
    [SerializeField] protected GameObject ruleImageRoot;
    [Tooltip("残り時間を表示するテキスト。本番の制限時間カウント中だけ表示する。")]
    [SerializeField] protected TMP_Text timerText;

    [Header("結果表示")]
    [Tooltip("終了時（防衛成功／ゲームオーバーいずれも）に、GetFinalScoreの値を数秒間表示するUIのルート。" +
      "未設定ならresultScoreText自体の表示/非表示で代用する。両方未設定なら結果表示自体を行わない。")]
    [SerializeField] protected GameObject resultRoot;
    [Tooltip("結果（勝敗＋スコア）を書き込むテキスト。")]
    [SerializeField] protected TMP_Text resultScoreText;

    [Header("効果音")]
    [Tooltip("効果音の再生に使うAudioSource。未設定ならこのGameObjectのAudioSourceを自動取得/追加する。")]
    [SerializeField] protected AudioSource audioSource;
    [Tooltip("モーション（ジェスチャー成功／ポインター操作成功など、このジャンルの「1回分の操作」が" +
      "成立した瞬間）に鳴らす効果音。未設定なら再生しない。")]
    [SerializeField] protected AudioClip motionSfx;

    public event Action<MinigameResult> OnMinigameFinished;

    protected List<CharacterData> assignedCharacters = new List<CharacterData>();
    protected float totalAttackPower;
    protected float remainingTime;
    protected bool isRunning;
    protected MinigameResult result = MinigameResult.None;

    private readonly List<GameObject> _summonedInstances = new List<GameObject>();

    public float RemainingTime => remainingTime;
    public bool IsRunning => isRunning;
    /// <summary>採用キャラの合計攻撃力。各ジャンルのスコア/ダメージ計算の基礎値として使う。</summary>
    public float TotalAttackPower => totalAttackPower;
    /// <summary>このミニゲームの最終スコア（GetFinalScoreの公開ラッパー）。エンディングで全ジャンルの
    /// スコア内訳を集計する際、GameFlowManagerから読み取れるようにするために公開している。</summary>
    public float FinalScore => GetFinalScore();

    protected virtual void Awake()
    {
      // 自分の準備完了ボタンは、実際にこのミニゲームが始まるまで隠しておく。
      if (closeRuleButton != null) closeRuleButton.gameObject.SetActive(false);
      if (readyButton != null) readyButton.gameObject.SetActive(false);
      ruleImageRoot?.SetActive(false);
      if (timerText != null) timerText.gameObject.SetActive(false);
      SetResultActive(false);
    }

    /// <summary>採用済みキャラのうち、このジャンルで使用するキャラを設定する。</summary>
    public void AssignCharacters(IEnumerable<CharacterData> characters)
    {
      assignedCharacters = characters.ToList();
      totalAttackPower = assignedCharacters.Sum(c => c.attackPower);
      UpdateAttackPowerText();
    }

    /// <summary>複数採用時にランダム1体を使うジャンル用のヘルパー。</summary>
    protected CharacterData PickRandomAssigned()
    {
      if (assignedCharacters.Count == 0) return null;
      return assignedCharacters[UnityEngine.Random.Range(0, assignedCharacters.Count)];
    }

    /// <summary>
    /// 飛行・俊足・耐寒のように「メインで動かす1体」を<see cref="PickRandomAssigned"/>等で別枠にしている
    /// ジャンル向けのヘルパー。それ以外の採用キャラを、指定した原点から軸方向へ間隔を空けて並べて召喚する。
    /// 採用試験の整列演出（<see cref="LineupAxisExtensions"/>）と同じ軸指定方式にしている。
    /// 呼び出し側は戻り値のインスタンス一覧を保持しておき、ミニゲーム終了時に破棄すること。
    /// </summary>
    protected List<GameObject> SpawnRemainingLineup(CharacterData mainCharacter, Transform lineupOrigin, LineupAxis lineupAxis, float lineupSpacing)
    {
      var spawned = new List<GameObject>();
      if (lineupOrigin == null) return spawned;

      var direction = lineupAxis.ToDirection();
      var index = 0;
      foreach (var character in assignedCharacters)
      {
        if (character == null || character == mainCharacter || character.characterPrefab == null) continue;

        var position = lineupOrigin.position + direction * (lineupSpacing * index);
        var instance = Instantiate(character.characterPrefab, position, lineupOrigin.rotation);
        spawned.Add(instance);
        index++;
      }
      return spawned;
    }

    /// <summary><see cref="SpawnRemainingLineup"/>で召喚したインスタンス一覧をまとめて破棄する。</summary>
    protected void DespawnLineup(List<GameObject> instances)
    {
      if (instances == null) return;
      foreach (var instance in instances)
      {
        if (instance != null) Destroy(instance);
      }
      instances.Clear();
    }

    /// <summary>飛行・俊足・耐寒のように「メインで操作する1体」をPickRandomAssigned等で選び、
    /// SpawnRemainingLineupで残りを整列させる方式のジャンルはtrueを返す。trueの場合、
    /// characterSpawnPointsへの一括召喚(SummonCharacters)を行わない（両方動くと操作キャラが二重に
    /// 召喚されてしまうため、召喚方式はどちらか一方に統一する）。</summary>
    protected virtual bool SkipGenericCharacterSummon => false;

    public IEnumerator RunAsync()
    {
      WarpPlayerToTarget();
      if (!SkipGenericCharacterSummon) SummonCharacters();
      UpdateAttackPowerText();

      yield return ShowRulesAndWaitReady();

      isRunning = true;
      result = MinigameResult.None;
      remainingTime = timeLimitOverride > 0f ? timeLimitOverride : (settings != null ? settings.defaultMinigameTimeLimit : 60f);

      if (timerText != null) timerText.gameObject.SetActive(true);
      UpdateTimerText();

      SubscribeGestures();
      OnMinigameStart();

      while (isRunning && remainingTime > 0f && result == MinigameResult.None)
      {
        remainingTime -= Time.deltaTime;
        UpdateTimerText();
        OnMinigameTick(Time.deltaTime);
        yield return null;
      }

      if (result == MinigameResult.None)
      {
        result = MinigameResult.DefenseSuccess; // 制限時間耐えたら防衛成功
      }

      isRunning = false;
      UnsubscribeGestures();
      if (timerText != null) timerText.gameObject.SetActive(false);
      OnMinigameEnd(result);
      yield return ShowResult(result);
      if (!SkipGenericCharacterSummon) DespawnCharacters();
      RestorePlayerRotation();

      OnMinigameFinished?.Invoke(result);
    }

    /// <summary>ジャンルごとの「1回分の操作」が成立した瞬間に呼ぶ、motionSfxの再生ヘルパー。
    /// 派生クラス側は、ジェスチャー成功／ポインター操作成功などの箇所でこれを呼ぶだけでよい。</summary>
    protected void PlayMotionSfx()
    {
      if (motionSfx == null) return;
      if (audioSource == null)
      {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
      }
      audioSource.PlayOneShot(motionSfx);
    }

    /// <summary>終了直後、GetFinalScoreの値を数秒間表示してから次のフェーズへ進む。
    /// キャラの後片付け(DespawnCharacters)より前に呼ぶことで、結果を見せている間はまだ
    /// ステージ上にキャラが残っている状態にする。</summary>
    private IEnumerator ShowResult(MinigameResult finalResult)
    {
      if (resultRoot == null && resultScoreText == null) yield break;

      if (resultScoreText != null)
      {
        var label = finalResult == MinigameResult.DefenseSuccess ? "防衛成功" : "ゲームオーバー";
        resultScoreText.text = $"{label}\nスコア: {GetFinalScore():0}";
      }
      SetResultActive(true);

      yield return new WaitForSeconds(settings != null ? settings.resultDisplaySeconds : 3f);

      SetResultActive(false);
    }

    private void SetResultActive(bool active)
    {
      if (resultRoot != null) resultRoot.SetActive(active);
      else if (resultScoreText != null) resultScoreText.gameObject.SetActive(active);
    }

    /// <summary>このミニゲームの最終スコア。派生クラスが自分のスコア/ダメージ/マグマ量等のプロパティを
    /// 返すようオーバーライドする。既定は0（スコア概念のないジャンルの保険）。</summary>
    protected virtual float GetFinalScore() => 0f;

    private void UpdateAttackPowerText()
    {
      if (attackPowerText != null) attackPowerText.text = $"編成攻撃力: {totalAttackPower:0}";
    }

    private void UpdateTimerText()
    {
      if (timerText != null) timerText.text = Mathf.CeilToInt(Mathf.Max(remainingTime, 0f)).ToString();
    }

    /// <summary>採用キャラを<see cref="characterSpawnPoints"/>に1体ずつ召喚する。</summary>
    private void SummonCharacters()
    {
      DespawnCharacters(); // 前回このミニゲームを実行した際の召喚が残っていれば片付ける（ゲームループは繰り返すため）

      if (characterSpawnPoints == null || characterSpawnPoints.Length == 0) return;

      for (var i = 0; i < assignedCharacters.Count; i++)
      {
        var character = assignedCharacters[i];
        if (character == null || character.characterPrefab == null) continue;

        var spawnPoint = characterSpawnPoints[Mathf.Min(i, characterSpawnPoints.Length - 1)];
        if (spawnPoint == null) continue;

        var instance = Instantiate(character.characterPrefab, spawnPoint.position, spawnPoint.rotation);
        _summonedInstances.Add(instance);
      }
    }

    private void DespawnCharacters()
    {
      foreach (var instance in _summonedInstances)
      {
        if (instance != null) Destroy(instance);
      }
      _summonedInstances.Clear();
    }

    private Quaternion _originalPlayerRotation;
    private bool _rotationOverridden;

    /// <summary>プレイヤー本体をそのミニゲームの場所へ瞬間移動させる。既定では位置だけを変更し、向きは
    /// 変えない。applyWarpRotationがtrueの場合はwarpTargetの向きにも合わせる（元の向きは覚えておき、
    /// ミニゲーム終了時にRestorePlayerRotationで戻す）。</summary>
    private void WarpPlayerToTarget()
    {
      if (playerRoot == null || warpTarget == null) return;
      playerRoot.position = warpTarget.position;

      if (applyWarpRotation)
      {
        _originalPlayerRotation = playerRoot.rotation;
        playerRoot.rotation = warpTarget.rotation;
        _rotationOverridden = true;
      }
    }

    /// <summary>WarpPlayerToTargetでapplyWarpRotationにより変更した向きを元へ戻す。</summary>
    private void RestorePlayerRotation()
    {
      if (!_rotationOverridden || playerRoot == null) return;
      playerRoot.rotation = _originalPlayerRotation;
      _rotationOverridden = false;
    }

    private IEnumerator ShowRulesAndWaitReady()
    {
      // ①ルール説明を表示し、閉じる用ゲージに合わせるまで待つ（この間はまだ練習操作できない）。
      ruleImageRoot?.SetActive(true);

      if (closeRuleButton != null)
      {
        closeRuleButton.HoldSeconds = settings != null ? settings.readyHoldSeconds : 3f;
        closeRuleButton.gameObject.SetActive(true);
        var closed = false;
        Action onClosed = () => closed = true;
        closeRuleButton.OnTriggered += onClosed;
        yield return new WaitUntil(() => closed);
        closeRuleButton.OnTriggered -= onClosed;
        closeRuleButton.gameObject.SetActive(false);
      }

      // ②ルール説明を閉じたら練習操作を有効にする。
      ruleImageRoot?.SetActive(false);
      SubscribePracticeGestures();
      OnRulesShown();

      // ③開始用ゲージに合わせるまで練習できる。合わせたら練習を終了して本番へ。
      if (readyButton != null)
      {
        readyButton.HoldSeconds = settings != null ? settings.readyHoldSeconds : 3f;
        readyButton.gameObject.SetActive(true);
        var ready = false;
        Action onReady = () => ready = true;
        readyButton.OnTriggered += onReady;
        yield return new WaitUntil(() => ready);
        readyButton.OnTriggered -= onReady;
        readyButton.gameObject.SetActive(false);
      }

      OnRulesHidden();
      UnsubscribePracticeGestures();
    }

    private void SubscribeGestures()
    {
      if (gestureRecognizer != null) gestureRecognizer.OnGestureDetected += HandleGesture;
    }

    private void UnsubscribeGestures()
    {
      if (gestureRecognizer != null) gestureRecognizer.OnGestureDetected -= HandleGesture;
    }

    private void SubscribePracticeGestures()
    {
      if (gestureRecognizer != null) gestureRecognizer.OnGestureDetected += HandlePracticeGesture;
    }

    private void UnsubscribePracticeGestures()
    {
      if (gestureRecognizer != null) gestureRecognizer.OnGestureDetected -= HandlePracticeGesture;
    }

    private void HandleGesture(GestureType type)
    {
      if (isRunning) OnGestureForMinigame(type);
    }

    private void HandlePracticeGesture(GestureType type)
    {
      // isRunning（本番中）はHandleGesture側で処理するので、練習フックはルール説明中だけ働かせる。
      if (!isRunning) OnPracticeGesture(type);
    }

    protected void FinishAsGameOver() => result = MinigameResult.GameOver;
    protected void FinishAsDefenseSuccess() => result = MinigameResult.DefenseSuccess;

    /// <summary>ミニゲーム開始直後（準備完了ゲージ達成後）に呼ばれる。</summary>
    protected virtual void OnMinigameStart() { }

    /// <summary>毎フレーム呼ばれる（制限時間カウント中のみ）。</summary>
    protected virtual void OnMinigameTick(float deltaTime) { }

    /// <summary>ミニゲーム終了時に呼ばれる（防衛成功／ゲームオーバーいずれの場合も）。</summary>
    protected virtual void OnMinigameEnd(MinigameResult finalResult) { }

    /// <summary>このミニゲームが購読するジェスチャーが検出された際に呼ばれる。</summary>
    protected abstract void OnGestureForMinigame(GestureType type);

    /// <summary>ルール説明中（準備完了ゲージに合わせる前）のジェスチャー練習用フック。
    /// スコアやゲージ等の実際のゲーム状態には影響を与えず、動作確認用の見た目のフィードバックだけを
    /// 返したい場合にオーバーライドする。既定では何もしない（練習フィードバックが不要なジャンルはそのままでよい）。</summary>
    protected virtual void OnPracticeGesture(GestureType type) { }

    /// <summary>ルール説明画像を表示した瞬間（練習開始）に呼ばれる。継続的な物理演算等、
    /// OnPracticeGestureだけでは足りない練習用の初期化が必要なジャンル（飛行等）で使う。</summary>
    protected virtual void OnRulesShown() { }

    /// <summary>準備完了で確定し、ルール説明画像を消す瞬間（練習終了）に呼ばれる。</summary>
    protected virtual void OnRulesHidden() { }
  }
}
