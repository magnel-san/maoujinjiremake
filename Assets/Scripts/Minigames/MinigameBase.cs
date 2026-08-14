using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using DemonLordHR.UI;
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
  /// ルール説明→「準備完了」ゲージ→制限時間カウント→終了判定、という流れをテンプレートメソッドで共通化し、
  /// 各ジャンル固有のロジック（購読するジェスチャー、スコア/ダメージ計算、勝敗条件）は派生クラスで実装する。
  /// </summary>
  public abstract class MinigameBase : MonoBehaviour
  {
    [SerializeField] protected GameSettings settings;
    [SerializeField] protected GestureRecognizer gestureRecognizer;
    [SerializeField] protected CircularHoldButton readyButton;
    [SerializeField] protected float timeLimitOverride = -1f;

    public event Action<MinigameResult> OnMinigameFinished;

    protected List<CharacterData> assignedCharacters = new List<CharacterData>();
    protected float totalAttackPower;
    protected float remainingTime;
    protected bool isRunning;
    protected MinigameResult result = MinigameResult.None;

    public float RemainingTime => remainingTime;
    public bool IsRunning => isRunning;

    /// <summary>採用済みキャラのうち、このジャンルで使用するキャラを設定する。</summary>
    public void AssignCharacters(IEnumerable<CharacterData> characters)
    {
      assignedCharacters = characters.ToList();
      totalAttackPower = assignedCharacters.Sum(c => c.attackPower);
    }

    /// <summary>複数採用時にランダム1体を使うジャンル用のヘルパー。</summary>
    protected CharacterData PickRandomAssigned()
    {
      if (assignedCharacters.Count == 0) return null;
      return assignedCharacters[UnityEngine.Random.Range(0, assignedCharacters.Count)];
    }

    public IEnumerator RunAsync()
    {
      yield return ShowRulesAndWaitReady();

      isRunning = true;
      result = MinigameResult.None;
      remainingTime = timeLimitOverride > 0f ? timeLimitOverride : (settings != null ? settings.defaultMinigameTimeLimit : 60f);

      SubscribeGestures();
      OnMinigameStart();

      while (isRunning && remainingTime > 0f && result == MinigameResult.None)
      {
        remainingTime -= Time.deltaTime;
        OnMinigameTick(Time.deltaTime);
        yield return null;
      }

      if (result == MinigameResult.None)
      {
        result = MinigameResult.DefenseSuccess; // 制限時間耐えたら防衛成功
      }

      isRunning = false;
      UnsubscribeGestures();
      OnMinigameEnd(result);

      OnMinigameFinished?.Invoke(result);
    }

    private IEnumerator ShowRulesAndWaitReady()
    {
      // TODO: ルール説明画像UIを表示する
      if (readyButton != null)
      {
        readyButton.HoldSeconds = settings != null ? settings.readyHoldSeconds : 3f;
        var ready = false;
        Action onReady = () => ready = true;
        readyButton.OnTriggered += onReady;
        yield return new WaitUntil(() => ready);
        readyButton.OnTriggered -= onReady;
      }
    }

    private void SubscribeGestures()
    {
      if (gestureRecognizer != null) gestureRecognizer.OnGestureDetected += HandleGesture;
    }

    private void UnsubscribeGestures()
    {
      if (gestureRecognizer != null) gestureRecognizer.OnGestureDetected -= HandleGesture;
    }

    private void HandleGesture(GestureType type)
    {
      if (isRunning) OnGestureForMinigame(type);
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
  }
}
