using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DemonLordHR.UI
{
  /// <summary>
  /// <see cref="ResumePoseRecognizer"/>が今どのポーズを認識しているか（4状態：なし／不採用／採用／裏を見る）と、
  /// 2秒保持の進捗を常に表示する。<see cref="CircularHoldButton"/>と同じく、Update()で毎フレーム
  /// 現在値をポーリングして表示を更新するだけのシンプルな作りにしている。
  /// </summary>
  public class ResumePoseStatusUI : MonoBehaviour
  {
    [SerializeField] private ResumePoseRecognizer _resumePoseRecognizer;
    [Tooltip("現在認識中のポーズ名を表示するテキスト")]
    [SerializeField] private TMP_Text _stateText;
    [Tooltip("保持の進捗を表示するゲージ（Image.fillAmountをType=Radial360等で使用、任意）")]
    [SerializeField] private Image _progressGauge;

    [Header("表示文言")]
    [SerializeField] private string _noneLabel = "認識中: なし";
    [SerializeField] private string _rejectLabel = "認識中: 不採用（腕クロス）";
    [SerializeField] private string _hireLabel = "認識中: 採用（両手で輪）";
    [SerializeField] private string _flipPageLabel = "認識中: 裏を見る（輪っか）";

    private void Update()
    {
      if (_resumePoseRecognizer == null) return;

      if (_stateText != null)
      {
        _stateText.text = _resumePoseRecognizer.CurrentPose switch
        {
          ResumePose.Reject => _rejectLabel,
          ResumePose.Hire => _hireLabel,
          ResumePose.FlipPage => _flipPageLabel,
          _ => _noneLabel,
        };
      }

      if (_progressGauge != null)
      {
        _progressGauge.fillAmount = _resumePoseRecognizer.CurrentPose == ResumePose.None
          ? 0f
          : _resumePoseRecognizer.HoldProgress01;
      }
    }
  }
}
