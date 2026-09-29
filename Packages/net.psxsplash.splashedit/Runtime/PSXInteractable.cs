using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Makes an object interactable by the player.
    /// When the player is within range and presses the interact button,
    /// the onInteract Lua event fires.
    /// </summary>
    [RequireComponent(typeof(PSXObjectExporter))]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXInteractable.png")]
    public class PSXInteractable : MonoBehaviour
    {
        [Header("Interaction Settings")]
        [Tooltip("Distance within which the player can interact with this object")]
        [SerializeField] private float interactionRadius = 2.0f;

        [Tooltip("Button that triggers interaction (0-15, matches PS1 button mapping)")]
        [SerializeField] private int interactButton = 14; // Default to Cross button

        [Tooltip("Can this object be interacted with multiple times?")]
        [SerializeField] private bool isRepeatable = true;

        [Tooltip("Cooldown between interactions (in frames, 60 = 1 second at NTSC)")]
        [SerializeField] private ushort cooldownFrames = 30;

        [Tooltip("Show a UI canvas when the player is in range")]
        [SerializeField] private bool showPrompt = false;

        [Tooltip("Name of the PSXCanvas to show when the player is in range")]
        [SerializeField] private string promptCanvasName = "";

        [Header("Advanced")]
        [Tooltip("Require the player to be facing this object to interact")]
        [SerializeField] private bool requireLineOfSight = false;

        // Public accessors for export
        public float InteractionRadius => interactionRadius;
        public int InteractButton => interactButton;
        public bool IsRepeatable => isRepeatable;
        public ushort CooldownFrames => cooldownFrames;
        public bool ShowPrompt => showPrompt;
        public string PromptCanvasName => promptCanvasName;
        public bool RequireLineOfSight => requireLineOfSight;

        private void OnDrawGizmosSelected()
        {
            // Draw interaction radius
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // Yellow, semi-transparent
            Vector3 center = transform.position;
            Gizmos.DrawWireSphere(center, interactionRadius);

            // Draw filled sphere with lower alpha
            Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
            Gizmos.DrawSphere(center, interactionRadius);
        }
    }
}
