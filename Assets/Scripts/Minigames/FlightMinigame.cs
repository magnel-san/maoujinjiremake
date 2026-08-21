using System.Collections.Generic;
using DemonLordHR.Core;
using DemonLordHR.HandTracking;
using TMPro;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// 【飛行】仕様4.2：「腕を翼のように上下に振る」で操作するTPSフラッピーバード風ミニゲーム。
  /// 他のジャンルと違い、ライフ制ではなく「時間内に何本の障害物を通過できたか」がそのままスコアになる。
  /// 墜落しても即ゲームオーバーにはせず、開始時と同じ3秒の猶予（重力OFF）を挟んで何度でも再開できる
  /// （編成攻撃力は復活回数ではなく、通過1本ごとのスコアの重みに使う）。
  ///
  /// 障害物（土管＋通過判定）とパイロットの当たり判定は、プレハブを一切使わずこのスクリプトが
  /// 実行時に全てプリミティブから生成する。
  /// ・パイロットの当たり判定は<see cref="pilotColliderRadius"/>の球コライダーで統一し、
  ///   採用キャラの見た目（プレハブの実サイズはキャラごとにバラバラ）はその球にちょうど収まるよう
  ///   自動的に拡大縮小する。当たり判定の球自体も半透明で表示するので、プレイヤーは自分の実際の
  ///   当たり判定の大きさを見ながら避けられる。
  /// ・障害物の隙間の大きさは「パイロットの当たり判定の直径 + <see cref="gapMargin"/>」で自動計算する
  ///   ため、手動でgapHeight等の数値をプレハブの見た目に合わせ込む必要がない。
  /// ・障害物の進行方向はobstacleSpawnPointからpilotSpawnPointへ向かうベクトルから求めるため、
  ///   ステージがどの向きを向いていても正しく機能する。
  /// </summary>
  public class FlightMinigame : MinigameBase
  {
    private enum FlightPhase { Idle, Practice, Countdown, Playing }

    [Header("パイロット")]
    [Tooltip("パイロット（実際に飛ばすキャラ）を配置する位置・向き")]
    [SerializeField] private Transform pilotSpawnPoint;
    [Tooltip("1回の羽ばたきで得る上昇速度(m/s)")]
    [SerializeField] private float flapLift = 4f;
    [SerializeField] private float gravity = 9f;
    [SerializeField] private float minY = -3f;
    [SerializeField] private float maxY = 3f;

    [Header("パイロットの当たり判定（球）")]
    [Tooltip("パイロットの当たり判定として使う球コライダーの半径。採用キャラの見た目はこの半径に" +
      "ちょうど収まるよう自動的に拡大縮小される（キャラごとのサイズ差を吸収する）。")]
    [SerializeField] private float pilotColliderRadius = 0.6f;
    [Tooltip("当たり判定の球を可視化する半透明マテリアル。未設定でも既定マテリアルで表示はされる。")]
    [SerializeField] private Material pilotColliderVisualMaterial;

    [Header("その他の採用キャラの整列")]
    [Tooltip("複数採用されている場合、パイロット以外の残りのキャラをここから並べて配置する（見送り役として待機させる想定）")]
    [SerializeField] private Transform remainingLineupOrigin;
    [SerializeField] private LineupAxis remainingLineupAxis = LineupAxis.PositiveX;
    [SerializeField] private float remainingLineupSpacing = 2f;

    [Header("開始/墜落後の待機")]
    [Tooltip("ゲーム開始時・墜落からの再開時、重力OFFで準備できる秒数")]
    [SerializeField] private float startupSeconds = 3f;

    [Header("障害物（実行時にプリミティブから生成する）")]
    [Tooltip("障害物（土管）に使うマテリアル")]
    [SerializeField] private Material obstacleMaterial;
    [Tooltip("障害物が出現する位置（プレイヤーから見て奥）")]
    [SerializeField] private Transform obstacleSpawnPoint;
    [Tooltip("障害物がプレイヤーへ向かって進む速度(m/s)")]
    [SerializeField] private float obstacleSpeed = 5f;
    [SerializeField] private float obstacleSpawnInterval = 2f;
    [Tooltip("障害物の隙間（ゲート）の中心Yが取りうる範囲")]
    [SerializeField] private float gapCenterMinY = -1.5f;
    [SerializeField] private float gapCenterMaxY = 1.5f;
    [Tooltip("隙間の大きさ ＝ パイロットの当たり判定の直径 + この値。大きいほど余裕を持って通過できる。")]
    [SerializeField] private float gapMargin = 1f;
    [Tooltip("土管の横幅（X）と奥行き（Z）")]
    [SerializeField] private float pipeWidth = 2f;
    [SerializeField] private float pipeDepth = 1f;
    [Tooltip("土管がminY/maxYの範囲をどれだけ超えて伸びるか（上下に飛び越えられないようにする余白）")]
    [SerializeField] private float pipeOverhang = 6f;
    [Tooltip("何にも当たらないまま何秒進んだら自動的に消すか（保険）")]
    [SerializeField] private float obstacleLifetimeSeconds = 15f;

    [Header("UI")]
    [Tooltip("通過数・スコアを表示するテキスト")]
    [SerializeField] private TMP_Text obstaclesPassedText;

    private CharacterData _pilot;
    private GameObject _pilotInstance;
    private float _pilotVelocityY;
    private FlightPhase _phase = FlightPhase.Idle;
    private float _phaseTimer;
    private float _spawnTimer;

    private readonly List<FlightObstacle> _obstacles = new List<FlightObstacle>();
    private readonly List<GameObject> _remainingLineup = new List<GameObject>();

    public int ObstaclesPassed { get; private set; }
    public float Score { get; private set; }

    /// <summary>障害物が前進する方向（＝カメラ/パイロットに近づく向き）。obstacleSpawnPointから
    /// pilotSpawnPointへ向かうベクトルから求めるため、ステージの向きに関わらず正しく機能する。
    /// 高さ(Y)は障害物ごとにgapCenterYで個別に決まるため、進行方向自体は水平成分だけを使う
    /// （2点のYが完全に一致していないと斜めに進んでしまうため、Y差の影響を受けないようにする）。</summary>
    private Vector3 ObstacleForwardAxis
    {
      get
      {
        if (obstacleSpawnPoint == null || pilotSpawnPoint == null) return Vector3.back;
        var diff = pilotSpawnPoint.position - obstacleSpawnPoint.position;
        diff.y = 0f;
        return diff.sqrMagnitude > 1e-6f ? diff.normalized : Vector3.back;
      }
    }

    /// <summary>隙間の半径（中心から上端/下端までの距離）。直径がパイロット当たり判定の直径+gapMarginになる。</summary>
    private float GapRadius => pilotColliderRadius + gapMargin * 0.5f;

    // パイロット＋残りの整列を自前でスポーンするため、基底クラスの一括召喚は使わない
    // （両方動くとパイロットが二重に召喚されてしまう）。
    protected override bool SkipGenericCharacterSummon => true;

    private void Update()
    {
      // 練習中（本番タイマー開始前）は、ここでだけパイロットの物理を動かす。
      // OnMinigameTickは本番の時間カウント中しか呼ばれないため。
      if (_phase == FlightPhase.Practice)
      {
        UpdatePilotHeight(Time.deltaTime, applyGravity: true);
      }
    }

    protected override void OnRulesShown()
    {
      _pilot = PickRandomAssigned();
      SpawnPilot();
      RefreshRemainingLineup();
      _phase = FlightPhase.Practice;
    }

    protected override void OnRulesHidden()
    {
      _phase = FlightPhase.Idle;
    }

    protected override void OnMinigameStart()
    {
      ObstaclesPassed = 0;
      Score = 0f;
      UpdateScoreText();
      ClearObstacles();
      SpawnPilot();
      RefreshRemainingLineup();
      EnterCountdown();
    }

    protected override void OnMinigameTick(float deltaTime)
    {
      if (_phase == FlightPhase.Countdown)
      {
        _phaseTimer -= deltaTime;
        UpdatePilotHeight(deltaTime, applyGravity: false);
        if (_phaseTimer <= 0f)
        {
          _phase = FlightPhase.Playing;
          SetObstaclesMoving(true);
        }
        return;
      }

      if (_phase != FlightPhase.Playing) return;

      UpdatePilotHeight(deltaTime, applyGravity: true);

      _spawnTimer -= deltaTime;
      if (_spawnTimer <= 0f)
      {
        SpawnObstacle();
        _spawnTimer = Mathf.Max(obstacleSpawnInterval, 0.1f);
      }

      _obstacles.RemoveAll(o => o == null); // 保険のタイムアウト等で消えた分をここで反映する
    }

    protected override void OnMinigameEnd(MinigameResult finalResult)
    {
      ClearObstacles();
      if (_pilotInstance != null) Destroy(_pilotInstance);
      _pilotInstance = null;
      DespawnLineup(_remainingLineup);
      _phase = FlightPhase.Idle;
    }

    /// <summary>パイロット以外の採用キャラを整列し直す（前回の召喚が残っていれば片付けてから再召喚する）。</summary>
    private void RefreshRemainingLineup()
    {
      DespawnLineup(_remainingLineup);
      _remainingLineup.AddRange(SpawnRemainingLineup(_pilot, remainingLineupOrigin, remainingLineupAxis, remainingLineupSpacing));
    }

    protected override void OnGestureForMinigame(GestureType type)
    {
      if (type == GestureType.WingFlap) Flap();
    }

    /// <summary>ルール説明中の練習：何度でも羽ばたいて高さの感覚を確認できる。障害物は出さず、失敗もない。</summary>
    protected override void OnPracticeGesture(GestureType type)
    {
      if (type == GestureType.WingFlap) Flap();
    }

    private void Flap()
    {
      if (_pilotInstance == null) return;
      if (_phase != FlightPhase.Practice && _phase != FlightPhase.Playing) return;
      _pilotVelocityY = flapLift;
    }

    private void SpawnPilot()
    {
      if (_pilotInstance != null) Destroy(_pilotInstance);
      if (_pilot == null || _pilot.characterPrefab == null || pilotSpawnPoint == null) return;

      _pilotInstance = Instantiate(_pilot.characterPrefab, pilotSpawnPoint.position, pilotSpawnPoint.rotation);
      _pilotVelocityY = 0f;
      SetupPilotHitVolume(_pilotInstance);
    }

    /// <summary>キャラの見た目をpilotColliderRadiusの球にちょうど収まるよう拡大縮小し、
    /// 実際の当たり判定として球コライダーを追加、見た目としても半透明の球を表示する。
    /// 採用キャラのプレハブは原点(0,0,0)がY=0の床、そこからY+方向にキャラが立っている前提のため、
    /// 球は中心を浮かせず「最下点が足元(Y=0)に接する」位置に置く（球の中心で合わせるとキャラが
    /// 上にはみ出てしまうため）。</summary>
    private void SetupPilotHitVolume(GameObject instance)
    {
      var scale = ComputeFitScale(instance, pilotColliderRadius);
      if (scale <= 0f) scale = 1f;
      instance.transform.localScale *= scale;

      // 親（キャラ本体）の拡大縮小の影響を打ち消し、常にワールド基準でpilotColliderRadiusになるようにする。
      var hitVolume = new GameObject("HitVolume");
      hitVolume.transform.SetParent(instance.transform, false);
      var counterScale = 1f / scale;
      hitVolume.transform.localScale = Vector3.one * counterScale;
      // 床(Y=0、親のローカル原点)からpilotColliderRadius分だけ上に球の中心を置く
      // （親のスケールがかかる前の値で指定する必要があるため、counterScaleで打ち消しておく）。
      hitVolume.transform.localPosition = new Vector3(0f, pilotColliderRadius * counterScale, 0f);

      var collider = hitVolume.AddComponent<SphereCollider>();
      collider.isTrigger = true;
      collider.radius = pilotColliderRadius;

      var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      visual.name = "HitVolumeVisual";
      visual.transform.SetParent(hitVolume.transform, false);
      visual.transform.localScale = Vector3.one * (pilotColliderRadius * 2f); // 既定の球は半径0.5(直径1)基準
      Destroy(visual.GetComponent<Collider>()); // 見た目だけなので、生成時に付与される既定のコライダーは要らない

      if (pilotColliderVisualMaterial != null)
      {
        visual.GetComponent<Renderer>().sharedMaterial = pilotColliderVisualMaterial;
      }
    }

    /// <summary>
    /// instanceの全Rendererを包むワールド空間の境界を求め、それをinstanceのローカル空間（拡縮前）に変換した
    /// 8隅の座標から、「Y=0(足元)に最下点が接する半径targetRadiusの球」に全て収まる最大スケールを求める。
    /// 球の中心は(0, targetRadius, 0)なので、ローカル座標(x,y,z)がスケールsされた点(sx,sy,sz)について
    ///   (sx)^2 + (sy-targetRadius)^2 + (sz)^2 <= targetRadius^2
    /// を整理すると s <= 2*y*targetRadius / (x^2+y^2+z^2) （y>0の点のみ有効）。
    /// 全隅点でこれを満たす最大のsが答え。
    /// </summary>
    private static float ComputeFitScale(GameObject instance, float targetRadius)
    {
      if (targetRadius <= 0f) return 0f;

      var renderers = instance.GetComponentsInChildren<Renderer>();
      if (renderers.Length == 0) return 0f;

      var worldBounds = renderers[0].bounds;
      for (var i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

      var worldToLocal = instance.transform.worldToLocalMatrix; // この時点ではまだ拡縮前なのでプレハブ本来の単位が得られる
      var min = worldBounds.min;
      var max = worldBounds.max;

      var maxScale = float.MaxValue;
      var anyValid = false;

      for (var i = 0; i < 8; i++)
      {
        var corner = new Vector3(
          (i & 1) == 0 ? min.x : max.x,
          (i & 2) == 0 ? min.y : max.y,
          (i & 4) == 0 ? min.z : max.z);

        var local = worldToLocal.MultiplyPoint3x4(corner);
        if (local.y <= 0.0001f) continue; // 床にほぼ接している/めり込んでいる点はこの式では扱えないためスキップ

        var sqDist = local.x * local.x + local.y * local.y + local.z * local.z;
        if (sqDist <= 0.0001f) continue;

        var allowedScale = (2f * local.y * targetRadius) / sqDist;
        if (allowedScale < maxScale) maxScale = allowedScale;
        anyValid = true;
      }

      return anyValid ? maxScale : 0f;
    }

    /// <summary>開始時／墜落後の3秒待機に入る。重力を切ってパイロットを基準の高さへ戻し、
    /// 残っている障害物も一緒に止める（猶予中は障害物側も進まない）。</summary>
    private void EnterCountdown()
    {
      _phase = FlightPhase.Countdown;
      _phaseTimer = Mathf.Max(startupSeconds, 0f);
      _pilotVelocityY = 0f;
      SetObstaclesMoving(false);

      if (_pilotInstance != null && pilotSpawnPoint != null)
      {
        var pos = _pilotInstance.transform.position;
        pos.y = pilotSpawnPoint.position.y;
        _pilotInstance.transform.position = pos;
      }
    }

    private void UpdatePilotHeight(float deltaTime, bool applyGravity)
    {
      if (_pilotInstance == null) return;

      if (applyGravity) _pilotVelocityY -= gravity * deltaTime;
      var pos = _pilotInstance.transform.position;
      pos.y = Mathf.Clamp(pos.y + _pilotVelocityY * deltaTime, minY, maxY);
      _pilotInstance.transform.position = pos;
    }

    /// <summary>土管2本＋通過判定ゾーンを、プレハブを使わず全てプリミティブから生成する。</summary>
    private void SpawnObstacle()
    {
      if (obstacleSpawnPoint == null) return;

      var gapCenterY = Random.Range(gapCenterMinY, gapCenterMaxY);
      var spawnPos = obstacleSpawnPoint.position;
      spawnPos.y = gapCenterY;

      var root = new GameObject("FlightObstacle");
      root.transform.SetPositionAndRotation(spawnPos, obstacleSpawnPoint.rotation);

      var gapRadius = GapRadius;
      var pipeHeight = Mathf.Max(maxY - minY, 1f) + pipeOverhang * 2f;

      CreatePipeSegment(root.transform, "TopPipe", gapRadius + pipeHeight * 0.5f, pipeHeight);
      CreatePipeSegment(root.transform, "BottomPipe", -(gapRadius + pipeHeight * 0.5f), pipeHeight);
      CreatePassZone(root.transform, gapRadius);

      var rb = root.AddComponent<Rigidbody>();
      rb.isKinematic = true;
      rb.useGravity = false;

      var obstacle = root.AddComponent<FlightObstacle>();
      var pilotTransform = _pilotInstance != null ? _pilotInstance.transform : null;
      obstacle.Initialize(ObstacleForwardAxis, obstacleSpeed, obstacleLifetimeSeconds, pilotTransform,
        onHazardHit: HandleObstacleHazardHit, onPassed: HandleObstaclePassed);

      _obstacles.Add(obstacle);
    }

    private void CreatePipeSegment(Transform parent, string name, float localCenterY, float height)
    {
      var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
      go.name = name;
      go.transform.SetParent(parent, false);
      go.transform.localPosition = new Vector3(0f, localCenterY, 0f);
      go.transform.localRotation = Quaternion.identity;
      go.transform.localScale = new Vector3(pipeWidth, height, pipeDepth);

      var collider = go.GetComponent<BoxCollider>();
      collider.isTrigger = true;

      if (obstacleMaterial != null) go.GetComponent<Renderer>().sharedMaterial = obstacleMaterial;

      var zone = go.AddComponent<FlightObstacleZone>();
      zone.SetZoneType(FlightObstacleZone.ZoneType.Hazard);
    }

    private void CreatePassZone(Transform parent, float gapRadius)
    {
      var go = new GameObject("PassZone");
      go.transform.SetParent(parent, false);
      go.transform.localPosition = Vector3.zero;

      var collider = go.AddComponent<BoxCollider>();
      collider.isTrigger = true;
      collider.size = new Vector3(pipeWidth, gapRadius * 2f, pipeDepth);

      var zone = go.AddComponent<FlightObstacleZone>();
      zone.SetZoneType(FlightObstacleZone.ZoneType.PassTrigger);
    }

    /// <summary>土管等の当たり判定に触れた瞬間に呼ばれる。ライフ制ではないので即ゲームオーバーにはせず、
    /// 3秒の猶予を挟んで再開する。</summary>
    private void HandleObstacleHazardHit()
    {
      EnterCountdown();
    }

    /// <summary>隙間の中央にあるPassZoneを通過した瞬間に呼ばれる。</summary>
    private void HandleObstaclePassed()
    {
      ObstaclesPassed++;
      Score += totalAttackPower;
      UpdateScoreText();
    }

    private void SetObstaclesMoving(bool moving)
    {
      foreach (var obstacle in _obstacles)
      {
        if (obstacle != null) obstacle.IsMoving = moving;
      }
    }

    private void ClearObstacles()
    {
      foreach (var obstacle in _obstacles)
      {
        if (obstacle != null) Destroy(obstacle.gameObject);
      }
      _obstacles.Clear();
    }

    private void UpdateScoreText()
    {
      if (obstaclesPassedText != null) obstaclesPassedText.text = $"通過数: {ObstaclesPassed}（スコア {Score:0}）";
    }
  }
}
