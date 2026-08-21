using UnityEngine;

namespace DemonLordHR.Minigames
{
  /// <summary>
  /// ObstaclePairプレハブの子オブジェクトにアタッチする、当たり判定の種別マーカー。
  /// 同じプレハブ内に「当たったら墜落」の判定と「通過したら成功」の判定という
  /// 役割の違う2種類のコライダーが同居するため、種別ごとに別オブジェクト＋このスクリプトで
  /// 区別する（同一GameObject上の複数コライダーではOnTriggerEnterでどちらに当たったか区別できないため）。
  ///
  /// Hazard：土管本体など、触れたら墜落になる部分。
  /// PassTrigger：隙間の中央を覆う細いトリガー。パイロットがここを通り抜けたら通過成功。
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class FlightObstacleZone : MonoBehaviour
  {
    public enum ZoneType { Hazard, PassTrigger }

    [SerializeField] private ZoneType zoneType;

    private FlightObstacle _owner;

    public void Initialize(FlightObstacle owner) => _owner = owner;

    /// <summary>手続き生成でこのコンポーネントを追加する場合に種別を設定する。</summary>
    public void SetZoneType(ZoneType type) => zoneType = type;

    private void OnTriggerEnter(Collider other)
    {
      if (_owner == null) return;

      if (zoneType == ZoneType.Hazard) _owner.NotifyHazardHit(other);
      else _owner.NotifyPassed(other);
    }
  }
}
