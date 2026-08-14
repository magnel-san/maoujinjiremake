namespace DemonLordHR.HandTracking
{
  /// <summary>
  /// 仕様書1.2で定義される全ジェスチャー種別。
  /// </summary>
  public enum GestureType
  {
    /// <summary>両手で親指と人差し指で輪をつくる → 履歴書の別ページ表示</summary>
    HoopBothHands,
    /// <summary>頭の上で大きな丸をつくる → 採用の意思表示</summary>
    BigCircleOverhead,
    /// <summary>右手をグーで思いっきり突き出す → ハンコ／投げる／決定パンチ</summary>
    RightFistPunchOut,
    /// <summary>胸の前で腕をクロス → 不採用の意思表示</summary>
    ArmsCross,
    /// <summary>拍手のように両手を胸の前で近づけ離す → 履歴書を丸める</summary>
    ClapNarrow,
    /// <summary>手で横に払う → 【遊泳】攻撃</summary>
    SwipeSideways,
    /// <summary>腕を翼のように上下に振る → 【飛行】操作</summary>
    WingFlap,
    /// <summary>両腕を振る → 【俊足】前進</summary>
    ArmSwingBoth,
    /// <summary>腕を振り下ろす（ハンマー打ち） → 【労働】ゲージ蓄積</summary>
    HammerSwingDown,
    /// <summary>両拳を交互に突き出す（パンチ） → 【戦闘】攻撃／最終決戦連打</summary>
    AlternatingPunch,
    /// <summary>右から左に腕を振る → 【耐寒】左移動</summary>
    SwipeRightToLeft,
    /// <summary>左から右に腕を振る → 【耐寒】右移動</summary>
    SwipeLeftToRight,
    /// <summary>胸の前でぐるぐるバルブを回す → 【耐熱】マグマ放出</summary>
    ValveSpin,
    /// <summary>両手を前で合わせる → 【知性】魔法陣起動</summary>
    HandsTogether,
  }
}
