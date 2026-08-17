using System.Collections;
using System.Collections.Generic;
using System.Text;
using DemonLordHR.Core;
using DemonLordHR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.Ending
{
  /// <summary>
  /// エンディング演出（仕様書6章）：AI画像生成による魔王城の新しい姿の表示、記念撮影演出、
  /// 「終了する」ボタンでタイトルへ戻る。
  /// </summary>
  public class EndingController : MonoBehaviour
  {
    [SerializeField] private GameSettings _settings;
    [SerializeField] private CircularHoldButton _endButton;
    [SerializeField] private RawImage _generatedStageImage;
    [SerializeField] private GameObject _commemorativePhotoRoot;

    private IStageImageGenerator _imageGenerator = new NullStageImageGenerator();

    private void Awake()
    {
      if (_endButton != null) _endButton.gameObject.SetActive(false);
      _commemorativePhotoRoot?.SetActive(false);
    }

    public void SetImageGenerator(IStageImageGenerator generator)
    {
      _imageGenerator = generator ?? new NullStageImageGenerator();
    }

    public IEnumerator RunAsync(string minigameResultSummary, IReadOnlyList<CharacterData> hiredCharacters)
    {
      var prompt = BuildPrompt(minigameResultSummary, hiredCharacters);

      Texture2D generated = null;
      yield return _imageGenerator.GenerateAsync(prompt, tex => generated = tex);

      if (generated != null && _generatedStageImage != null)
      {
        _generatedStageImage.texture = generated;
      }

      // 記念撮影演出
      _commemorativePhotoRoot?.SetActive(true);
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
  }
}
