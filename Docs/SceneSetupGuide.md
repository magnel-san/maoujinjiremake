# 魔王の人事 — シーン配置ガイド（フェーズ1：共通基盤）

`Assets/Scripts/` に実装済みのスクリプトをUnityシーンへ配置し、最低限「手が映って、ポインターが出て、ジェスチャーが拾える」状態まで確認するための手順。まだキャラクターモデル・アニメーション・UI画像などのアセットは無いため、ここではプレースホルダー（Primitiveオブジェクト等）で代用する。

---

## 0. 前提：MediaPipeのBootstrapが必要

`HandTrackingController` はMediaPipeのHandLandmarkerを直接起動するが、その前提として **`Bootstrap`**（`AssetLoader` / `GpuManager` / `ImageSourceProvider` の初期化を行うMonoBehaviour。`Assets/MediaPipeUnity/Samples/Common/Scripts/Bootstrap.cs`）がシーン内で先に完走している必要がある。`HandTrackingController` は起動時にシーン内から `"Bootstrap"` という名前のGameObjectを探し、無ければインスペクタで指定したプレハブを自動生成して初期化完了を待つ。

- 使うプレハブ： `Assets/MediaPipeUnity/Samples/Resources/Bootstrap.prefab`
- このプレハブは `AppSettings` アセット（`Assets/MediaPipeUnity/Samples/Scenes/AppSettings.asset`）を参照しており、Webカメラ入力・推論モード(CPU/GPU)・ログレベルなどはそちらで設定する。

**シーンに直接 `Bootstrap.prefab` をドラッグ配置してもよいし、`HandTrackingController` の `_bootstrapPrefab` フィールドに指定して自動生成に任せてもよい。** 後者の場合は`DontDestroyOnLoad`されるので、タイトル→ゲーム本編のシーン遷移をまたいでも再初期化されない。

---

## 1. GameSettingsアセットの作成

1. `Project` ウィンドウで `Assets/Scripts/Core` 配下などに移動。
2. 右クリック → `Create > DemonLordHR > Game Settings` で `GameSettings.asset` を作成。
3. 同様に候補キャラの数だけ `Create > DemonLordHR > Character Data` でキャラデータを作成し、`genre` ・ `salary` ・ `attackPower` ・（あれば）`resumePages` を仮設定。
4. `GameSettings.asset` の `Character Data` セクション（`allCharacters`）に、作成した `CharacterData` を全て登録。
5. `availableGenres` は初期値で9ジャンル全部入っているので、実施したい数だけ残す（先頭から `recruitmentCycleCount` 件が使われる）。

このアセットは以降ほぼ全コントローラーの `_settings` フィールドに同じものを割り当てる。

---

## 2. シーン階層（推奨構成）

新規シーン（例: `MaouStage.unity`）を作り、以下のヒエラルキーを組む。カッコ内はアタッチするコンポーネント。

```
MaouStage (シーンルート)
├─ Bootstrap                         ← 1.で説明した通り、無ければ自動生成されるので省略可
├─ Systems
│   ├─ GameFlowManager                (GameFlowManager)
│   ├─ HandTracking                   (HandTrackingController, GestureRecognizer)
│   └─ Pointer                        (PointerController)
├─ Player (FPS視点カメラ)
│   └─ Main Camera
├─ UI (Canvas / World Space推奨)
│   ├─ TitleStartButton                (Collider + CircularHoldButton)
│   ├─ RuleReadyButton                 (Collider + CircularHoldButton)
│   ├─ EndInterviewButton              (Collider + CircularHoldButton)
│   └─ EndingEndButton                 (Collider + CircularHoldButton)
├─ Recruitment
│   ├─ RecruitmentPhaseController      (RecruitmentPhaseController)
│   ├─ ResumeUIController              (ResumeUIController)
│   ├─ DoorSpawnPoint                  (空オブジェクト、扉の位置)
│   └─ StandPositions
│       ├─ Stand0 / Stand1 / Stand2    (空オブジェクト、3体の最終停止位置)
├─ Minigames
│   ├─ SwimMinigame                    (SwimMinigame)
│   ├─ FlightMinigame                  (FlightMinigame)
│   ├─ SprintMinigame                  (SprintMinigame)
│   ├─ LaborMinigame                   (LaborMinigame)
│   ├─ ScoutMinigame                   (ScoutMinigame)
│   ├─ CombatMinigame                  (CombatMinigame)
│   ├─ IntelligenceMinigame            (IntelligenceMinigame)
│   ├─ ColdResistMinigame              (ColdResistMinigame)
│   └─ HeatResistMinigame              (HeatResistMinigame)
├─ FinalBattle
│   └─ FinalBattleController           (FinalBattleController)
└─ Ending
    └─ EndingController                (EndingController)
```

