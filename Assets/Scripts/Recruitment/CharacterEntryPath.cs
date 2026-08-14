using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DemonLordHR.Recruitment
{
  /// <summary>
  /// Waypointベースの入室歩行制御。曲がり角の座標を複数指定でき、
  /// 曲がる際はキャラも進行方向へ回転させる。
  /// </summary>
  public class CharacterEntryPath : MonoBehaviour
  {
    [Tooltip("扉から最終停止位置までの曲がり角座標（最後の要素が最終停止位置）")]
    [SerializeField] private List<Vector3> _waypoints = new List<Vector3>();
    [Tooltip("全Waypointを歩き切るまでの所要時間")]
    [SerializeField] private float _totalDuration = 4f;
    [SerializeField] private float _turnSpeedDegPerSec = 360f;

    public event Action OnArrived;

    public IEnumerator WalkAsync()
    {
      if (_waypoints == null || _waypoints.Count == 0)
      {
        OnArrived?.Invoke();
        yield break;
      }

      var totalLength = 0f;
      var prev = transform.position;
      foreach (var wp in _waypoints)
      {
        totalLength += Vector3.Distance(prev, wp);
        prev = wp;
      }
      if (totalLength <= 0f) totalLength = 1f;

      var speed = totalLength / Mathf.Max(_totalDuration, 0.01f);

      foreach (var waypoint in _waypoints)
      {
        yield return MoveTo(waypoint, speed);
      }

      OnArrived?.Invoke();
    }

    private IEnumerator MoveTo(Vector3 target, float speed)
    {
      var targetDir = target - transform.position;
      targetDir.y = 0f;

      while (targetDir.sqrMagnitude > 0.0001f)
      {
        var toTarget = target - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f) break;

        var targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, _turnSpeedDegPerSec * Time.deltaTime);
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

        targetDir = target - transform.position;
        targetDir.y = 0f;
        yield return null;
      }

      transform.position = new Vector3(target.x, transform.position.y, target.z);
    }

    public void SetWaypoints(List<Vector3> waypoints) => _waypoints = waypoints;
    public void SetDuration(float duration) => _totalDuration = duration;
  }
}
