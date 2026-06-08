using UnityEngine;

/// <summary>
/// Receives footstep animation events from imported character clips.
/// </summary>
public class FootstepAnimationEventReceiver : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip footstepClip;
    [SerializeField] private float volume = 0.18f;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponentInParent<AudioSource>();
    }

    public void OnLeftFootstep()
    {
        PlayFootstep();
    }

    public void OnRightFootstep()
    {
        PlayFootstep();
    }

    private void PlayFootstep()
    {
        if (audioSource != null && footstepClip != null)
            audioSource.PlayOneShot(footstepClip, volume);
    }
}
