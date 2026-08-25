using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonLordHR.Core
{
  /// <summary>
  /// 「魔王の人事」の全インスペクタ外だしパラメータを一元管理するScriptableObject。
  /// 仕様書セクション7のチェックリストに対応。
  /// </summary>
  [CreateAssetMenu(fileName = "GameSettings", menuName = "DemonLordHR/Game Settings")]
  public class GameSettings : ScriptableObject
  {
    [Header("共通UI")]
    [Tooltip("円状ポインター合わせゲージの標準保持秒数")]
    public float defaultHoldSeconds = 3f;
    [Tooltip("「面接終了する」ボタンの保持秒数")]
    public float endInterviewHoldSeconds = 5f;
    [Tooltip("デバッグ用：円状ボタンをクリックでも発火可能にする")]
    public bool debugClickEnabled = true;

    [Header("ゲーム全体フロー")]
    [Tooltip("ルール説明UI表示後、「準備完了」を保持する秒数")]
    public float readyHoldSeconds = 3f;
    [Tooltip("実施する採用試験の回数")]
    public int recruitmentCycleCount = 3;
    [Tooltip("「勇者襲来」UI表示秒数")]
    public float heroIncomingDisplaySeconds = 3f;
    [Tooltip("「{ジャンル名}の採用試験を開始する」UI表示秒数")]
    public float genreStartDisplaySeconds = 3f;

    [Header("採用試験フェーズ")]
    [Tooltip("ジャンル開始時に得られる資金")]
    public int recruitmentPhaseFunds = 100;
    [Tooltip("履歴書にポインターを合わせてから表示するまでの秒数")]
    public float resumeViewHoldSeconds = 3f;
    [Tooltip("履歴書「もどる」ボタンの保持秒数")]
    public float resumeBackHoldSeconds = 3f;
    [Tooltip("採用スタンプを押した後、履歴書を表示したままにする秒数（この後、自動的に閉じて次の候補へ）")]
    public float hireStampDisplaySeconds = 1.5f;
    [Tooltip("不採用キャラの吹き飛び威力（共通デフォルト）")]
    public float rejectKnockbackForce = 8f;
    [Tooltip("不採用キャラの吹き飛び方向（ローカル、共通デフォルト）")]
    public Vector3 rejectKnockbackDirection = new Vector3(0f, 0.3f, -1f);
    [Tooltip("採用キャラの整列開始座標")]
    public Vector3 hiredLineupOrigin = Vector3.zero;
    [Tooltip("採用キャラが並ぶ軸・向き")]
    public LineupAxis hiredLineupAxis = LineupAxis.PositiveX;
    [Tooltip("採用キャラの整列間隔")]
    public float hiredLineupSpacing = 2f;
    [Tooltip("採用キャラが整列位置まで移動する演出の所要時間")]
    public float hiredLineupMoveDuration = 1.5f;
    [Tooltip("不採用キャラが吹き飛ぶ演出の所要時間")]
    public float rejectKnockbackDuration = 0.6f;

    [Header("キャラクター入室")]
    [Tooltip("全員の入室完了までの所要時間")]
    public float characterEntryDuration = 4f;

    [Header("ミニゲーム共通")]
    [Tooltip("各ミニゲームの制限時間（既定値。ジャンルごとに上書き可）")]
    public float defaultMinigameTimeLimit = 60f;
    [Tooltip("ミニゲーム終了時、結果（防衛成功/ゲームオーバー＋スコア）を表示しておく秒数")]
    public float resultDisplaySeconds = 3f;

    [Header("最終決戦")]
    public float finalBattleMaxHp = 1000f;
    [Tooltip("パンチ1回あたりのダメージ")]
    public float finalBattlePunchDamage = 20f;
    [Tooltip("フェードアウトにかける秒数")]
    public float finalBattleFadeOutSeconds = 2f;

    [Header("エンディング")]
    public float endingHoldSeconds = 3f;

    [Header("実施ジャンル選択")]
    [Tooltip("設定画面から選べる採用試験ジャンルの一覧")]
    public List<RecruitmentGenre> availableGenres = new List<RecruitmentGenre>
    {
      RecruitmentGenre.Swim, RecruitmentGenre.Flight, RecruitmentGenre.Sprint,
      RecruitmentGenre.Labor, RecruitmentGenre.Scout, RecruitmentGenre.Combat,
      RecruitmentGenre.Intelligence, RecruitmentGenre.ColdResist, RecruitmentGenre.HeatResist,
    };

    [Header("キャラクターデータ")]
    public List<CharacterData> allCharacters = new List<CharacterData>();

    [Header("ジャンル開始表示の文章")]
    [Tooltip("採用試験開始時（候補キャラ入室前）に表示する文章を、ジャンルごとにInspectorで設定できる。" +
      "ここに設定が無いジャンルは「{ジャンル名}の採用試験を開始する」を既定文として使う。")]
    public List<GenreStartMessage> genreStartMessages = new List<GenreStartMessage>();

    /// <summary>ジャンル開始表示に使う文章を取得する。genreStartMessagesに個別設定があればそれを使い、
    /// 無ければ「{ジャンル名}の採用試験を開始する」を返す。</summary>
    public string GetGenreStartMessage(RecruitmentGenre genre)
    {
      foreach (var entry in genreStartMessages)
      {
        if (entry.genre == genre && !string.IsNullOrEmpty(entry.message)) return entry.message;
      }
      return $"{genre.ToDisplayName()}の採用試験を開始する";
    }
  }

  [Serializable]
  public class GenreStartMessage
  {
    public RecruitmentGenre genre;
    [Tooltip("このジャンルの採用試験開始時に表示する文章。空なら既定文（{ジャンル名}の採用試験を開始する）を使う。")]
    public string message;
  }

  /// <summary>採用試験／ミニゲームのジャンル。</summary>
  public enum RecruitmentGenre
  {
    Swim,
    Flight,
    Sprint,
    Labor,
    Scout,
    Combat,
    Intelligence,
    ColdResist,
    HeatResist,
  }

  public static class RecruitmentGenreExtensions
  {
    /// <summary>ジャンル開始表示（「{ジャンル名}の採用試験を開始する」）等、プレイヤーに見せる日本語名。</summary>
    public static string ToDisplayName(this RecruitmentGenre genre)
    {
      switch (genre)
      {
        case RecruitmentGenre.Swim: return "遊泳";
        case RecruitmentGenre.Flight: return "飛行";
        case RecruitmentGenre.Sprint: return "俊足";
        case RecruitmentGenre.Labor: return "労働";
        case RecruitmentGenre.Scout: return "偵察";
        case RecruitmentGenre.Combat: return "戦闘";
        case RecruitmentGenre.Intelligence: return "知性";
        case RecruitmentGenre.ColdResist: return "耐寒";
        case RecruitmentGenre.HeatResist: return "耐熱";
        default: return genre.ToString();
      }
    }
  }

  /// <summary>採用キャラの整列方向。ワールド座標軸のどちらへ並べるかを選ぶ。</summary>
  public enum LineupAxis
  {
    PositiveX,
    NegativeX,
    PositiveY,
    NegativeY,
    PositiveZ,
    NegativeZ,
  }

  public static class LineupAxisExtensions
  {
    public static Vector3 ToDirection(this LineupAxis axis)
    {
      switch (axis)
      {
        case LineupAxis.PositiveX: return Vector3.right;
        case LineupAxis.NegativeX: return Vector3.left;
        case LineupAxis.PositiveY: return Vector3.up;
        case LineupAxis.NegativeY: return Vector3.down;
        case LineupAxis.PositiveZ: return Vector3.forward;
        case LineupAxis.NegativeZ: return Vector3.back;
        default: return Vector3.right;
      }
    }
  }
}
