using System;
using DemonLordHR.HandTracking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DemonLordHR.UI
{
  /// <summary>
  /// 円状ポインター合わせゲージ。指定秒数ポインターを重ねると発火する汎用UI。
  /// 「準備完了」「もどる」「面接終了する」「終了する」等で使い回す。
  /// デバッグ用に、クリックでも発火可能（<see cref="debugClickEnabled"/>）。
  /// ワールド空間ポインター（<see cref="PointerController"/>）とUIのポインター(IPointerClickHandler)
  /// の両方から呼べるよう、<see cref="IPointerHoldTarget"/>を実装する。
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class CircularHoldButton : MonoBehaviour, IPointerHoldTarget, IPointerClickHandler
  {
    [Tooltip("保持完了までの秒数")]
    [SerializeField] private float _holdSeconds = 3f;
    [Tooltip("ポインターを重ねている間を可視化する円形ゲージ（Image.fillAmountをType=Radial360で使用）")]
    [SerializeField] private Image _gaugeImage;
    [Tooltip("デバッグ用：クリックでも即時発火可能にする")]
    [SerializeField] private bool _debugClickEnabled = true;
    [Tooltip("発火後、自動でゲージをリセットするか")]
    [SerializeField] private bool _resetAfterTrigger = true;
    [Tooltip("このボタンの表示/非表示に連動させたいUIのルート（任意）。ボタン本体がSetActive(true)される" +
      "タイミングで一緒に表示し、発火して呼び出し元がボタンをSetActive(false)するタイミングで一緒に" +
      "非表示になる（背景パネルや説明文など、ボタンとは別オブジェクトを同時に出し入れしたい場合に使う）。")]
    [SerializeField] private GameObject _uiRoot;

    public event Action OnTriggered;

    private bool _isHovering;
    private float _heldSeconds;
    private bool _triggeredOnce;

    public float HoldSeconds
    {
      get => _holdSeconds;
      set => _holdSeconds = value;
    }

    public bool DebugClickEnabled
    {
      get => _debugClickEnabled;
      set => _debugClickEnabled = value;
    }

    /// <summary>呼び出し元がこのボタン自身をSetActive(true)した瞬間に呼ばれる（Unity標準のコールバック）。
    /// 表示タイミングを合わせたいだけなので、呼び出し元側に個別の連動コードを書く必要がない。</summary>
    private void OnEnable()
    {
      if (_uiRoot != null) _uiRoot.SetActive(true);
    }

    /// <summary>呼び出し元がこのボタン自身をSetActive(false)した瞬間に呼ばれる。発火直後に呼び出し元が
    /// ボタンを非表示にする既存の流れ（各ジャンルのShowRulesAndWaitReady等）にそのまま乗っかる形で、
    /// 「選択したら連動UIも消える」を実現している。</summary>
    private void OnDisable()
    {
      if (_uiRoot != null) _uiRoot.SetActive(false);
    }

    private void Update()
    {
      if (!_isHovering)
      {
        if (_heldSeconds > 0f)
        {
          _heldSeconds = 0f;
          UpdateGauge();
        }
        return;
      }

      if (_triggeredOnce && _resetAfterTrigger == false) return;

      _heldSeconds += Time.deltaTime;
      UpdateGauge();

      if (_heldSeconds >= _holdSeconds)
      {
        Fire();
      }
    }

    private void UpdateGauge()
    {
      if (_gaugeImage != null)
      {
        _gaugeImage.fillAmount = _holdSeconds <= 0f ? 0f : Mathf.Clamp01(_heldSeconds / _holdSeconds);
      }
    }

    private void Fire()
    {
      _heldSeconds = 0f;
      _triggeredOnce = true;
      UpdateGauge();
      OnTriggered?.Invoke();
    }

    public void OnPointerHoldEnter()
    {
      _isHovering = true;
    }

    public void OnPointerHoldExit()
    {
      _isHovering = false;
    }

    /// <summary>デバッグ用：UI EventSystem経由のマウスクリックで即発火。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
      if (_debugClickEnabled)
      {
        Fire();
      }
    }
  }
}
