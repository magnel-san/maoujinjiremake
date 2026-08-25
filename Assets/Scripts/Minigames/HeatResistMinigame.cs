using System.Collections.Generic;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【耐熱】仕様4.9：ドーナツ状のバルブUIを表示し、手の先（人差し指の先端）でその円周をなぞって
  /// 実際に回すとマグマを放出する。
  ///
  /// 以前は「胸の前でぐるぐる回す」動きを手首の速度ベクトルの向きの変化から推定していたが、
  /// 手が画面外に出やすい・回転方向の推定がノイズに弱いという弱点があった。
  /// 代わりに<see cref="PointerController"/>が計算する「指先が指しているワールド座標」を
  /// <see cref="donutCenter"/>を中心とした角度に変換し、実際に円周上を動いた角度を直接積算する方式にした
  /// （手の先は常にドーナツの円周上付近を動くはずなので、位置そのものを読む方が自然かつ頑健）。
  /// </summary>
  public class HeatResistMinigame : MinigameBase
  {
    [Header("ドーナツ型バルブUI")]
    [Tooltip("ドーナツの中心。指先のワールド座標をこの中心からの角度に変換する（このTransformのXY平面を円盤面とみなす）")]
    [SerializeField] private Transform donutCenter;
    [Tooltip("指先の位置に追従して円周上を動くハンドル（カーソル代わりの見た目、任意）")]
    [SerializeField] private Transform valveHandle;
    [Tooltip("画面中央に固定表示する、実際に回転するバルブ本体の見た目。donutCenterの子にして、" +
      "回転させたい軸(donutCenterのZ軸＝正面)まわりの回転だけを持つ空のピボットを指定する" +
      "（見た目のメッシュ自体はさらにその子にして、向き合わせの固定回転だけを持たせる）。" +
      "指先の角度計算とは独立した見た目専用のフィードバックで、判定には影響しない。")]
    [SerializeField] private Transform valveWheelVisual;
    [Tooltip("ハンドルをドーナツの円周からどれだけの距離に置くか")]
    [SerializeField] private float donutRadius = 1f;
    [Tooltip("これだけ回したら1回分（マグマ放出1回）とみなす累積角度（度）")]
    [SerializeField] private float degreesPerFire = 360f;
    [Tooltip("ポインターが有効でない（指先が検出できていない）場合の回転追跡のリセット猶予")]
    [SerializeField] private PointerController pointerController;

    [Header("カーソル")]
    [Tooltip("このミニゲーム中だけ使うバルブハンドル型カーソル。未設定ならデフォルトのままにする。")]
    [SerializeField] private GameObject customPointerVisualPrefab;

    [Header("マグマオブジェクト")]
    [Tooltip("バルブを1周回すたびに召喚する小さいマグマオブジェクト")]
    [SerializeField] private GameObject magmaObjectPrefab;
    [Tooltip("magmaPerPack個たまった時に、それらの代わりに置く「まとまった」マグマオブジェクト")]
    [SerializeField] private GameObject magmaPackPrefab;
    [SerializeField] private Transform magmaBoxCenter;
    [SerializeField] private float magmaBoxRadius = 1f;
    [SerializeField] private int magmaPerPack = 10;
    [Tooltip("まとまったマグマ(magmaPackPrefab)を積み重ねる際、1個あたり積み上げるY方向の高さ。" +
      "プレハブ自体の実際の厚みに合わせないと、めり込んだり隙間が空いたりする。")]
    [SerializeField] private float magmaPackStackHeight = 0.48f;

    [Header("背景")]
    [Tooltip("まとめた回数（パック数）に応じて切り替える背景")]
    [SerializeField] private Renderer backgroundRenderer;
    [SerializeField] private Material[] backgroundStages;

    [Header("UI")]
    [SerializeField] private TMP_Text magmaGaugeText;

    [Tooltip("バルブ1周あたりのスコア加算＝合計攻撃力×この係数。他ジャンルとスコア水準を揃えるための調整値" +
      "（詳細はSwimMinigame.scoreMultiplierのコメント参照）。")]
    [SerializeField] private float scoreMultiplier = 0.0333f;

    [Header("デバッグ")]
    [Tooltip("ONの間、バルブの回転が反応しない原因切り分け用に一定間隔でコンソールへ状態を出す。")]
    [SerializeField] private bool _debugLogValveState;
    [SerializeField] private float _debugLogInterval = 0.5f;
    private float _debugLogTimer;

    public float TotalMagma { get; private set; }

    private readonly List<GameObject> _pendingMagmaObjects = new List<GameObject>();
    private int _packCount;

    private bool _isTracking;
    private bool _hasPrevAngle;
    private float _prevAngle;
    private float _accumulatedDegrees;

    protected override void OnRulesShown()
    {
      _isTracking = true;
      ResetTracking();
      if (pointerController != null) pointerController.SetPointerVisual(customPointerVisualPrefab);
    }

    protected override void OnRulesHidden()
    {
      _isTracking = false;
    }

    protected override void OnMinigameStart()
    {
      TotalMagma = 0f;
      _packCount = 0;
      ClearPendingMagma();
      ResetTracking();
      _isTracking = true;
      UpdateGaugeText();
      UpdateBackground();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      UpdateValveTracking(isPractice: false);
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      _isTracking = false;
      ClearPendingMagma();
      if (pointerController != null) pointerController.ResetPointerVisual();
    }

    private void Update()
    {
      // 練習中（本番タイマー開始前）は、ここでだけ回転を追跡する。
      // OnMinigameTickは本番の時間カウント中しか呼ばれないため。
      if (_isTracking && !IsRunning)
      {
        UpdateValveTracking(isPractice: true);
      }
    }

    protected override void OnGestureForMinigame(GestureType type) { } // このミニゲームはポインター位置で進行するためジェスチャーは使わない

    /// <summary>指先のワールド座標をdonutCenter基準の角度に変換し、実際に動いた角度を積算する。
    /// 一定角度分回ったら1回分としてfireCallbackを呼び、ハンドルの見た目も円周上へ追従させる。</summary>
    private void UpdateValveTracking(bool isPractice)
    {
      if (pointerController == null || donutCenter == null || !pointerController.IsPointerActive)
      {
        _hasPrevAngle = false;
        DebugLogValveState(reachedAngleCalc: false, Vector2.zero, 0f);
        return;
      }

      var local = donutCenter.InverseTransformPoint(pointerController.PointerWorldPosition);
      var localXY = new Vector2(local.x, local.y);
      if (localXY.sqrMagnitude < 1e-6f)
      {
        DebugLogValveState(reachedAngleCalc: false, localXY, 0f);
        return; // 中心ぴったりだと角度が定まらないので無視する
      }

      if (valveHandle != null)
      {
        var direction = localXY.normalized;
        valveHandle.position = donutCenter.TransformPoint(direction * donutRadius);
      }

      var angle = Mathf.Atan2(localXY.y, localXY.x) * Mathf.Rad2Deg;

      // バルブ本体は画面中央に固定のまま、指先が今どの角度にいるかへ常に向き直す
      // （＝実際に指で回している向きへバルブ自体が回転して見える）。
      if (valveWheelVisual != null) valveWheelVisual.localRotation = Quaternion.AngleAxis(angle, Vector3.forward);

      DebugLogValveState(reachedAngleCalc: true, localXY, angle);

      if (!_hasPrevAngle)
      {
        _prevAngle = angle;
        _hasPrevAngle = true;
        return;
      }

      _accumulatedDegrees += Mathf.Abs(Mathf.DeltaAngle(_prevAngle, angle));
      _prevAngle = angle;

      if (_accumulatedDegrees >= Mathf.Max(degreesPerFire, 10f))
      {
        _accumulatedDegrees -= Mathf.Max(degreesPerFire, 10f);
        if (isPractice) SpawnPracticeMagma();
        else RegisterValveTurn();
      }
    }

    /// <summary>バルブが反応しない原因を実機で切り分けるための診断ログ。
    /// ポインター自体が無効なのか、中心ぴったりで無視されているだけなのか、
    /// 角度計算まで届いているのに見た目に反映されていないだけなのかを判別できるようにする。</summary>
    private void DebugLogValveState(bool reachedAngleCalc, Vector2 localXY, float angle)
    {
      if (!_debugLogValveState) return;
      _debugLogTimer -= Time.deltaTime;
      if (_debugLogTimer > 0f) return;
      _debugLogTimer = Mathf.Max(_debugLogInterval, 0.05f);

      var pointerActive = pointerController != null && pointerController.IsPointerActive;
      var pointerPos = pointerController != null ? pointerController.PointerWorldPosition.ToString("F2") : "N/A";

      Debug.Log($"[HeatResistDebug] pointerController={(pointerController != null)} donutCenter={(donutCenter != null)} " +
        $"IsPointerActive={pointerActive} PointerWorldPosition={pointerPos} reachedAngleCalc={reachedAngleCalc} " +
        $"localXY={localXY:F3} angle={angle:F1} valveWheelVisual={(valveWheelVisual != null)}");
    }

    private void ResetTracking()
    {
      _hasPrevAngle = false;
      _accumulatedDegrees = 0f;
    }

    private void RegisterValveTurn()
    {
      TotalMagma += totalAttackPower * scoreMultiplier;
      SpawnMagmaObject();
      UpdateGaugeText();
    }

    protected override float GetFinalScore() => TotalMagma;

    /// <summary>練習中：バルブ回しの動作自体は試せるが、マグマは実際には蓄積されない。</summary>
    private void SpawnPracticeMagma()
    {
      if (magmaObjectPrefab == null || magmaBoxCenter == null) return;

      var offset = Random.insideUnitSphere * magmaBoxRadius;
      offset.y = Mathf.Abs(offset.y);
      var instance = Instantiate(magmaObjectPrefab, magmaBoxCenter.position + offset, Quaternion.identity);
      Destroy(instance, 1f); // 練習用の一時的な見た目なのですぐ消す
    }

    private void SpawnMagmaObject()
    {
      if (magmaObjectPrefab == null || magmaBoxCenter == null) return;

      var offset = Random.insideUnitSphere * magmaBoxRadius;
      offset.y = Mathf.Abs(offset.y); // 箱の底より上、積み上がるイメージ
      var instance = Instantiate(magmaObjectPrefab, magmaBoxCenter.position + offset, Quaternion.identity);
      _pendingMagmaObjects.Add(instance);

      if (_pendingMagmaObjects.Count >= magmaPerPack)
      {
        ConsumeIntoPack();
      }
    }

    /// <summary>小さいマグマオブジェクトがmagmaPerPack個たまったら、まとめて1つの「まとまった」表現に置き換える。
    /// 大量のオブジェクトを個別に残し続けると重くなるための対策。まとまった表現はmagmaBoxCenterの
    /// 真上へ積み重ねていき、パックが増えるたびに高さが伸びていくようにする。</summary>
    private void ConsumeIntoPack()
    {
      foreach (var obj in _pendingMagmaObjects)
      {
        if (obj != null) Destroy(obj);
      }
      _pendingMagmaObjects.Clear();

      _packCount++;
      UpdateBackground();

      if (magmaPackPrefab != null && magmaBoxCenter != null)
      {
        var stackedPosition = magmaBoxCenter.position + Vector3.up * (magmaPackStackHeight * (_packCount - 1));
        Instantiate(magmaPackPrefab, stackedPosition, magmaBoxCenter.rotation, magmaBoxCenter);
      }
    }

    private void ClearPendingMagma()
    {
      foreach (var obj in _pendingMagmaObjects)
      {
        if (obj != null) Destroy(obj);
      }
      _pendingMagmaObjects.Clear();
    }

    private void UpdateBackground()
    {
      if (backgroundRenderer == null || backgroundStages == null || backgroundStages.Length == 0) return;
      var index = Mathf.Min(_packCount, backgroundStages.Length - 1);
      backgroundRenderer.material = backgroundStages[index];
    }

    private void UpdateGaugeText()
    {
      if (magmaGaugeText != null) magmaGaugeText.text = $"マグマ量: {TotalMagma:0}（{_pendingMagmaObjects.Count}/{magmaPerPack}）";
    }
  }
}
