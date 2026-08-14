using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemonLordHR.Core;
using DemonLordHR.UI;
using UnityEngine;

namespace DemonLordHR.Recruitment
{
  /// <summary>
  /// 採用試験フェーズ（仕様書3章）のロジック全体を統括する。
  /// 1ジャンルにつき3体の候補キャラを登場させ、履歴書閲覧→採用/不採用の決定→
  /// 終了演出までを進行する。3D演出（キャラプレハブの実際の見た目、スタンプ演出等）は
  /// アセット未整備のためTODOのまま、進行ロジックとイベント発行に注力する。
  /// </summary>
  public class RecruitmentPhaseController : MonoBehaviour
  {
    [SerializeField] private GameSettings _settings;
    [SerializeField] private ResumeUIController _resumeUIController;
    [SerializeField] private CircularHoldButton _endInterviewButton;

    [Header("入室・整列")]
    [Tooltip("扉の位置（キャラのスポーン地点）")]
    [SerializeField] private Transform _doorSpawnPoint;
    [Tooltip("3体分の最終停止位置")]
    [SerializeField] private Transform[] _standPositions = new Transform[3];
    [Tooltip("候補キャラの入室ルート（曲がり角の座標リスト、standPositionsと同数）")]
    [SerializeField] private List<Vector3>[] _entryWaypoints = new List<Vector3>[3];

    public event Action<RecruitmentGenre> OnGenreStarted;
    public event Action<CharacterData> OnCharacterHired;
    public event Action<CharacterData> OnCharacterRejected;
    public event Action<RecruitmentGenre> OnGenreCompleted;

    public int CurrentFunds { get; private set; }
    public IReadOnlyList<CharacterData> HiredCharacters => _hiredCharacters;

    private readonly List<CharacterData> _hiredCharacters = new List<CharacterData>();
    private readonly List<CharacterData> _candidates = new List<CharacterData>();
    private readonly HashSet<CharacterData> _decided = new HashSet<CharacterData>();
    private bool _endRequested;

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
      }
    }

    /// <summary>1ジャンル分の採用試験フェーズを実行する（仕様3.1〜3.6）。</summary>
    public IEnumerator RunGenreAsync(RecruitmentGenre genre)
    {
      OnGenreStarted?.Invoke(genre);
      _endRequested = false;
      _decided.Clear();
      _candidates.Clear();

      // 3.1: 「{ジャンル名}のキャラの採用試験を開始する」UI表示
      yield return new WaitForSeconds(_settings != null ? _settings.genreStartDisplaySeconds : 3f);

      CurrentFunds += _settings != null ? _settings.recruitmentPhaseFunds : 100;

      _candidates.AddRange(PickCandidates(genre, 3));

      // 入室演出
      var entryCoroutines = new List<Coroutine>();
      for (var i = 0; i < _candidates.Count && i < _standPositions.Length; i++)
      {
        entryCoroutines.Add(StartCoroutine(SpawnAndWalkIn(_candidates[i], i)));
      }
      foreach (var c in entryCoroutines) yield return c;

      // 3.5: 3体全員決定 or 「面接終了する」5秒保持のいずれかで終了
      yield return new WaitUntil(() => _endRequested || _decided.Count >= _candidates.Count);

      // 3.6: 終了演出
      foreach (var candidate in _candidates)
      {
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

      // TODO: 入室完了後、キャラの前に履歴書オブジェクトを出現させ、
      // ポインター3秒保持で ResumeUIController.Open(character) を呼ぶトリガーを設置する。
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
      OnCharacterHired?.Invoke(character);
    }

    private void HandleRejectIntent(CharacterData character)
    {
      if (_decided.Contains(character)) return;
      _decided.Add(character);
      OnCharacterRejected?.Invoke(character);
    }

    private void HandleEndInterviewRequested()
    {
      _endRequested = true;
    }

    private void PlayHireLineup(CharacterData character, int index)
    {
      if (_settings == null) return;
      var origin = _settings.hiredLineupOrigin;
      var dir = _settings.hiredLineupDirection.normalized;
      var targetPos = origin + dir * (_settings.hiredLineupSpacing * index);
      // TODO: 対応するインスタンスを targetPos へ整列移動させる演出を実装する。
    }

    private void PlayRejectKnockback(CharacterData character)
    {
      if (_settings == null) return;
      // TODO: 対応するインスタンスに settings.rejectKnockbackDirection / rejectKnockbackForce で
      // 吹き飛び演出を適用する。
    }
  }
}
