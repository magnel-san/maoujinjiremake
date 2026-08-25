using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemonLordHR.Core;
using DemonLordHR.UI;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Recruitment
{
  /// <summary>
  /// 採用試験フェーズ（仕様書3章）のロジック全体を統括する。
  /// 1ジャンルにつき3体の候補キャラを登場させ、履歴書閲覧→採用/不採用の決定→
  /// 終了演出までを進行する。整列移動・吹き飛びは実装済み。
  /// 採用はスタンプ確定時、不採用はパンチ確定時に即座に演出を再生し、
  /// 「面接終了する」による自動不採用（ジェスチャー確定を経ない）だけジャンル終了時にまとめて処理する。
  /// </summary>
  public class RecruitmentPhaseController : MonoBehaviour
  {
    [SerializeField] private GameSettings _settings;
    [SerializeField] private ResumeUIController _resumeUIController;
    [SerializeField] private CircularHoldButton _endInterviewButton;
    [Tooltip("現在の所持資金を表示するテキスト（未設定でも動作する）")]
    [SerializeField] private TMP_Text _fundsText;

    [Header("ジャンル開始表示")]
    [Tooltip("「{ジャンル名}の採用試験を開始する」を表示するUIのルート。未設定なら表示自体を行わない" +
      "（テキストの表示/非表示だけをこのルートで切り替える）。")]
    [SerializeField] private GameObject _genreStartRoot;
    [Tooltip("ジャンル名を書き込むテキスト。")]
    [SerializeField] private TMP_Text _genreStartText;

    [Header("入室・整列")]
    [Tooltip("扉の位置（キャラのスポーン地点）")]
    [SerializeField] private Transform _doorSpawnPoint;
    [Tooltip("3体分の最終停止位置")]
    [SerializeField] private Transform[] _standPositions = new Transform[3];
    [Tooltip("候補キャラの入室ルート（曲がり角の座標リスト、standPositionsと同数）")]
    [SerializeField] private List<Vector3>[] _entryWaypoints = new List<Vector3>[3];

    [Header("履歴書")]
    [Tooltip("CircularHoldButton付きの履歴書オブジェクトのプレハブ（ゲージ画像もここで設定する）")]
    [SerializeField] private GameObject _resumePrefab;
    [Tooltip("履歴書オブジェクトを、キャラの位置からどれだけずらして配置するか")]
    [SerializeField] private Vector3 _resumeLocalOffset = new Vector3(0f, 1.2f, 0.5f);
    [Tooltip("生成した履歴書オブジェクトの親（「UI」CanvasのTransform等）。" +
      "祖先にCanvasが無いとUI(Image)が描画されないため、Canvasの子として生成する。")]
    [SerializeField] private Transform _resumeParent;

    private readonly Dictionary<CharacterData, GameObject> _characterInstances = new Dictionary<CharacterData, GameObject>();
    private readonly Dictionary<CharacterData, GameObject> _resumeInstances = new Dictionary<CharacterData, GameObject>();
    /// <summary>整列/吹き飛び演出を既に再生したキャラ。スタンプ/パンチのジェスチャー確定時に即座に再生するため、
    /// ジャンル終了時の一括処理で二重に再生しないようにするためのガード。</summary>
    private readonly HashSet<CharacterData> _animatedCharacters = new HashSet<CharacterData>();

    public event Action<RecruitmentGenre> OnGenreStarted;
    public event Action<CharacterData> OnCharacterHired;
    public event Action<CharacterData> OnCharacterRejected;
    public event Action<RecruitmentGenre> OnGenreCompleted;

    public int CurrentFunds { get; private set; }
    public IReadOnlyList<CharacterData> HiredCharacters => _hiredCharacters;

    /// <summary>採用ジェスチャーを受け付ける前に、残り資金で給料を払えるか確認するために使う。</summary>
    public bool CanAfford(CharacterData character) => character != null && character.salary <= CurrentFunds;

    private readonly List<CharacterData> _hiredCharacters = new List<CharacterData>();
    private readonly List<CharacterData> _candidates = new List<CharacterData>();
    private readonly HashSet<CharacterData> _decided = new HashSet<CharacterData>();
    private bool _endRequested;
    private bool _interviewInProgress;

    private void Awake()
    {
      // 採用試験を実施していない間はボタンを隠しておく。
      if (_endInterviewButton != null) _endInterviewButton.gameObject.SetActive(false);
      SetGenreStartActive(false);
      UpdateFundsDisplay();
    }

    private void UpdateFundsDisplay()
    {
      if (_fundsText != null) _fundsText.text = $"資金: {CurrentFunds}";
    }

    private void OnEnable()
    {
      if (_endInterviewButton != null)
      {
        _endInterviewButton.HoldSeconds = _settings != null ? _settings.endInterviewHoldSeconds : 5f;
        _endInterviewButton.OnTriggered += HandleEndInterviewRequested;
      }
      if (_resumeUIController != null)
      {
        _resumeUIController.OnHireIntent += HandleHireIntent;
        _resumeUIController.OnRejectIntent += HandleRejectIntent;
        _resumeUIController.OnStamped += HandleStamped;
        _resumeUIController.OnThrown += HandleThrown;
        _resumeUIController.OnOpened += HandleResumeOpened;
        _resumeUIController.OnClosed += HandleResumeClosed;
      }
    }

    private void OnDisable()
    {
      if (_endInterviewButton != null)
      {
        _endInterviewButton.OnTriggered -= HandleEndInterviewRequested;
      }
      if (_resumeUIController != null)
      {
        _resumeUIController.OnHireIntent -= HandleHireIntent;
        _resumeUIController.OnRejectIntent -= HandleRejectIntent;
        _resumeUIController.OnStamped -= HandleStamped;
        _resumeUIController.OnThrown -= HandleThrown;
        _resumeUIController.OnOpened -= HandleResumeOpened;
        _resumeUIController.OnClosed -= HandleResumeClosed;
      }
    }

    /// <summary>1ジャンル分の採用試験フェーズを実行する（仕様3.1〜3.6）。</summary>
    public IEnumerator RunGenreAsync(RecruitmentGenre genre)
    {
      OnGenreStarted?.Invoke(genre);
      _endRequested = false;
      _decided.Clear();
      _candidates.Clear();
      _animatedCharacters.Clear();

      // 3.1: 「{ジャンル名}のキャラの採用試験を開始する」UI表示。候補キャラの入室（SpawnAndWalkIn）より
      // 前に、ジャンル名を数秒間だけ表示する。
      yield return ShowGenreStart(genre);

      CurrentFunds += _settings != null ? _settings.recruitmentPhaseFunds : 100;
      UpdateFundsDisplay();

      _candidates.AddRange(PickCandidates(genre, 3));

      // 入室演出
      var entryCoroutines = new List<Coroutine>();
      for (var i = 0; i < _candidates.Count && i < _standPositions.Length; i++)
      {
        entryCoroutines.Add(StartCoroutine(SpawnAndWalkIn(_candidates[i], i)));
      }
      foreach (var c in entryCoroutines) yield return c;

      _interviewInProgress = true;
      UpdateEndInterviewButtonVisibility();

      // 3.5: 3体全員決定 or 「面接終了する」5秒保持のいずれかで終了
      yield return new WaitUntil(() => _endRequested || _decided.Count >= _candidates.Count);

      _interviewInProgress = false;
      UpdateEndInterviewButtonVisibility();

      // 「面接終了する」で打ち切った場合、まだ決定していない候補は自動的に不採用扱いにする。
      foreach (var candidate in _candidates)
      {
        if (!_decided.Contains(candidate))
        {
          HandleRejectIntent(candidate);
        }
      }

      // 3.6: 終了演出（スタンプ/パンチ確定時に既に再生済みのキャラはスキップする。
      // ここに残るのは「面接終了する」で自動不採用になった、ジェスチャー確定を経ていないキャラだけのはず）。
      foreach (var candidate in _candidates)
      {
        if (_animatedCharacters.Contains(candidate)) continue;

        if (_hiredCharacters.Contains(candidate))
        {
          PlayHireLineup(candidate, _hiredCharacters.IndexOf(candidate));
        }
        else if (_decided.Contains(candidate))
        {
          PlayRejectKnockback(candidate);
        }
      }

      OnGenreCompleted?.Invoke(genre);
    }

    /// <summary>候補キャラが入室する前に「{ジャンル名}の採用試験を開始する」を数秒間表示する。
    /// _genreStartRootが未設定でも、_genreStartText自体のGameObjectを表示/非表示できるようにしておく。</summary>
    private IEnumerator ShowGenreStart(RecruitmentGenre genre)
    {
      if (_genreStartText != null)
      {
        _genreStartText.text = _settings != null ? _settings.GetGenreStartMessage(genre) : $"{genre.ToDisplayName()}の採用試験を開始する";
      }
      SetGenreStartActive(true);

      yield return new WaitForSeconds(_settings != null ? _settings.genreStartDisplaySeconds : 3f);

      SetGenreStartActive(false);
    }

    private void SetGenreStartActive(bool active)
    {
      if (_genreStartRoot != null) _genreStartRoot.SetActive(active);
      else if (_genreStartText != null) _genreStartText.gameObject.SetActive(active);
    }

    private List<CharacterData> PickCandidates(RecruitmentGenre genre, int count)
    {
      var pool = _settings != null
        ? _settings.allCharacters.Where(c => c != null && c.genre == genre && !_hiredCharacters.Contains(c)).ToList()
        : new List<CharacterData>();

      var picked = new List<CharacterData>();
      var rng = new System.Random();
      while (picked.Count < count && pool.Count > 0)
      {
        var index = rng.Next(pool.Count);
        picked.Add(pool[index]);
        pool.RemoveAt(index);
      }
      return picked;
    }

    private IEnumerator SpawnAndWalkIn(CharacterData character, int slotIndex)
    {
      if (character.characterPrefab == null || _doorSpawnPoint == null) yield break;

      var instance = Instantiate(character.characterPrefab, _doorSpawnPoint.position, _doorSpawnPoint.rotation);
      _characterInstances[character] = instance;
      var path = instance.GetComponent<CharacterEntryPath>();
      if (path == null) path = instance.AddComponent<CharacterEntryPath>();

      var waypoints = slotIndex < _entryWaypoints.Length && _entryWaypoints[slotIndex] != null
        ? _entryWaypoints[slotIndex]
        : new List<Vector3> { _standPositions[slotIndex].position };

      path.SetWaypoints(waypoints);
      path.SetDuration(_settings != null ? _settings.characterEntryDuration : 4f);

      var done = false;
      path.OnArrived += () => done = true;
      yield return StartCoroutine(path.WalkAsync());
      yield return new WaitUntil(() => done);

      SpawnResumeTrigger(character, instance);
    }

    private void SpawnResumeTrigger(CharacterData character, GameObject characterInstance)
    {
      if (_resumePrefab == null) return;

      // 回転はワールド基準ではなく、親（UI Canvas）と同じ向きに合わせる。
      // Quaternion.identity固定だとCanvasの回転（Y180°等）と噛み合わず、文字が裏返って見える。
      var rotation = _resumeParent != null ? _resumeParent.rotation : Quaternion.identity;
      var resumeInstance = Instantiate(_resumePrefab, characterInstance.transform.position + _resumeLocalOffset, rotation, _resumeParent);

      var button = resumeInstance.GetComponent<CircularHoldButton>();
      if (button != null)
      {
        button.HoldSeconds = _settings != null ? _settings.resumeViewHoldSeconds : 3f;
        button.OnTriggered += () => RequestOpenResume(character);
      }

      _resumeInstances[character] = resumeInstance;
    }

    public void RequestOpenResume(CharacterData character)
    {
      _resumeUIController?.Open(character);
    }

    private void HandleHireIntent(CharacterData character)
    {
      if (_decided.Contains(character)) return;
      _decided.Add(character);
      _hiredCharacters.Add(character);
      CurrentFunds -= character.salary;
      UpdateFundsDisplay();
      HideResumeTrigger(character);
      OnCharacterHired?.Invoke(character);
    }

    private void HandleRejectIntent(CharacterData character)
    {
      if (_decided.Contains(character)) return;
      _decided.Add(character);
      HideResumeTrigger(character);
      OnCharacterRejected?.Invoke(character);
    }

    private void HandleEndInterviewRequested()
    {
      _endRequested = true;
    }

    /// <summary>
    /// 履歴書が開かれている間は「面接終了する」を押せないようにする。
    /// 押せてしまうと、今まさにハンコ/パンチ待ちの候補が自動不採用で二重処理されてしまい、
    /// UIが閉じずに残るバグの原因になる。
    /// </summary>
    private void HandleResumeOpened(CharacterData character)
    {
      UpdateEndInterviewButtonVisibility();
      SetOtherResumeTriggersActive(false);
    }

    private void HandleResumeClosed()
    {
      UpdateEndInterviewButtonVisibility();
      SetOtherResumeTriggersActive(true);
    }

    private void UpdateEndInterviewButtonVisibility()
    {
      if (_endInterviewButton == null) return;
      var resumeOpen = _resumeUIController != null && _resumeUIController.IsOpen;
      _endInterviewButton.gameObject.SetActive(_interviewInProgress && !resumeOpen);
    }

    /// <summary>履歴書を1つ開いている間、他の（まだ決定していない）候補の履歴書トリガーも
    /// 触れないよう非表示にする。同時に複数の履歴書に触れると状態が壊れるため。</summary>
    private void SetOtherResumeTriggersActive(bool active)
    {
      foreach (var kvp in _resumeInstances)
      {
        if (kvp.Value == null) continue;
        if (active)
        {
          if (_decided.Contains(kvp.Key)) continue; // 決定済みのキャラは再表示しない
          kvp.Value.SetActive(true);
        }
        else
        {
          kvp.Value.SetActive(false);
        }
      }
    }

    /// <summary>採用スタンプが確定した瞬間、すぐに整列演出を再生する。</summary>
    private void HandleStamped(CharacterData character)
    {
      if (!_animatedCharacters.Add(character)) return;
      PlayHireLineup(character, _hiredCharacters.IndexOf(character));
    }

    /// <summary>不採用の一撃が確定した瞬間、すぐに吹き飛び演出を再生する。</summary>
    private void HandleThrown(CharacterData character)
    {
      if (!_animatedCharacters.Add(character)) return;
      PlayRejectKnockback(character);
    }

    /// <summary>3.3/3.4: 決定後、履歴書を開くためのトリガー表示を非表示化する。</summary>
    private void HideResumeTrigger(CharacterData character)
    {
      if (_resumeInstances.TryGetValue(character, out var resumeInstance) && resumeInstance != null)
      {
        resumeInstance.SetActive(false);
      }
    }

    private void PlayHireLineup(CharacterData character, int index)
    {
      if (_settings == null) return;
      if (!_characterInstances.TryGetValue(character, out var instance) || instance == null) return;

      var origin = _settings.hiredLineupOrigin;
      var dir = _settings.hiredLineupAxis.ToDirection();
      var targetPos = origin + dir * (_settings.hiredLineupSpacing * index);
      StartCoroutine(MoveOverTime(instance.transform, targetPos, _settings.hiredLineupMoveDuration));
    }

    private void PlayRejectKnockback(CharacterData character)
    {
      if (_settings == null) return;
      if (!_characterInstances.TryGetValue(character, out var instance) || instance == null) return;

      var targetPos = instance.transform.position + _settings.rejectKnockbackDirection.normalized * _settings.rejectKnockbackForce;
      StartCoroutine(KnockbackAndDestroy(instance, character, targetPos, _settings.rejectKnockbackDuration));
    }

    private static IEnumerator MoveOverTime(Transform target, Vector3 destination, float duration)
    {
      var start = target.position;
      var elapsed = 0f;
      duration = Mathf.Max(duration, 0.01f);

      while (elapsed < duration)
      {
        elapsed += Time.deltaTime;
        target.position = Vector3.Lerp(start, destination, elapsed / duration);
        yield return null;
      }

      target.position = destination;
    }

    private IEnumerator KnockbackAndDestroy(GameObject instance, CharacterData character, Vector3 destination, float duration)
    {
      yield return MoveOverTime(instance.transform, destination, duration);

      _characterInstances.Remove(character);
      var resumeInstance = _resumeInstances.TryGetValue(character, out var r) ? r : null;
      _resumeInstances.Remove(character);
      if (resumeInstance != null) Destroy(resumeInstance);
      Destroy(instance);
    }
  }
}
