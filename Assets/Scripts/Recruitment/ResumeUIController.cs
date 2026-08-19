using System;
using System.Collections;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using DemonLordHR.UI;
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
  /// 仕様書3.2〜3.4に対応。3D履歴書の丸まり・投げる演出は、専用アセットが無くても
  /// 進行を確認できるよう、拡大縮小・移動による手続き的な演出で仮実装している。
  /// </summary>
  public class ResumeUIController : MonoBehaviour
  {
    [SerializeField] private GestureRecognizer _gestureRecognizer;
    [SerializeField] private GameSettings _settings;
    [Tooltip("採用のハンコを押す間、右手を「ハンコ持ち手」モデルに差し替えるために使う")]
    [SerializeField] private HandTrackingController _handTrackingController;

    [Header("2D履歴書（採用/不採用決定前）")]
    [SerializeField] private GameObject _resumeImageRoot;
    [SerializeField] private Image _resumeImage;
    [Tooltip("採用のハンコを押した際に表示するスタンプ画像のルート")]
    [SerializeField] private GameObject _stampImageRoot;

    [Header("3D履歴書（不採用時のみ）")]
    [SerializeField] private GameObject _resume3DRoot;
    [Tooltip("丸まりきった状態のスケール（1が等倍）")]
    [SerializeField] private float _crumpledScale = 0.35f;
    [Tooltip("丸め1段階ごとに追加で回転させる角度")]
    [SerializeField] private float _crumpleRotationPerStep = 20f;
    [Tooltip("投げる演出で移動させる距離・方向（ローカル）")]
    [SerializeField] private Vector3 _throwLocalOffset = new Vector3(0f, 0.5f, 2f);
    [Tooltip("投げる演出の所要時間")]
    [SerializeField] private float _throwDuration = 0.4f;

    [Header("戻るボタン")]
    [Tooltip("履歴書表示中、決定前に閉じるための円状ボタン")]
    [SerializeField] private CircularHoldButton _backButton;

    private CharacterData _currentCharacter;
    private int _currentPage;
    private int _crumpleStep;
    private bool _isOpen;
    private ResumeDecision _decision;

    public event Action<CharacterData> OnHireIntent;
    public event Action<CharacterData> OnRejectIntent;
    public event Action<CharacterData> OnStamped;
    public event Action<CharacterData> OnThrown;

    private void Awake()
    {
      // 履歴書を開くまでは何も表示しない。
      _resumeImageRoot?.SetActive(false);
      _resume3DRoot?.SetActive(false);
      _stampImageRoot?.SetActive(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
      if (_gestureRecognizer != null)
      {
        _gestureRecognizer.OnGestureDetected += HandleGesture;
      }
      if (_backButton != null)
      {
        _backButton.HoldSeconds = _settings != null ? _settings.resumeBackHoldSeconds : 3f;
        _backButton.OnTriggered += HandleBackRequested;
      }
    }

    private void OnDisable()
    {
      if (_gestureRecognizer != null)
      {
        _gestureRecognizer.OnGestureDetected -= HandleGesture;
      }
      if (_backButton != null)
      {
        _backButton.OnTriggered -= HandleBackRequested;
      }
    }

    public void Open(CharacterData character)
    {
      // 既に同じキャラの決定作業（丸め途中など）が進んでいる場合、再オープンで状態を壊さない。
      if (_isOpen && _currentCharacter == character && _decision != ResumeDecision.None) return;

      _currentCharacter = character;
      _currentPage = 0;
      _crumpleStep = 0;
      _decision = ResumeDecision.None;
      _isOpen = true;

      _resume3DRoot?.SetActive(false);
      if (_resume3DRoot != null)
      {
        _resume3DRoot.transform.localScale = Vector3.one;
        _resume3DRoot.transform.localRotation = Quaternion.identity;
      }
      _stampImageRoot?.SetActive(false);
      _resumeImageRoot?.SetActive(true);
      if (_backButton != null) _backButton.gameObject.SetActive(true);
      _handTrackingController?.SetRightHandStampMode(false);
      ApplyPageSprite();
    }

    public void Close()
    {
      _isOpen = false;
      _resumeImageRoot?.SetActive(false);
      _resume3DRoot?.SetActive(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);
      _handTrackingController?.SetRightHandStampMode(false);
      _currentCharacter = null;
    }

    private void HandleBackRequested()
    {
      if (!_isOpen || _decision != ResumeDecision.None) return;
      Close();
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
      ApplyCrumpleVisual();
      OnRejectIntent?.Invoke(_currentCharacter);
    }

    private void AdvanceCrumple()
    {
      var maxSteps = _settings != null ? _settings.resumeCrumpleSteps : 4;
      if (_crumpleStep >= maxSteps) return;
      _crumpleStep++;
      ApplyCrumpleVisual();
      // _crumpleStepがmaxStepsに達すると IsFullyCrumpled() がtrueになり、
      // 次のRightFistPunchOutで「投げろ」側の分岐（ThrowResumeAndClose）に入る。
    }

    /// <summary>専用アセットが無くても進捗が分かるよう、拡大縮小と回転で「丸まっていく」様子を表現する。</summary>
    private void ApplyCrumpleVisual()
    {
      if (_resume3DRoot == null) return;

      var maxSteps = _settings != null ? _settings.resumeCrumpleSteps : 4;
      var ratio = maxSteps <= 0 ? 0f : (float)_crumpleStep / maxSteps;
      _resume3DRoot.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * _crumpledScale, ratio);
      _resume3DRoot.transform.localRotation = Quaternion.Euler(0f, 0f, _crumpleRotationPerStep * _crumpleStep);
    }

    private bool IsFullyCrumpled()
    {
      var maxSteps = _settings != null ? _settings.resumeCrumpleSteps : 4;
      return _crumpleStep >= maxSteps;
    }

    private IEnumerator ThrowResumeAndClose()
    {
      OnThrown?.Invoke(_currentCharacter);

      if (_resume3DRoot != null)
      {
        var start = _resume3DRoot.transform.localPosition;
        var destination = start + _throwLocalOffset;
        var elapsed = 0f;
        var duration = Mathf.Max(_throwDuration, 0.01f);

        while (elapsed < duration)
        {
          elapsed += Time.deltaTime;
          _resume3DRoot.transform.localPosition = Vector3.Lerp(start, destination, elapsed / duration);
          yield return null;
        }

        _resume3DRoot.transform.localPosition = start;
      }

      Close();
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
            _handTrackingController?.SetRightHandStampMode(true);
            // ハイライトはRecruitmentPhaseController側で行う。
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
            _stampImageRoot?.SetActive(true);
            OnStamped?.Invoke(_currentCharacter);
          }
          else if (_decision == ResumeDecision.Rejected && IsFullyCrumpled())
          {
            StartCoroutine(ThrowResumeAndClose());
          }
          break;
      }
    }
  }
}
