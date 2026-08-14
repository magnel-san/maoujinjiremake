using UnityEngine;

namespace DemonLordHR.Core
{
  /// <summary>
  /// 採用候補キャラクター1体分のデータ。
  /// </summary>
  [CreateAssetMenu(fileName = "CharacterData", menuName = "DemonLordHR/Character Data")]
  public class CharacterData : ScriptableObject
  {
    [Header("基本情報")]
    public string characterName;
    public RecruitmentGenre genre;
    public GameObject characterPrefab;

    [Header("採用試験")]
    [Tooltip("採用時に資金から減算される給料")]
    public int salary;
    [Tooltip("各ミニゲームのスコア/ダメージ計算に使う攻撃力")]
    public float attackPower;

    [Header("履歴書")]
    [Tooltip("履歴書画像。0番目が1ページ目、1番目が2ページ目（輪っかポーズで表示）")]
    public Sprite[] resumePages;

    [Header("エンディング")]
    [Tooltip("AI画像生成プロンプトに合成する、このキャラ専用のプロンプト断片")]
    [TextArea]
    public string aiPromptFragment;
  }
}
