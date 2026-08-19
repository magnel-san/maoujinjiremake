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
  /// 履歴書表示・ページ送り・採用/不採用の意思表示を制御する。
  ///
  /// 採用/不採用の決定は「ポインター保持ボタン」で行う（ジェスチャーだけに頼ると、
  /// 誤検出・無反応が起きやすく、取り消しの効かない重要な決定には不向きなため）。
  /// ボタンでの決定はあくまで「意思決定」で、その後に続くジェスチャー
  /// （ハンコを押す＝右手を上から下に振り下ろす／殴る＝右手を前に突き出す）は「確定の演出」という役割分担にしている。
  /// ポインターと確定ジェスチャーが両方とも右手を使うため、ボタンを指し続けながら腕を振ることは
  /// 物理的にできない。そのため「ボタン保持完了 → プロンプト表示 → ここで初めてジェスチャー待ち」という
  /// 時間差のある2段階フローにし、ボタンから手を離してから腕を振れるようにしている。
  /// </summary>
  public class ResumeUIController : MonoBehaviour
  {
    [SerializeField] private GestureRecognizer _gestureRecognizer;
    [SerializeField] private GameSettings _settings;
    [Tooltip("採用のハンコを押す間、右手を「ハンコ持ち手」モデルに差し替えるために使う")]
    [SerializeField] private HandTrackingController _handTrackingController;

    [Header("2D履歴書")]
    [SerializeField] private GameObject _resumeImageRoot;
    [SerializeField] private Image _resumeImage;
    [Tooltip("採用のハンコを押した際に表示するスタンプ画像のルート")]
    [SerializeField] private GameObject _stampImageRoot;

    [Header("採用/不採用ボタン（決定前のみ表示）")]
    [SerializeField] private CircularHoldButton _hireButton;
    [SerializeField] private CircularHoldButton _rejectButton;

    [Header("決定後のジェスチャー案内")]
    [Tooltip("採用ボタン確定後に表示する「ハンコを押せ！」等のUI")]
    [SerializeField] private GameObject _stampPromptUI;
    [Tooltip("不採用ボタン確定後に表示する「殴れ！」等のUI")]
    [SerializeField] private GameObject _punchPromptUI;

    [Header("戻るボタン")]
    [Tooltip("履歴書表示中、決定前に閉じるための円状ボタン")]
    [SerializeField] private CircularHoldButton _backButton;

    private CharacterData _currentCharacter;
    private int _currentPage;
    private bool _isOpen;
    private ResumeDecision _decision;
    private Coroutine _autoCloseCoroutine;

    public event Action<CharacterData> OnHireIntent;
    public event Action<CharacterData> OnRejectIntent;
    /// <summary>採用スタンプ確定（ハンコを振り下ろした）時に発火。整列演出のトリガーに使う。</summary>
    public event Action<CharacterData> OnStamped;
    /// <summary>不採用の一撃確定（殴った）時に発火。吹き飛び演出のトリガーに使う。</summary>
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
      _stampPromptUI?.SetActive(false);
      _punchPromptUI?.SetActive(false);
      if (_hireButton != null) _hireButton.gameObject.SetActive(false);
      if (_rejectButton != null) _rejectButton.gameObject.SetActive(false);
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
      if (_hireButton != null)
      {
        _hireButton.HoldSeconds = _settings != null ? _settings.resumeDecisionHoldSeconds : 2f;
        _hireButton.OnTriggered += HandleHireButtonConfirmed;
      }
      if (_rejectButton != null)
      {
        _rejectButton.HoldSeconds = _settings != null ? _settings.resumeDecisionHoldSeconds : 2f;
        _rejectButton.OnTriggered += HandleRejectButtonConfirmed;
      }
    }

    private void OnDisable()
    {
      if (_gestureRecognizer != null)
      {
        _gestureRecognizer.OnGestureDetected -= HandleGesture;
      }
      if (_backButton != null) _backButton.OnTriggered -= HandleBackRequested;
      if (_hireButton != null) _hireButton.OnTriggered -= HandleHireButtonConfirmed;
      if (_rejectButton != null) _rejectButton.OnTriggered -= HandleRejectButtonConfirmed;
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
      _stampPromptUI?.SetActive(false);
      _punchPromptUI?.SetActive(false);
      _resumeImageRoot?.SetActive(true);
      if (_backButton != null) _backButton.gameObject.SetActive(true);
      ShowDecisionButtons(true);
      _handTrackingController?.SetRightHandStampMode(false);
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

      _isOpen = false;
      _resumeImageRoot?.SetActive(false);
      _stampImageRoot?.SetActive(false);
      _stampPromptUI?.SetActive(false);
      _punchPromptUI?.SetActive(false);
      ShowDecisionButtons(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);
      _handTrackingController?.SetRightHandStampMode(false);
      _currentCharacter = null;

      OnClosed?.Invoke();
    }

    private void ShowDecisionButtons(bool show)
    {
      if (_hireButton != null) _hireButton.gameObject.SetActive(show);
      if (_rejectButton != null) _rejectButton.gameObject.SetActive(show);
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

    /// <summary>
    /// 「採用にする」ボタンの保持が完了した瞬間。ここではまだ最終決定にはしない
    /// （<see cref="OnHireIntent"/>は発火しない）。実際にハンコを振り下ろす(HammerSwingDown)まで
    /// 待つことで、次の候補・次のジャンルへ進む条件（3体全員の決定）が、ジェスチャーを
    /// やり切るまで満たされないようにする。
    /// </summary>
    private void HandleHireButtonConfirmed()
    {
      if (!_isOpen || _decision != ResumeDecision.None) return;

      _decision = ResumeDecision.Hired;
      ShowDecisionButtons(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);

      // ここで右手をハンコ持ち手に切り替え、「ハンコを押せ」の合図を出す。
      // ボタンへのポインター保持が終わった後なので、腕を振ってもポインター操作と競合しない。
      _handTrackingController?.SetRightHandStampMode(true);
      _stampPromptUI?.SetActive(true);
    }

    /// <summary>
    /// 「不採用にする」ボタンの保持が完了した瞬間。こちらもまだ最終決定にはしない
    /// （<see cref="OnRejectIntent"/>は発火しない）。実際に殴る(RightFistPunchOut)まで待つ。
    /// </summary>
    private void HandleRejectButtonConfirmed()
    {
      if (!_isOpen || _decision != ResumeDecision.None) return;

      _decision = ResumeDecision.Rejected;
      ShowDecisionButtons(false);
      if (_backButton != null) _backButton.gameObject.SetActive(false);
      _resumeImageRoot?.SetActive(false);

      _punchPromptUI?.SetActive(true);
    }

    private void HandleGesture(GestureType type)
    {
      if (!_isOpen || _currentCharacter == null) return;

      switch (type)
      {
        case GestureType.HoopBothHands:
          if (_decision == ResumeDecision.None) TurnPage();
          break;

        // ハンコを押す＝上から下へ振り下ろす動作なので、労働ミニゲームのハンマー打ちと同じ
        // HammerSwingDown（振り上げていた手が急速に下降）を流用する。
        case GestureType.HammerSwingDown:
          if (_decision == ResumeDecision.Hired)
          {
            ConfirmStamp();
          }
          break;

        // 殴る＝前方への突き出しはそのままRightFistPunchOutを使う。
        case GestureType.RightFistPunchOut:
          if (_decision == ResumeDecision.Rejected)
          {
            ConfirmPunch();
          }
          break;
      }
    }

    private void ConfirmStamp()
    {
      _stampPromptUI?.SetActive(false);
      _stampImageRoot?.SetActive(true);
      _handTrackingController?.SetRightHandStampMode(false);

      // ここで初めて最終決定として扱う。次の候補・次のジャンルへ進む条件は
      // このイベントが発火するまで満たされない。
      OnHireIntent?.Invoke(_currentCharacter);
      OnStamped?.Invoke(_currentCharacter);

      // 「履歴書は表示したまま」にしつつ、少し見せたら自動的に次の候補へ戻れるようにする。
      var delay = _settings != null ? _settings.hireStampDisplaySeconds : 1.5f;
      _autoCloseCoroutine = StartCoroutine(AutoCloseAfter(delay));
    }

    private void ConfirmPunch()
    {
      _punchPromptUI?.SetActive(false);

      // ここで初めて最終決定として扱う。
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
  }
}
