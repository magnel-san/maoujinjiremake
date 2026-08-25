using DemonLordHR.Core;
using UnityEngine;

namespace DemonLordHR.Audio
{
  /// <summary>
  /// ゲーム全体のBGMを管理する。GameFlowManagerのCurrentStateを毎フレーム見て、
  /// ミニゲーム中(GameState.Minigame)とそれ以外(タイトル/採用試験/勇者襲来/最終決戦/エンディング)で
  /// 曲を自動的に切り替え、常にループ再生する。
  ///
  /// AudioSourceのspatialBlendを0(2D)に固定しているため、3D空間音のような距離減衰が起きず、
  /// プレイヤー(カメラ)がステージ上のどこにワープしても常に同じ音量で聞こえる。
  /// </summary>
  public class BgmController : MonoBehaviour
  {
    [SerializeField] private GameFlowManager _gameFlowManager;
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("ミニゲーム中以外（タイトル・採用試験・勇者襲来・最終決戦・エンディング）に流すBGM。")]
    [SerializeField] private AudioClip _defaultBgm;
    [Tooltip("ミニゲーム中に流すBGM。")]
    [SerializeField] private AudioClip _minigameBgm;
    [Range(0f, 1f)]
    [SerializeField] private float _volume = 0.6f;

    private bool _hasState;
    private bool _lastWasMinigame;

    private void Awake()
    {
      if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
      if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();

      _audioSource.loop = true;
      _audioSource.playOnAwake = false;
      // プレイヤーの位置に関わらず常に同じ音量で聞こえるようにする（3D距離減衰を無効化）。
      _audioSource.spatialBlend = 0f;
      _audioSource.volume = _volume;
    }

    private void Update()
    {
      if (_gameFlowManager == null) return;

      var isMinigame = _gameFlowManager.CurrentState == GameState.Minigame;
      if (_hasState && isMinigame == _lastWasMinigame) return;

      _hasState = true;
      _lastWasMinigame = isMinigame;
      SwitchTo(isMinigame ? _minigameBgm : _defaultBgm);
    }

    private void SwitchTo(AudioClip clip)
    {
      if (clip == null || _audioSource.clip == clip) return;
      _audioSource.clip = clip;
      _audioSource.Play();
    }
  }
}
