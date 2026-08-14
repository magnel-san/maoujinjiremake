using System;
using System.Collections;
using UnityEngine;

namespace DemonLordHR.Ending
{
  /// <summary>
  /// AI画像生成サービスを差し替え可能にするためのインターフェース。
  /// 実際の画像生成APIの選定・キー管理は別途方針を確認してから実装するため、
  /// ここでは呼び出し口だけを定義する（<see cref="NullStageImageGenerator"/>が既定のダミー実装）。
  /// </summary>
  public interface IStageImageGenerator
  {
    /// <summary>promptを渡して画像生成を行い、完了時にonCompleteへ結果を渡す。</summary>
    IEnumerator GenerateAsync(string prompt, Action<Texture2D> onComplete);
  }

  /// <summary>実際のAPI接続が未実装の間に使うダミー実装。何も生成せずnullを返す。</summary>
  public class NullStageImageGenerator : IStageImageGenerator
  {
    public IEnumerator GenerateAsync(string prompt, Action<Texture2D> onComplete)
    {
      Debug.LogWarning($"[NullStageImageGenerator] AI画像生成は未実装です。prompt: {prompt}");
      onComplete?.Invoke(null);
      yield break;
    }
  }
}
