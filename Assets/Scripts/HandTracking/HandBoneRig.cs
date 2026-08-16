using System;
using UnityEngine;

namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 手首をrootボーンとし、そこから5本指に枝分かれ、各指はボーン4本の階層構造を持つ
  /// 手モデルのボーン参照をまとめるコンポーネント。
  /// 左手モデルにアタッチして使う。右手は本コンポーネントごとミラーリング生成する。
  /// </summary>
  public class HandBoneRig : MonoBehaviour
  {
    [Serializable]
    public class Finger
    {
      [Tooltip("付け根から指先へ向かって4本のボーン")]
      public Transform[] bones = new Transform[4];
    }

    [Header("Root")]
    [Tooltip("MediaPipeランドマーク0番（手首）に対応するボーン")]
    public Transform wristRoot;

    [Header("指（各4ボーン、付け根→指先）")]
    public Finger thumb = new Finger();
    public Finger index = new Finger();
    public Finger middle = new Finger();
    public Finger ring = new Finger();
    public Finger pinky = new Finger();

    public bool isRightHand;

    [Header("軸の向き設定（回転が変な方向に見える場合はここを調整）")]
    [Tooltip("各ボーンの『指が伸びる方向』に相当するローカル軸。モデル作成時のボーンの向きに合わせる。" +
      "指の曲げ伸ばしが上下逆になる実機確認結果を踏まえてVector3.downをデフォルトにしている。")]
    public Vector3 boneLocalForwardAxis = Vector3.down;
    [Tooltip("手首ボーンの『手のひらが向く方向』に相当するローカル軸。手首自体のひねり・傾きの基準に使う。")]
    public Vector3 wristLocalPalmAxis = Vector3.forward;
    [Tooltip("手のひらの向きの基準符号を反転する。ほぼ180°捻れて見える場合に切り替える。")]
    public bool invertPalmDirection;

    [Header("前腕（任意）")]
    [Tooltip("手首より手前（肘側）の前腕ボーン。MediaPipeのHandLandmarkerは肘の情報を持たないため、" +
      "本当に肘で曲げることはできない。代わりに、位置は手首にしっかり追従させつつ回転だけ" +
      "手首より遅らせて追従させることで、手首の部分で曲がっているように見せる演出に使う。" +
      "未設定でも他の機能には影響しない。")]
    public Transform forearmBone;
    [Tooltip("前腕ボーンの位置を手首からどれだけずらすか（ワールド空間オフセット）。" +
      "前腕の見た目が手首から離れて見える/めり込む場合に調整する。")]
    public Vector3 forearmPositionOffset;

    /// <summary>人差し指の指先ボーン。ポインターのレイキャスト起点に使う。</summary>
    public Transform IndexTip => index.bones != null && index.bones.Length == 4 ? index.bones[3] : null;

    public Finger GetFinger(int fingerIndex)
    {
      switch (fingerIndex)
      {
        case 0: return thumb;
        case 1: return index;
        case 2: return middle;
        case 3: return ring;
        case 4: return pinky;
        default: throw new ArgumentOutOfRangeException(nameof(fingerIndex));
      }
    }
  }
}
