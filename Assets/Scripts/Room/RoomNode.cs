using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Điểm xuất hiện của nhân vật trong một phòng (VD: "Default", "From_PhongNgu", "From_HanhLang").
/// </summary>
[Serializable]
public class RoomSpawnPoint
{
    [Tooltip("Mã định danh điểm xuất hiện trong phòng (VD: Default, From_PhongNgu).")]
    public string spawnId = "Default";

    [Tooltip("Vị trí Transform nơi nhân vật sẽ đứng khi bước vào phòng qua điểm này.")]
    public Transform point;
}

/// <summary>
/// Đại diện cho một căn phòng độc lập trong hệ thống chuyển phòng cùng Scene.
/// Quản lý vùng giới hạn Camera (CinemachineConfiner2D), các điểm Spawn và bật/tắt GameObject phòng để tối ưu Draw Calls.
/// </summary>
public class RoomNode : MonoBehaviour
{
    [Header("Định danh phòng")]
    [Tooltip("Mã duy nhất của phòng dùng để chuyển phòng và lưu tiến trình (VD: Ch1_PhongNgu).")]
    [SerializeField] private string roomId = "Ch1_PhongNgu";

    [Header("Giới hạn Camera (Tùy chọn)")]
    [Tooltip("PolygonCollider2D hoặc BoxCollider2D định nghĩa biên giới hạn cho CinemachineConfiner2D khi ở phòng này.")]
    [SerializeField] private Collider2D cameraBounds;

    [Header("Danh sách điểm xuất hiện (Spawn Points)")]
    [Tooltip("Các vị trí nhân vật xuất hiện tùy theo cửa đi vào (VD: Default, From_PhongNgu...).")]
    [SerializeField] private List<RoomSpawnPoint> spawnPoints = new List<RoomSpawnPoint>();

    /// <summary>Mã định danh duy nhất của phòng.</summary>
    public string RoomId => roomId;

    /// <summary>Collider2D giới hạn camera của phòng (có thể null nếu phòng không khóa biên camera).</summary>
    public Collider2D CameraBounds => cameraBounds;

    /// <summary>Danh sách chỉ đọc các điểm spawn đã cấu hình trên phòng.</summary>
    public IReadOnlyList<RoomSpawnPoint> SpawnPoints => spawnPoints;

    /// <summary>
    /// Tra cứu điểm xuất hiện theo spawnId (không phân biệt hoa thường).
    /// Luôn có fallback về điểm đầu tiên hợp lệ hoặc chính transform của phòng để không bao giờ trả về null.
    /// </summary>
    public Transform GetSpawnPoint(string spawnId)
    {
        Transform firstValidPoint = null;

        if (spawnPoints != null)
        {
            bool hasTargetId = !string.IsNullOrEmpty(spawnId);

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                RoomSpawnPoint entry = spawnPoints[i];
                if (entry == null || entry.point == null)
                {
                    continue;
                }

                if (firstValidPoint == null)
                {
                    firstValidPoint = entry.point;
                }

                if (hasTargetId && string.Equals(entry.spawnId, spawnId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.point;
                }
            }
        }

        // Chống NullReferenceException: rơi về điểm spawn đầu tiên hoặc chính Transform của phòng
        return firstValidPoint != null ? firstValidPoint : transform;
    }

    /// <summary>
    /// Bật hoặc tắt toàn bộ GameObject của phòng để tối ưu Draw Calls và vật lý ngoài tầm nhìn.
    /// </summary>
    public void SetRoomActive(bool isActive)
    {
        if (gameObject.activeSelf != isActive)
        {
            gameObject.SetActive(isActive);
        }
    }
}