各ミニゲームは常時シーンに置いたまま「使うときだけ有効化」でよい（`MinigameBase.RunAsync()` を`GameFlowManager`が順番に呼ぶだけなので、`SetActive`の管理は現状必須ではない）。

---

## 3. 手モデルとHandTrackingController

### 3.1 左手プレハブの作成
仕様書1.1の通り「手首root→5指×4ボーン」の階層を持つ左手モデルが必要。3Dモデルが無い間は、デバッグ用に**球（手首）＋各指4関節を細い直方体で仮組み**するのが手っ取り早い。

1. 空オブジェクト `LeftHand_Root` を作成し、`HandBoneRig` をアタッチ。
2. `LeftHand_Root` の子に `Wrist` を作り、`HandBoneRig.wristRoot` に割り当て。
3. `Wrist` の子に `Thumb_0〜3` / `Index_0〜3` / `Middle_0〜3` / `Ring_0〜3` / `Pinky_0〜3` の空オブジェクト（または見た目用にCubeなど）を作り、`HandBoneRig` の `thumb.bones[0..3]` 〜 `pinky.bones[0..3]` にそれぞれ割り当てる。
4. これをプレハブ化（`Assets/Scripts` 外、例えば `Assets/Prefabs/Hands/MaouHand_Left.prefab`）。

### 3.2 HandTrackingControllerの設定
`Systems/HandTracking` オブジェクトに `HandTrackingController` をアタッチし、インスペクタで：

- `Left Hand Prefab` … 3.1で作った左手プレハブ
- `Hands Parent` … 未指定なら自身の配下に左右の手が生成される
- `Bootstrap Prefab` … `Assets/MediaPipeUnity/Samples/Resources/Bootstrap.prefab`
- `Model Asset Path` … デフォルト `hand_landmarker.bytes`（StreamingAssets等に配置されている前提。`AppSettings`の`AssetLoaderType`設定に依存）
- `Num Hands` … `2`（両手認識）

`HandsVisible` は初期状態でOFF。`GameFlowManager` がタイトル通過後にONへ切り替える（実装済み）。

### 3.3 GestureRecognizerの設定
同じ `Systems/HandTracking` オブジェクトに `GestureRecognizer` を追加し、`Hand Tracking Controller` フィールドに同オブジェクトの `HandTrackingController` を割り当てる。閾値（`_fistDistanceThreshold` 等）は実機で手を動かしながら調整する想定。

### 3.4 PointerControllerの設定
`Systems/Pointer` に `PointerController` をアタッチし、`Hand Tracking Controller` を割り当てる。`Use Right Hand` はデフォルトtrueで右手人差し指基準。`Raycast Mask` は円状ボタンや履歴書・ノード等の当たり判定レイヤーに絞る。

---

## 4. UI（円状ボタン）の配置

`CircularHoldButton` は `Collider` が必須（`RequireComponent`）。ワールド空間に3Dオブジェクトとして置く想定：

1. 円盤状のオブジェクト（Cylinder等、薄く潰す）を作成し、`Collider` を残したまま `CircularHoldButton` をアタッチ。
2. 子に `Image`（`fillAmount` を `Radial 360` に設定したCanvas Image）を置き、`Gauge Image` に割り当てるとゲージが視覚化される（無くても機能はする）。
3. `Hold Seconds` は用途に応じて上書き（「面接終了する」は5秒、それ以外は3秒など。`GameFlowManager`/`RecruitmentPhaseController` 側から`HoldSeconds`を上書きしている箇所もあるので、インスペクタ初期値は目安でよい）。
4. `Debug Click Enabled` をONにしておくと、`OnPointerClick`（uGUIのクリックイベント）経由でも発火できる。**ただし3D配置のワールド空間ボタンでクリック判定を効かせるには、`EventSystem` に加えて `Physics Raycaster`（3Dオブジェクト用）を `Main Camera` にアタッチしておく必要がある。**

