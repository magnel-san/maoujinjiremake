using System;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.Recruitment
{
  public enum ResumeDecision
  {
    None,
    Hired,
    Rejected,
  }

  /// <summary>
  /// 履歴書表示・ページ送り・採用/不採用の意思表示・丸めアニメーション制御。
  /// 仕様書3.2〜3.4に対応。実際の3D丸めアニメーションの再生やスタンプ画像の演出は
  /// TODOとして拡張ポイントのみ用意する（アセット未整備のため）。
  /// </summary>
  public class ResumeUIController : MonoBehaviour
  {
    [SerializeField] private GestureRecognizer _gestureRecognizer;
    [SerializeField] private GameSettings _settings;

    [Header("2D履歴書（採用/不採用決定前）")]
    [SerializeField] private GameObject _resumeImageRoot;
    [SerializeField] private Image _resumeImage;

    [Header("3D履歴書（不採用時のみ）")]
    [SerializeField] private GameObject _resume3DRoot;

    private CharacterData _currentCharacter;
    private int _currentPage;
    private int _crumpleStep;
    private bool _isOpen;
    private ResumeDecision _decision;

    public event Action<CharacterData> OnHireIntent;
    public event Action<CharacterData> OnRejectIntent;
    public event Action<CharacterData> OnStamped;
    public event Action<CharacterData> OnThrown;

    private void OnEnable()
    {
      if (_gestureRecognizer != null)
      {
        _gestureRecognizer.OnGestureDetected += HandleGesture;
      }
    }

    private void OnDisable()
    {
      if (_gestureRecognizer != null)
      {
        _gestureRecognizer.OnGestureDetected -= HandleGesture;
      }
    }

    public void Open(CharacterData character)
    {
      _currentCharacter = character;
      _currentPage = 0;
      _crumpleStep = 0;
      _decision = ResumeDecision.None;
      _isOpen = true;

      _resume3DRoot?.SetActive(false);
      _resumeImageRoot?.SetActive(true);
      ApplyPageSprite();
    }

    public void Close()
    {
      _isOpen = false;
      _resumeImageRoot?.SetActive(false);
      _resume3DRoot?.SetActive(false);
      _currentCharacter = null;
    }

    private void ApplyPageSprite()
    {
      if (_resumeImage == null || _currentCharacter == null || _currentCharacter.resumePages == null) return;
      if (_currentPage < _currentCharacter.resumePages.Length)
      {
        _resumeImage.sprite = _currentCharacter.resumePages[_currentPage];
      }
    }

    private void TurnPage()
    {
      if (_currentCharacter?.resumePages == null || _currentCharacter.resumePages.Length <= 1) return;
      _currentPage = (_currentPage + 1) % _currentCharacter.resumePages.Length;
      ApplyPageSprite();
    }

    private void BeginReject()
    {
      _decision = ResumeDecision.Rejected;
      _resumeImageRoot?.SetActive(false);
      _resume3DRoot?.SetActive(true);
      _crumpleStep = 0;
      OnRejectIntent?.Invoke(_currentCharacter);
      // TODO: 3D履歴書の丸まりアニメーションを初期状態(0段階目)にセットする
    }

    private void AdvanceCrumple()
    {
      var maxSteps = _settings != null ? _settings.resumeCrumpleSteps : 4;
      if (_crumpleStep >= maxSteps) return;
      _crumpleStep++;
      // TODO: 3D履歴書の丸まりアニメーションをコマ送りする（_crumpleStep段階目を再生）
      if (_crumpleStep >= maxSteps)
      {
        // UIが「丸めろ」→「投げろ」に変化する通知はここで発火するUIイベントに委譲する
      }
    }

    private bool IsFullyCrumpled()
    {
      var maxSteps = _settings != null ? _settings.resumeCrumpleSteps : 4;
      return _crumpleStep >= maxSteps;
    }

    private void HandleGesture(GestureType type)
    {
      if (!_isOpen || _currentCharacter == null) return;

      switch (type)
      {
        case GestureType.HoopBothHands:
          if (_decision == ResumeDecision.None) TurnPage();
          break;

        case GestureType.BigCircleOverhead:
          if (_decision == ResumeDecision.None)
          {
            _decision = ResumeDecision.Hired;
            OnHireIntent?.Invoke(_currentCharacter);
            // TODO: 対象キャラを settings.hireHighlightColor でハイライトし、
            // 魔王の右手を「ハンコ持ち手」モデルに差し替える（右手指トラッキングOFF）
          }
          break;

        case GestureType.ArmsCross:
          if (_decision == ResumeDecision.None)
          {
            BeginReject();
          }
          break;

        case GestureType.ClapNarrow:
          if (_decision == ResumeDecision.Rejected && !IsFullyCrumpled())
          {
            AdvanceCrumple();
          }
          break;

        case GestureType.RightFistPunchOut:
          if (_decision == ResumeDecision.Hired)
          {
            OnStamped?.Invoke(_currentCharacter);
            // TODO: 履歴書画像の上に「採用」スタンプ画像を表示
          }
          else if (_decision == ResumeDecision.Rejected && IsFullyCrumpled())
          {
            OnThrown?.Invoke(_currentCharacter);
            // TODO: 丸まった3D履歴書を投げる演出
          }
          break;
      }
    }
  }
}
