using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemonLordHR.Ending;
using DemonLordHR.FinalBattle;
using DemonLordHR.HandTracking;
using DemonLordHR.Minigames;
using DemonLordHR.Recruitment;
using DemonLordHR.UI;
using UnityEngine;

namespace DemonLordHR.Core
{
  public enum GameState
  {
    Title,
    RuleExplanation,
    Recruitment,
    HeroIncoming,
    Minigame,
    FinalBattle,
    Ending,
  }

  /// <summary>
  /// タイトル→採用試験→ミニゲーム→最終決戦→エンディングの状態遷移を管理する（仕様書2章）。
  /// 各フェーズの重い処理は専用コントローラーに委譲し、本クラスは進行順序とジャンル選択のみを扱う。
  /// </summary>
  public class GameFlowManager : MonoBehaviour
  {
    [SerializeField] private GameSettings _settings;
    [SerializeField] private HandTrackingController _handTrackingController;
    [SerializeField] private CircularHoldButton _titleStartButton;
    [SerializeField] private CircularHoldButton _ruleReadyButton;
    [SerializeField] private RecruitmentPhaseController _recruitmentController;
    [SerializeField] private FinalBattleController _finalBattleController;
    [SerializeField] private EndingController _endingController;

    [Header("ミニゲーム（ジャンルごとに1つ割り当てる）")]
    [SerializeField] private List<GenreMinigameEntry> _minigames = new List<GenreMinigameEntry>();

    [System.Serializable]
    public class GenreMinigameEntry
    {
      public RecruitmentGenre genre;
      public MinigameBase minigame;
    }

    public GameState CurrentState { get; private set; } = GameState.Title;

    private readonly List<RecruitmentGenre> _selectedGenres = new List<RecruitmentGenre>();
    private int _defenseSuccessCount;
    private int _gameOverCount;

    private void Start()
    {
      StartCoroutine(RunGameLoopForever());
    }

    private IEnumerator RunGameLoopForever()
    {
      while (true)
      {
        yield return RunSingleGameAsync();
      }
    }

    private IEnumerator RunSingleGameAsync()
    {
      CurrentState = GameState.Title;
      if (_handTrackingController != null) _handTrackingController.HandsVisible = false;
      yield return WaitForButton(_titleStartButton);

      CurrentState = GameState.RuleExplanation;
      // TODO: ルール説明画像UIを表示する
      if (_ruleReadyButton != null)
      {
        _ruleReadyButton.HoldSeconds = _settings != null ? _settings.readyHoldSeconds : 3f;
      }
      yield return WaitForButton(_ruleReadyButton);

      if (_handTrackingController != null) _handTrackingController.HandsVisible = true;

      _selectedGenres.Clear();
      _selectedGenres.AddRange(SelectGenresForThisRun());

      CurrentState = GameState.Recruitment;
      foreach (var genre in _selectedGenres)
      {
        if (_recruitmentController != null)
        {
          yield return _recruitmentController.RunGenreAsync(genre);
        }
      }

      CurrentState = GameState.HeroIncoming;
      // TODO: 「勇者襲来」UI表示
      yield return new WaitForSeconds(_settings != null ? _settings.heroIncomingDisplaySeconds : 3f);

      CurrentState = GameState.Minigame;
      _defenseSuccessCount = 0;
      _gameOverCount = 0;
      foreach (var genre in _selectedGenres)
      {
        var minigame = _minigames.FirstOrDefault(e => e.genre == genre)?.minigame;
        if (minigame == null) continue;

        var hiredForGenre = _recruitmentController != null
          ? _recruitmentController.HiredCharacters.Where(c => c.genre == genre)
          : System.Array.Empty<CharacterData>();
        minigame.AssignCharacters(hiredForGenre);

        var finished = false;
        var result = MinigameResult.None;
        void OnFinished(MinigameResult r) { result = r; finished = true; }
        minigame.OnMinigameFinished += OnFinished;

        yield return minigame.RunAsync();
        yield return new WaitUntil(() => finished);
        minigame.OnMinigameFinished -= OnFinished;

        if (result == MinigameResult.DefenseSuccess) _defenseSuccessCount++;
        else if (result == MinigameResult.GameOver) _gameOverCount++;
      }

      CurrentState = GameState.FinalBattle;
      if (_finalBattleController != null)
      {
        _finalBattleController.SetHeroConditionFromMinigameResults(_defenseSuccessCount, _gameOverCount);
        yield return _finalBattleController.RunAsync();
      }

      CurrentState = GameState.Ending;
      if (_endingController != null)
      {
        var summary = $"防衛成功:{_defenseSuccessCount} ゲームオーバー:{_gameOverCount}";
        var hired = _recruitmentController != null ? _recruitmentController.HiredCharacters : new List<CharacterData>();
        yield return _endingController.RunAsync(summary, hired);
      }
    }

    private IEnumerable<RecruitmentGenre> SelectGenresForThisRun()
    {
      if (_settings == null || _settings.availableGenres == null || _settings.availableGenres.Count == 0)
      {
        yield break;
      }

      var count = Mathf.Min(_settings.recruitmentCycleCount, _settings.availableGenres.Count);
      for (var i = 0; i < count; i++)
      {
        yield return _settings.availableGenres[i];
      }
    }

    private IEnumerator WaitForButton(CircularHoldButton button)
    {
      if (button == null) yield break;

      var triggered = false;
      void OnTriggered() => triggered = true;
      button.OnTriggered += OnTriggered;
      yield return new WaitUntil(() => triggered);
      button.OnTriggered -= OnTriggered;
    }
  }
}