`PointerController` 側のワールド空間レイキャストによるhover判定（`IPointerHoldTarget.OnPointerHoldEnter/Exit`）はこの`Physics Raycaster`設定と無関係に動作する。デバッグ時にマウスクリックだけで進めたい場合のみ`Physics Raycaster`が要る。

---

## 5. GameFlowManagerの配線

`Systems/GameFlowManager` に `GameFlowManager` をアタッチし、以下を割り当てる：

- `Settings` … 1.で作ったGameSettings
- `Hand Tracking Controller`
- `Title Start Button` / `Rule Ready Button` … UIで作成した円状ボタン
- `Recruitment Controller`
- `Final Battle Controller`
- `Ending Controller`
- `Minigames` … リスト要素を9つ作り、各 `genre` と対応する `Minigame`（シーン内の各ミニゲームコンポーネント）を1対1で割り当てる

---

## 6. RecruitmentPhaseController / ResumeUIControllerの配線

- `RecruitmentPhaseController`
  - `Settings`、`Resume UI Controller`、`End Interview Button`
  - `Door Spawn Point`（3.1で用意した扉位置）
  - `Stand Positions`（3要素、3体分の最終停止位置）
  - `Entry Waypoints`（3要素、各キャラの曲がり角リスト。空なら`standPositions`へ直行）
  - キャラプレハブ自体は各`CharacterData.characterPrefab`に設定する（プレハブ側に`CharacterEntryPath`は無くても`RecruitmentPhaseController`が自動`AddComponent`する）

- `ResumeUIController`
  - `Gesture Recognizer`、`Settings`
  - `Resume Image Root` / `Resume Image`（2D履歴書用のCanvas Image）
  - `Resume 3D Root`（不採用時の3D履歴書オブジェクト。無ければ空オブジェクトのダミーでも可）

履歴書を開くトリガー（「ポインターを3秒合わせたら`ResumeUIController.Open(character)`」）は現状 `RecruitmentPhaseController.RequestOpenResume()` を呼び出すだけの口を用意してあるが、実際に履歴書オブジェクトにポインター保持判定を仕込む部分は未実装（コード内`TODO`）。`ScoutBaseTarget`と同様のパターン（`IPointerHoldTarget`を実装した小さいコンポーネント）で追加する想定。

---

## 7. ミニゲームの配線

各ミニゲームコンポーネント共通で、インスペクタに：

- `Settings`
- `Gesture Recognizer`
- `Ready Button`（そのミニゲーム用の「準備完了」円状ボタン。共通の1つを使い回しても、ミニゲームごとに専用を用意してもよい）
- `Time Limit Override`（-1なら`GameSettings.defaultMinigameTimeLimit`を使用）

`ScoutMinigame` は追加でシーン上に `ScoutBaseTarget`（`Collider`必須）を複数配置し、各々の `Minigame` フィールドに `ScoutMinigame` を割り当てる。`IntelligenceMinigame` も同様に `MagicCircleNode` を配置する。

---

## 8. FinalBattle / Ending の配線

- `FinalBattleController`：`Settings`、`Gesture Recognizer` を割り当てるだけでよい。
- `EndingController`：`Settings`、`End Button`、（あれば）`Generated Stage Image`（`RawImage`）、`Commemorative Photo Root`。AI画像生成は`NullStageImageGenerator`が既定なので、実際のサービスを繋ぐ場合は起動時に `endingController.SetImageGenerator(new YourGenerator())` のようなブートストラップコードを別途追加する。

---

## 9. 動作確認の手順

1. Unityエディタを再生。
2. コンソールにMediaPipe初期化ログ（`Bootstrap`のログ）が出て、しばらくして `Delegate = ...` 等が出れば手認識パイプラインが起動している。
3. Webカメラに手をかざし、`PointerController`のポインター（Sphere）が画面内を動くか確認。
4. `TitleStartButton` にポインターを3秒重ねてタイトルを抜けられるか確認。
5. 各ジェスチャーを実際に行い、`GestureRecognizer`にログ出力を一時的に追加するなどして正しいジェスチャーが発火するか確認・閾値調整する。

3Dモデルや演出未実装の部分（波・スタンプ演出・整列移動・吹き飛び等）は各スクリプト内の `// TODO` コメントを目印に、アセットが揃ったフェーズで実装していく。
