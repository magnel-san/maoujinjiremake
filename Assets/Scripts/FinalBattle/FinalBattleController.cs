using System;
using System.Collections;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.FinalBattle
{
  /// <summary>
  /// 最終決戦フェーズ（仕様書5章）：勇者のHPゲージを「両拳を交互に突き出す」連打で削り切る。
  /// 勇者の見た目変化・フェード演出の実際の描画はTODO、HP管理と終了通知のみ実装する。
  /// </summary>
  public class FinalBattleController : MonoBehaviour
  {
    [SerializeField] private GameSettings _settings;
    [SerializeField] private GestureRecognizer _gestureRecognizer;

    public float MaxHp { get; private set; }
    public float CurrentHp { get; private set; }
    public bool IsRunning { get; private set; }

    public event Action OnHeroDefeated;

    /// <summary>ミニゲームフェーズの結果（防衛成功数など）を渡し、勇者の見た目・状態を決める。</summary>
    public void SetHeroConditionFromMinigameResults(int defenseSuccessCount, int gameOverCount)
    {
      // TODO: defenseSuccessCount / gameOverCount の比率で勇者の見た目（ボロボロ／元気）を変更する
    }

    public IEnumerator RunAsync()
    {
      MaxHp = _settings != null ? _settings.finalBattleMaxHp : 1000f;
      CurrentHp = MaxHp;
      IsRunning = true;

      if (_gestureRecognizer != null) _gestureRecognizer.OnGestureDetected += HandleGesture;

      // TODO: 「連打しろ」UI表示
      yield return new WaitUntil(() => CurrentHp <= 0f);

      if (_gestureRecognizer != null) _gestureRecognizer.OnGestureDetected -= HandleGesture;
      IsRunning = false;

      OnHeroDefeated?.Invoke();

      yield return FadeToWhite();
    }

    private void HandleGesture(GestureType type)
    {
      if (!IsRunning || type != GestureType.AlternatingPunch) return;

      var damage = _settings != null ? _settings.finalBattlePunchDamage : 20f;
      CurrentHp = Mathf.Max(0f, CurrentHp - damage);
    }

    private IEnumerator FadeToWhite()
    {
      var duration = _settings != null ? _settings.finalBattleFadeOutSeconds : 2f;
      // TODO: 画面フェードアウト（真っ白）の実際の描画（CanvasGroup等）をここで制御する
      yield return new WaitForSeconds(duration);
    }
  }
}
