using UnityEngine;
using UnityEngine.Serialization;


namespace SplashEdit.RuntimeCode
{
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXPlayer.png")]
    public class PSXPlayer : MonoBehaviour
    {
        [Header("Player Dimensions")]
        [FormerlySerializedAs("PlayerHeight")]
        [Tooltip("Camera eye height above the player's feet")]
        [SerializeField] private float playerHeight = 1.8f;

        [Tooltip("Collision radius for wall sliding")]
        [SerializeField] private float playerRadius = 0.5f;

        [Header("Movement")]
        [Tooltip("Walk speed in world units per second")]
        [SerializeField] private float moveSpeed = 3.0f;

        [Tooltip("Sprint speed in world units per second")]
        [SerializeField] private float sprintSpeed = 8.0f;

       
    }
}
