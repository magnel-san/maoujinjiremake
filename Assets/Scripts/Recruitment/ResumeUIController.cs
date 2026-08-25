using System;
using System.Collections;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using DemonLordHR.UI;
using TMPro;
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
  /// 履歴書表示・ページ送り・採用/不採用の意思表示を制御する。
  ///
  /// 採用/不採用/ページ送りは、<see cref="ResumePoseRecognizer"/>が判定する3つの静止ポーズで行う
  /// （腕クロス＝不採用、両手を挙げて輪＝採用、両手で輪っかを顔の近くに＝裏を見る）。
  /// 速度や軌跡など「動き」に一切頼らない判定なので、腕を振っただけで誤って発火する、といった
  /// 動きベースの誤検出が構造的に起こらない。同じポーズを2秒間保持し続けて初めて確定する。
  ///
  /// 決定後の取り消しは用意していない（「魔王の決定は覆らない」という演出として割り切っている）。
  /// </summary>
  public class ResumeUIController : MonoBehaviour
  {
    [SerializeField] private ResumePoseRecognizer _resumePoseRecognizer;
    [SerializeField] private GameSettings _settings;
    [Tooltip("採用ジェスチャー時の資金不足チェックに使う（未設定の場合はチェックをスキップする）")]
    [SerializeField] private RecruitmentPhaseController _recruitmentController;

    [Header("2D履歴書")]
    [SerializeField] private GameObject _resumeImageRoot;
    [SerializeField] private Image _resumeImage;
    [Tooltip("採用の丸を描いた際に表示するスタンプ画像のルート")]
    [SerializeField] private GameObject _stampImageRoot;

    [Header("戻るボタン")]
    [Tooltip("履歴書表示中、決定前に閉じるための円状ボタン")]
    [SerializeField] private CircularHoldButton _backButton;

    [Header("資金不足エラー表示")]
    [Tooltip("資金が足りず採用できなかった時に表示するUIのルート。未設定でも_insufficientFundsText自体の" +
      "表示/非表示で代用する。両方未設定ならエラー表示自体を行わない（採用も従来通り不成立のまま）。")]
    [SerializeField] private GameObject _insufficientFundsRoot;
    [Tooltip("エラーメッセージを書き込むテキスト。")]
    [SerializeField] private TMP_Text _insufficientFundsText;
    [Tooltip("表示する文言。")]
    [SerializeField] private string _insufficientFundsMessage = "お金が足りない";
    [Tooltip("エラー表示を自動的に消すまでの秒数。")]
    [SerializeField] private float _insufficientFundsDisplaySeconds = 2f;

    private CharacterData _currentCharacter;
    private int _currentPage;
    private bool _isOpen;
    private ResumeDecision _decision;
    private Coroutine _autoCloseCoroutine;
    private Coroutine _insufficientFundsCoroutine;

    public event Action<CharacterData> OnHireIntent;
    public event Action<CharacterData> OnRejectIntent;
    /// <summary>採用（丸）確定時に発火。整列演出のトリガーに使う。</summary>
    public event Action<CharacterData> OnStamped;
    /// <summary>不採用（罰）確定時に発火。吹き飛び演出のトリガーに使う。</summary>
    public event Action<CharacterData> OnThrown;
    /// <summary>履歴書を開いた時に発火。開いている間は他の候補の履歴書トリガーや
    /// 「面接終了する」ボタンを非表示にするために使う（複数の履歴書を同時に触れると状態が壊れるため）。</summary>
    public event Action<CharacterData> OnOpened;
    /// <summary>履歴書を閉じた時に発火。</summary>
    public event Action OnClosed;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
      // 履歴書を開くまでは何も表示しない。
      _resumeImageRoot?.SetActive(false);
      _stampImageRoot?.SetActive(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);
      SetInsufficientFundsActive(false);
    }

    private void OnEnable()
    {
      if (_resumePoseRecognizer != null)
      {
        _resumePoseRecognizer.OnPoseConfirmed += HandlePoseConfirmed;
      }
      if (_backButton != null)
      {
        _backButton.HoldSeconds = _settings != null ? _settings.resumeBackHoldSeconds : 3f;
        _backButton.OnTriggered += HandleBackRequested;
      }
    }

    private void OnDisable()
    {
      if (_resumePoseRecognizer != null)
      {
        _resumePoseRecognizer.OnPoseConfirmed -= HandlePoseConfirmed;
      }
      if (_backButton != null) _backButton.OnTriggered -= HandleBackRequested;
    }

    public void Open(CharacterData character)
    {
      // 既に同じキャラの決定作業が進んでいる場合、再オープンで状態を壊さない。
      if (_isOpen && _currentCharacter == character && _decision != ResumeDecision.None) return;

      if (_autoCloseCoroutine != null)
      {
        StopCoroutine(_autoCloseCoroutine);
        _autoCloseCoroutine = null;
      }

      _currentCharacter = character;
      _currentPage = 0;
      _decision = ResumeDecision.None;
      _isOpen = true;

      _stampImageRoot?.SetActive(false);
      _resumeImageRoot?.SetActive(true);
      if (_backButton != null) _backButton.gameObject.SetActive(true);
      _resumePoseRecognizer?.SetCapturing(true);
      ApplyPageSprite();

      OnOpened?.Invoke(character);
    }

    public void Close()
    {
      if (_autoCloseCoroutine != null)
      {
        StopCoroutine(_autoCloseCoroutine);
        _autoCloseCoroutine = null;
      }

      if (_insufficientFundsCoroutine != null)
      {
        StopCoroutine(_insufficientFundsCoroutine);
        _insufficientFundsCoroutine = null;
      }

      _isOpen = false;
      _resumeImageRoot?.SetActive(false);
      _stampImageRoot?.SetActive(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);
      SetInsufficientFundsActive(false);
      _resumePoseRecognizer?.SetCapturing(false);
      _currentCharacter = null;

      OnClosed?.Invoke();
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

    private void HandlePoseConfirmed(ResumePose pose)
    {
      if (!_isOpen || _currentCharacter == null || _decision != ResumeDecision.None) return;

      switch (pose)
      {
        // 両手で輪っか、顔の近く＝履歴書の裏を見る（ページ送り）
        case ResumePose.FlipPage:
          TurnPage();
          break;

        // 両手を挙げて輪＝採用
        case ResumePose.Hire:
          ConfirmHire();
          break;

        // 腕クロス＝不採用
        case ResumePose.Reject:
          ConfirmReject();
          break;
      }
    }

    private void ConfirmHire()
    {
      if (_recruitmentController != null && !_recruitmentController.CanAfford(_currentCharacter))
      {
        ShowInsufficientFunds();
        return; // 資金不足のため不成立。履歴書は開いたままにし、プレイヤーがそのまま見送り(不採用)を選べるようにする。
      }

      _decision = ResumeDecision.Hired;
      _resumePoseRecognizer?.SetCapturing(false);
      _stampImageRoot?.SetActive(true);

      OnHireIntent?.Invoke(_currentCharacter);
      OnStamped?.Invoke(_currentCharacter);

      // 「履歴書は表示したまま」にしつつ、少し見せたら自動的に次の候補へ戻れるようにする。
      var delay = _settings != null ? _settings.hireStampDisplaySeconds : 1.5f;
      _autoCloseCoroutine = StartCoroutine(AutoCloseAfter(delay));
    }

    private void ConfirmReject()
    {
      _decision = ResumeDecision.Rejected;
      _resumePoseRecognizer?.SetCapturing(false);
      _resumeImageRoot?.SetActive(false);

      OnRejectIntent?.Invoke(_currentCharacter);
      OnThrown?.Invoke(_currentCharacter);
      Close();
    }

    private IEnumerator AutoCloseAfter(float seconds)
    {
      yield return new WaitForSeconds(Mathf.Max(seconds, 0f));
      _autoCloseCoroutine = null;
      Close();
    }

    /// <summary>資金不足で採用が成立しなかった際、一定時間だけエラーメッセージを表示する。</summary>
    private void ShowInsufficientFunds()
    {
      if (_insufficientFundsText != null) _insufficientFundsText.text = _insufficientFundsMessage;
      SetInsufficientFundsActive(true);

      if (_insufficientFundsCoroutine != null) StopCoroutine(_insufficientFundsCoroutine);
      _insufficientFundsCoroutine = StartCoroutine(HideInsufficientFundsAfter(_insufficientFundsDisplaySeconds));
    }

    private IEnumerator HideInsufficientFundsAfter(float seconds)
    {
      yield return new WaitForSeconds(Mathf.Max(seconds, 0f));
      _insufficientFundsCoroutine = null;
      SetInsufficientFundsActive(false);
    }

    private void SetInsufficientFundsActive(bool active)
    {
      if (_insufficientFundsRoot != null) _insufficientFundsRoot.SetActive(active);
      else if (_insufficientFundsText != null) _insufficientFundsText.gameObject.SetActive(active);
    }
  }
}
