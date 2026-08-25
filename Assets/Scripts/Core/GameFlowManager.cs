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
    [Tooltip("ミニゲームのたびにワープするプレイヤー本体（MinigameBase.playerRootと同じオブジェクト）。" +
      "ミニゲームフェーズ開始前の位置を覚えておき、全ミニゲーム終了後にそこへ戻すために使う。")]
    [SerializeField] private Transform _playerRoot;
    [SerializeField] private CircularHoldButton _titleStartButton;
    [SerializeField] private CircularHoldButton _ruleReadyButton;
    [SerializeField] private RecruitmentPhaseController _recruitmentController;
    [SerializeField] private FinalBattleController _finalBattleController;
    [SerializeField] private EndingController _endingController;

    [Header("ミニゲーム（ジャンルごとに1つ割り当てる）")]
    [SerializeField] private List<GenreMinigameEntry> _minigames = new List<GenreMinigameEntry>();

    [Header("デバッグ：指定したミニゲームから直接開始する")]
    [Tooltip("ONの場合、タイトル画面・採用試験を全てスキップし、_debugGenreに対応するミニゲームだけを" +
      "_debugHiredCharactersを採用済み扱いにして実行する。個別ミニゲームの動作確認用。")]
    [SerializeField] private bool _debugStartFromMinigame;
    [SerializeField] private RecruitmentGenre _debugGenre;
    [Tooltip("デバッグ時、採用試験を経ずに「採用済み」として渡す仮のキャラクター一覧")]
    [SerializeField] private List<CharacterData> _debugHiredCharacters = new List<CharacterData>();

    [System.Serializable]
    public class GenreMinigameEntry
    {
      public RecruitmentGenre genre;
      public MinigameBase minigame;
    }

    public GameState CurrentState { get; private set; } = GameState.Title;

    private readonly List<RecruitmentGenre> _selectedGenres = new List<RecruitmentGenre>();
    private readonly List<(RecruitmentGenre genre, float score)> _genreScores = new List<(RecruitmentGenre, float)>();
    private int _defenseSuccessCount;
    private int _gameOverCount;

    private void Awake()
    {
      // ゲーム開始時点で不要なUIは非表示にしておく。各フェーズの担当箇所が必要な時だけ表示する。
      SetButtonActive(_titleStartButton, false);
      SetButtonActive(_ruleReadyButton, false);
    }

    private void Start()
    {
      StartCoroutine(_debugStartFromMinigame ? RunDebugMinigameOnly() : RunGameLoopForever());
    }

    /// <summary>デバッグ用：タイトル・採用試験を全てスキップし、指定した1ジャンルのミニゲームだけを
    /// 仮のキャラクターで実行する。個別ミニゲームの動作確認をするための入り口。</summary>
    private IEnumerator RunDebugMinigameOnly()
    {
      // 通常フローのミニゲーム中と同じく、手モデルは邪魔になるので非表示にする。
      if (_handTrackingController != null) _handTrackingController.HandsVisible = false;

      var minigame = _minigames.FirstOrDefault(e => e.genre == _debugGenre)?.minigame;
      if (minigame == null)
      {
        Debug.LogError($"[GameFlowManager] デバッグ起動: ジャンル{_debugGenre}に対応するミニゲームが_minigamesに設定されていません。");
        yield break;
      }

      minigame.AssignCharacters(_debugHiredCharacters);

      var finished = false;
      void OnFinished(MinigameResult r) => finished = true;
      minigame.OnMinigameFinished += OnFinished;

      yield return minigame.RunAsync();
      yield return new WaitUntil(() => finished);

      minigame.OnMinigameFinished -= OnFinished;
      Debug.Log($"[GameFlowManager] デバッグ起動: {_debugGenre}のミニゲームが終了しました。");
    }

    private static void SetButtonActive(CircularHoldButton button, bool active)
    {
      if (button != null) button.gameObject.SetActive(active);
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
      SetButtonActive(_titleStartButton, true);
      yield return WaitForButton(_titleStartButton);
      SetButtonActive(_titleStartButton, false);

      CurrentState = GameState.RuleExplanation;
      // TODO: ルール説明画像UIを表示する
      if (_ruleReadyButton != null)
      {
        _ruleReadyButton.HoldSeconds = _settings != null ? _settings.readyHoldSeconds : 3f;
      }
      SetButtonActive(_ruleReadyButton, true);
      yield return WaitForButton(_ruleReadyButton);
      SetButtonActive(_ruleReadyButton, false);

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
      // 魔王の手モデルはミニゲーム中は画面上邪魔になるため非表示にする（ジェスチャー認識自体は
      // MediaPipeの生データを直接見ているため、手モデルを消しても動作に影響しない）。
      if (_handTrackingController != null) _handTrackingController.HandsVisible = false;
      _defenseSuccessCount = 0;
      _gameOverCount = 0;
      _genreScores.Clear();

      // 各ミニゲームはこの後プレイヤー本体をその場所へワープさせるため、
      // 全ミニゲーム終了後に戻れるよう、ここで元の位置を覚えておく。
      var originalPlayerPosition = _playerRoot != null ? (Vector3?)_playerRoot.position : null;

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

        _genreScores.Add((genre, minigame.FinalScore));
      }

      if (originalPlayerPosition.HasValue) _playerRoot.position = originalPlayerPosition.Value;
      if (_handTrackingController != null) _handTrackingController.HandsVisible = true;

      // 最終決戦（勇者との連打勝負）は行わず、ミニゲーム終了後は直接エンディングへ進む。
      CurrentState = GameState.Ending;
      if (_endingController != null)
      {
        var summary = $"防衛成功:{_defenseSuccessCount} ゲームオーバー:{_gameOverCount}";
        var hired = _recruitmentController != null ? _recruitmentController.HiredCharacters : new List<CharacterData>();
        yield return _endingController.RunAsync(summary, hired, _genreScores);
      }
    }

    private IEnumerable<RecruitmentGenre> SelectGenresForThisRun()
    {
      if (_settings == null || _settings.availableGenres == null || _settings.availableGenres.Count == 0)
      {
        yield break;
      }

      // availableGenresから重複無しでランダムにrecruitmentCycleCount個選ぶ。
      var pool = new List<RecruitmentGenre>(_settings.availableGenres);
      var count = Mathf.Min(_settings.recruitmentCycleCount, pool.Count);
      var rng = new System.Random();

      for (var i = 0; i < count; i++)
      {
        var index = rng.Next(pool.Count);
        yield return pool[index];
        pool.RemoveAt(index);
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
