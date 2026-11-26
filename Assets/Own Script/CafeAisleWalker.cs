using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class CafeAisleWalker : MonoBehaviour
{
    [Tooltip("World-space offset from this NPC's starting position to its turnaround point.")]
    [SerializeField] private Vector3 aisleOffset = new Vector3(0f, 0f, 4.9f);
    [SerializeField, Min(0.1f)] private float walkingSpeed = 1.05f;
    [SerializeField, Min(0.1f)] private float turnSeconds = 0.85f;

    private Vector3 start;
    private Vector3 end;
    private bool outbound = true;
    private bool turning;
    private float turnTime;
    private Quaternion turnFrom;

    private void Start()
    {
        start = transform.position;
        end = start + Vector3.ProjectOnPlane(aisleOffset, Vector3.up);
        if ((end - start).sqrMagnitude < 0.01f)
        {
            enabled = false;
            return;
        }

        transform.rotation = Quaternion.LookRotation(end - start);
    }

    private void Update()
    {
        if (turning)
        {
            turnTime += Time.deltaTime;
            float progress = Mathf.Clamp01(turnTime / Mathf.Max(0.1f, turnSeconds));
            float angle = 180f * Mathf.SmoothStep(0f, 1f, progress);
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.up) * turnFrom;
            turning = progress < 1f;
            return;
        }

        Vector3 target = outbound ? end : start;
        transform.position = Vector3.MoveTowards(transform.position, target, walkingSpeed * Time.deltaTime);
        if (transform.position == target)
        {
            transform.position = target;
            outbound = !outbound;
            turnFrom = Quaternion.LookRotation(outbound ? start - end : end - start);
            turnTime = 0f;
            turning = true;
        }
    }
}
