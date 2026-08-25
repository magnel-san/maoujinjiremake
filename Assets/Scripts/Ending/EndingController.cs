using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DemonLordHR.Core;
using DemonLordHR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.Ending
{
  /// <summary>
  /// エンディング演出（仕様書6章）：AI画像生成による魔王城の新しい姿の表示、記念撮影演出、
  /// 全ジャンルの合計スコア表示、スコアに応じた結果画像の切り替え、「終了する」ボタンでタイトルへ戻る。
  /// </summary>
  public class EndingController : MonoBehaviour
  {
    [SerializeField] private GameSettings _settings;
    [SerializeField] private CircularHoldButton _endButton;
    [SerializeField] private RawImage _generatedStageImage;
    [SerializeField] private GameObject _commemorativePhotoRoot;

    [Header("スコア内訳表示")]
    [Tooltip("「合計スコア: 3000点（遊泳フェーズ1000点+飛行フェーズ1000点+労働フェーズ1000点）」のような" +
      "内訳込みの合計スコアを表示するテキスト。")]
    [SerializeField] private TMP_Text _scoreBreakdownText;

    [Header("スコアに応じた結果画像")]
    [Tooltip("合計スコアに応じて切り替える結果画像。「このスコア以上ならこの画像」という条件をここに" +
      "並べておくと、合計スコアを一番高いしきい値から順に判定し、最初に条件を満たしたものを表示する。")]
    [SerializeField] private Image _resultImage;
    [Tooltip("結果画像とあわせて表示する説明テキスト（例：「大成功！」等）。ResultImageThreshold側の" +
      "resultTextを表示する。未設定なら結果テキストの表示自体を行わない。")]
    [SerializeField] private TMP_Text _resultText;
    [SerializeField] private List<ResultImageThreshold> _resultImageThresholds = new List<ResultImageThreshold>();

    private IStageImageGenerator _imageGenerator = new NullStageImageGenerator();

    private void Awake()
    {
      if (_endButton != null) _endButton.gameObject.SetActive(false);
      if (_commemorativePhotoRoot != null) _commemorativePhotoRoot.SetActive(false);
      SetResultActive(false);
    }

    private void SetResultActive(bool active)
    {
      if (_resultImage != null) _resultImage.gameObject.SetActive(active);
      if (_resultText != null) _resultText.gameObject.SetActive(active);
    }

    public void SetImageGenerator(IStageImageGenerator generator)
    {
      _imageGenerator = generator ?? new NullStageImageGenerator();
    }

    public IEnumerator RunAsync(string minigameResultSummary, IReadOnlyList<CharacterData> hiredCharacters,
      IReadOnlyList<(RecruitmentGenre genre, float score)> genreScores = null)
    {
      var prompt = BuildPrompt(minigameResultSummary, hiredCharacters);

      Texture2D generated = null;
      yield return _imageGenerator.GenerateAsync(prompt, tex => generated = tex);

      if (generated != null && _generatedStageImage != null)
      {
        _generatedStageImage.texture = generated;
      }

      ShowScoreBreakdown(genreScores);

      // 記念撮影演出
      if (_commemorativePhotoRoot != null) _commemorativePhotoRoot.SetActive(true);
      // TODO: プレイヤー（の見た目 or カメラ枠）＋採用キャラクター一同を画面に並べる

      if (_endButton != null)
      {
        _endButton.HoldSeconds = _settings != null ? _settings.endingHoldSeconds : 3f;
        _endButton.gameObject.SetActive(true);
        var ended = false;
        System.Action onEnd = () => ended = true;
        _endButton.OnTriggered += onEnd;
        yield return new WaitUntil(() => ended);
        _endButton.OnTriggered -= onEnd;
        _endButton.gameObject.SetActive(false);
      }

      // 次のループ（タイトル〜最終決戦）の間、このエンディング専用の演出が残って見えてしまわないよう片付ける。
      if (_commemorativePhotoRoot != null) _commemorativePhotoRoot.SetActive(false);
      SetResultActive(false);
    }

    private string BuildPrompt(string minigameResultSummary, IReadOnlyList<CharacterData> hiredCharacters)
    {
      var sb = new StringBuilder();
      sb.Append("魔王城の新しい姿。");
      sb.Append(minigameResultSummary);
      if (hiredCharacters != null)
      {
        foreach (var c in hiredCharacters)
        {
          if (c == null) continue;
          sb.Append($" / {c.characterName}: {c.aiPromptFragment}");
        }
      }
      return sb.ToString();
    }

    /// <summary>「合計スコア: 3000点（遊泳フェーズ1000点+飛行フェーズ1000点+...）」の形式でテキストへ
    /// 反映し、合計スコアに応じた結果画像を選んで表示する。</summary>
    private void ShowScoreBreakdown(IReadOnlyList<(RecruitmentGenre genre, float score)> genreScores)
    {
      if (genreScores == null) return;

      var total = 0f;
      var terms = new List<string>();
      foreach (var entry in genreScores)
      {
        total += entry.score;
        terms.Add($"{entry.genre.ToDisplayName()}フェーズ{entry.score:0}点");
      }

      if (_scoreBreakdownText != null)
      {
        var breakdown = terms.Count > 0 ? $"（{string.Join("+", terms)}）" : "";
        _scoreBreakdownText.text = $"合計スコア: {total:0}点{breakdown}";
      }

      ApplyResultImage(total);
    }

    /// <summary>合計スコアが一番高いしきい値から順に判定し、最初に条件（合計スコア >= minScore）を
    /// 満たしたものを結果画像として表示する。</summary>
    private void ApplyResultImage(float totalScore)
    {
      if (_resultImage == null || _resultImageThresholds == null || _resultImageThresholds.Count == 0) return;

      ResultImageThreshold best = null;
      foreach (var entry in _resultImageThresholds)
      {
        if (entry == null || totalScore < entry.minScore) continue;
        if (best == null || entry.minScore > best.minScore) best = entry;
      }

      if (best != null && best.image != null)
      {
        _resultImage.sprite = best.image;
        _resultImage.gameObject.SetActive(true);
      }

      if (_resultText != null)
      {
        var hasText = best != null && !string.IsNullOrEmpty(best.resultText);
        if (hasText) _resultText.text = best.resultText;
        _resultText.gameObject.SetActive(hasText);
      }
    }
  }

  [System.Serializable]
  public class ResultImageThreshold
  {
    [Tooltip("合計スコアがこの値以上の場合にこの画像を使う。")]
    public float minScore;
    public Sprite image;
    [Tooltip("この結果画像とあわせて表示する説明テキスト（例：「大成功！魔王城は繁栄した」）。空なら表示しない。")]
    [TextArea]
    public string resultText;
  }
}
