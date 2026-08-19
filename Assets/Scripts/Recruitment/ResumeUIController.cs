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
  /// 採用/不採用は「履歴書を開いている間、常にハンコ振り下ろし(採用)／パンチ(不採用)ジェスチャーだけで
  /// 一撃確定」する。以前はポインター保持ボタンで仮決定してからジェスチャーで確定する2段階方式だったが、
  /// 手を見せて操作すること自体を面白さの核に据えるなら、確定ジェスチャーの前に地味なポインター的当てを
  /// 挟むのは本末転倒だった。ポインターと確定ジェスチャーが両方右手、という物理制約も、
  /// 決定をジェスチャーだけで完結させることで自然に解消される（ポインターで的を狙う必要が無くなるため）。
  ///
  /// 誤発火対策として、履歴書を開いた直後は<see cref="GameSettings.resumeGestureArmDelaySeconds"/>秒だけ
  /// ジェスチャー判定を無効化する「構えの猶予」を設けている。それ以外は、殴る/振り下ろす動作自体の
  /// 閾値（<see cref="GestureRecognizer"/>側でチューニング済み）を誤発火対策の主軸としている。
  /// 決定後の取り消しは用意していない（「魔王の決定は覆らない」という演出として割り切っている）。
  /// </summary>
  public class ResumeUIController : MonoBehaviour
  {
    [SerializeField] private GestureRecognizer _gestureRecognizer;
    [SerializeField] private GameSettings _settings;
    [Tooltip("履歴書を開いている間、右手を「ハンコ持ち手」モデルに差し替えるために使う")]
    [SerializeField] private HandTrackingController _handTrackingController;
    [Tooltip("採用ジェスチャー時の資金不足チェックに使う（未設定の場合はチェックをスキップする）")]
    [SerializeField] private RecruitmentPhaseController _recruitmentController;

    [Header("2D履歴書")]
    [SerializeField] private GameObject _resumeImageRoot;
    [SerializeField] private Image _resumeImage;
    [Tooltip("採用のハンコを押した際に表示するスタンプ画像のルート")]
    [SerializeField] private GameObject _stampImageRoot;

    [Header("決定ジェスチャーの案内（履歴書を開いている間、常時表示）")]
    [Tooltip("「ハンコを振り下ろせば採用」等の案内UI")]
    [SerializeField] private GameObject _stampPromptUI;
    [Tooltip("「殴れば不採用」等の案内UI")]
    [SerializeField] private GameObject _punchPromptUI;

    [Header("戻るボタン")]
    [Tooltip("履歴書表示中、決定前に閉じるための円状ボタン")]
    [SerializeField] private CircularHoldButton _backButton;

    private CharacterData _currentCharacter;
    private int _currentPage;
    private bool _isOpen;
    private ResumeDecision _decision;
    private float _gestureArmTime;
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
      _gestureArmTime = Time.time + (_settings != null ? _settings.resumeGestureArmDelaySeconds : 1f);

      _stampImageRoot?.SetActive(false);
      _resumeImageRoot?.SetActive(true);
      if (_backButton != null) _backButton.gameObject.SetActive(true);
      // 開いている間は常に「振り下ろせば採用／殴れば不採用」の両方を案内し続ける。
      _stampPromptUI?.SetActive(true);
      _punchPromptUI?.SetActive(true);
      // ジェスチャー検出自体はMediaPipeの生の手の動きで行うため、見た目のモデルを
      // ハンコ持ち手に切り替えてもパンチ判定には影響しない。開いた瞬間から切り替えておくことで、
      // 「いつでも振り下ろせる」ことを視覚的に示す。
      _handTrackingController?.SetRightHandStampMode(true);
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
      if (_backButton != null) _backButton.gameObject.SetActive(false);
      _handTrackingController?.SetRightHandStampMode(false);
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

    private void HandleGesture(GestureType type)
    {
      if (!_isOpen || _currentCharacter == null || _decision != ResumeDecision.None) return;

      switch (type)
      {
        case GestureType.HoopBothHands:
          TurnPage();
          break;

        // ハンコを押す＝上から下へ振り下ろす動作なので、労働ミニゲームのハンマー打ちと同じ
        // HammerSwingDown（振り上げていた手が急速に下降）を流用する。
        case GestureType.HammerSwingDown:
          TryConfirmHire();
          break;

        // 殴る＝Combatミニゲームと同じAlternatingPunch（左右どちらかの拳を突き出す）を流用する。
        // ジェスチャーの種類を増やさず、Combatと感覚を統一するため。
        case GestureType.AlternatingPunch:
          TryConfirmReject();
          break;
      }
    }

    private void TryConfirmHire()
    {
      if (Time.time < _gestureArmTime) return; // 開いた直後の構えの猶予中は無視する
      if (_recruitmentController != null && !_recruitmentController.CanAfford(_currentCharacter)) return; // 資金不足

      ConfirmStamp();
    }

    private void TryConfirmReject()
    {
      if (Time.time < _gestureArmTime) return;

      ConfirmPunch();
    }

    private void ConfirmStamp()
    {
      _decision = ResumeDecision.Hired;
      _stampPromptUI?.SetActive(false);
      _punchPromptUI?.SetActive(false);
      _stampImageRoot?.SetActive(true);
      _handTrackingController?.SetRightHandStampMode(false);

      OnHireIntent?.Invoke(_currentCharacter);
      OnStamped?.Invoke(_currentCharacter);

      // 「履歴書は表示したまま」にしつつ、少し見せたら自動的に次の候補へ戻れるようにする。
      var delay = _settings != null ? _settings.hireStampDisplaySeconds : 1.5f;
      _autoCloseCoroutine = StartCoroutine(AutoCloseAfter(delay));
    }

    private void ConfirmPunch()
    {
      _decision = ResumeDecision.Rejected;
      _stampPromptUI?.SetActive(false);
      _punchPromptUI?.SetActive(false);
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
  }
}
