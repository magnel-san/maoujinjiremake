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
