using DemonLordHR.HandTracking;
using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>魔法陣周囲（五芒星の頂点）に配置するノード。ポインターで指すと接続される。
  /// 五芒星は正しい順番でなぞる必要があるため、各ノードは自分が何番目の頂点かを<see cref="nodeIndex"/>で
  /// 持っておき、<see cref="IntelligenceMinigame"/>側で順番の正否を判定する。
  /// ポインターが乗っている間・接続済みの間は、マテリアルの色を変えて見た目でも分かるようにする
  /// （MaterialPropertyBlock経由なので、マテリアルのインスタンスを増やさない）。</summary>
  [RequireComponent(typeof(Collider))]
  public class MagicCircleNode : MonoBehaviour, IPointerHoldTarget
  {
    [SerializeField] private IntelligenceMinigame _minigame;
    [SerializeField] private float _connectHoldSeconds = 0.5f;
    [Tooltip("五芒星の頂点として、円周上を並んだ順に0〜4を振る（IntelligenceMinigame側のなぞり順はこの番号を基準に決まる）")]
    [SerializeField] private int nodeIndex;

    [Header("見た目のフィードバック")]
    [Tooltip("色を変えるRenderer。未設定なら自身/子から自動で探す。")]
    [SerializeField] private Renderer _renderer;
    [SerializeField] private Color _idleColor = Color.white;
    [Tooltip("ポインターが乗っているが、まだ接続保持時間に達していない間の色")]
    [SerializeField] private Color _hoverColor = Color.yellow;
    [Tooltip("接続済み（正しく触れた）間の色")]
    [SerializeField] private Color _connectedColor = Color.cyan;

    private bool _isHovering;
    private bool _connected;
    private float _heldSeconds;
    private MaterialPropertyBlock _propertyBlock;

    private void Awake()
    {
      if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
      _propertyBlock = new MaterialPropertyBlock();
      ApplyColor(_idleColor);
    }

    private void Update()
    {
      if (_connected || !_isHovering) return;

      _heldSeconds += Time.deltaTime;
      if (_heldSeconds >= _connectHoldSeconds)
      {
        _connected = true;
        ApplyColor(_connectedColor);
        _minigame?.RegisterNodeTouched(nodeIndex);
      }
    }

    public void OnPointerHoldEnter()
    {
      _isHovering = true;
      if (!_connected) ApplyColor(_hoverColor);
    }

    public void OnPointerHoldExit()
    {
      _isHovering = false;
      if (!_connected)
      {
        _heldSeconds = 0f;
        ApplyColor(_idleColor);
      }
    }

    public void ResetNode()
    {
      _connected = false;
      _heldSeconds = 0f;
      ApplyColor(_isHovering ? _hoverColor : _idleColor);
    }

    private void ApplyColor(Color color)
    {
      if (_renderer == null) return;
      _renderer.GetPropertyBlock(_propertyBlock);
      _propertyBlock.SetColor("_BaseColor", color); // URP Lit/Unlit
      _propertyBlock.SetColor("_Color", color); // Built-in/Standard互換
      _renderer.SetPropertyBlock(_propertyBlock);
    }
  }
}
