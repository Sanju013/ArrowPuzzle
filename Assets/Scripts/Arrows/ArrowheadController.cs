using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ArrowheadController : MonoBehaviour
{
    public static event Action GameplayStateChanged;

    private struct SweepTarget
    {
        public Vector2 center;
        public float radius;
    }

    [Header("References")]
    [SerializeField] private ArrowPath arrowPath;
    [SerializeField] private ArrowheadController oppositeArrowhead;
    [SerializeField] private ArrowLineController lineController;
    [SerializeField] private GameManager gameManager;

    [Header("Movement")]
    [SerializeField] private bool movesFromStart = true;
    [SerializeField] private float moveSpeed = 2f;

    private bool isMoving;
    private float progress;
    private Camera mainCamera;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Vector3 pathEntryPosition;
    private float pathEntryLength;

    public ArrowLineController GetLineController()
    {
        return lineController;
    }

    /// <summary>
    /// Position along the path as a normalised distance from the start.
    /// Used by ArrowLineController to hide the path behind moving heads.
    /// </summary>
    public float NormalizedPathPosition
    {
        get
        {
            if (arrowPath == null || arrowPath.TotalLength <= 0f)
                return movesFromStart ? 0f : 1f;

            float entryLength = pathEntryLength;
            float travelled = progress * (arrowPath.TotalLength + entryLength);
            if (travelled <= entryLength)
                return movesFromStart ? 0f : 1f;

            float pathProgress = Mathf.Clamp01(
                (travelled - entryLength) / arrowPath.TotalLength);
            return movesFromStart ? pathProgress : 1f - pathProgress;
        }
    }

    public bool IsLocked
    {
        get { return oppositeArrowhead != null && oppositeArrowhead.isMoving; }
    }

    /// <summary>
    /// Runtime wiring used by LevelLoader. The loaded level has no
    /// hand-assigned Inspector references, so the loader supplies them here.
    /// </summary>
    public void Configure(
        ArrowPath path,
        ArrowLineController line,
        GameManager manager,
        bool fromStart,
        float speed)
    {
        arrowPath = path;
        lineController = line;
        gameManager = manager;
        movesFromStart = fromStart;
        moveSpeed = speed;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        CachePathEntry();
    }

    /// <summary>
    /// Sets the opposite arrowhead reference for the lock mechanic.
    /// When one arrowhead is moving, the opposite one is locked.
    /// </summary>
    public void SetOpposite(ArrowheadController opposite)
    {
        oppositeArrowhead = opposite;
    }

    private void Awake()
    {
        mainCamera = Camera.main;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        CachePathEntry();
    }

    private void Update()
    {
        if (!isMoving && mainCamera != null &&
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePosition =
                Mouse.current.position.ReadValue();

            Vector3 worldPosition =
                mainCamera.ScreenToWorldPoint(mousePosition);

            if (TryStartMovingAt(worldPosition))
            {
                Debug.Log("Arrowhead started: " + gameObject.name);
            }
        }

        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances movement by an explicit delta time. Update passes
    /// Time.deltaTime; editor validation can drive the same code path
    /// deterministically.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!isMoving)
            return;

        if (arrowPath == null || arrowPath.TotalLength <= 0f)
            return;

        float entryLength = pathEntryLength;
        float movementLength = arrowPath.TotalLength + entryLength;
        if (movementLength <= 0f)
            return;

        progress += moveSpeed * deltaTime / movementLength;
        progress = Mathf.Clamp01(progress);

        float distanceTravelled = progress * movementLength;
        if (entryLength > 0f && distanceTravelled < entryLength)
        {
            transform.position = Vector3.Lerp(
                spawnPosition, pathEntryPosition,
                distanceTravelled / entryLength);
        }
        else
        {
            float pathDistance = Mathf.Max(0f, distanceTravelled - entryLength);
            float pathProgress = Mathf.Clamp01(pathDistance / arrowPath.TotalLength);
            if (!movesFromStart)
                pathProgress = 1f - pathProgress;

            transform.position =
                arrowPath.GetPointAtNormalizedDistance(pathProgress);
        }

        if (progress >= 1f && lineController != null)
        {
            lineController.CompleteLine();
        }
    }

    /// <summary>
    /// Attempts to begin movement for this arrowhead using the same
    /// point-raycast and opposite-head lock as a mouse click.
    /// Returns true when movement started.
    /// </summary>
    public bool TryStartMovingAt(Vector3 worldPosition)
    {
        if (isMoving)
            return false;

        RaycastHit2D hit =
            Physics2D.Raycast(worldPosition, Vector2.zero);

        if (hit.collider == null ||
            hit.collider.gameObject != gameObject)
        {
            return false;
        }

        if (oppositeArrowhead != null && oppositeArrowhead.isMoving)
            return false;

        isMoving = true;
        NotifyGameplayStateChanged();
        return true;
    }

    /// <summary>
    /// Tests whether this stationary, unlocked arrowhead can traverse its
    /// remaining route without its actual collider sweeping into another
    /// active arrowhead. Paths have no collider and are never considered.
    /// </summary>
    public bool CanReachDestinationSafely()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy ||
            isMoving || IsLocked || lineController == null ||
            lineController.IsCompleted() || arrowPath == null)
        {
            return false;
        }

        CircleCollider2D sourceCircle = GetComponent<CircleCollider2D>();
        if (sourceCircle == null || !sourceCircle.enabled || arrowPath.TotalLength <= 0f)
            return false;

        float destination = movesFromStart ? 1f : 0f;
        List<Vector3> route = arrowPath.GetRoutePoints(
            NormalizedPathPosition, destination);
        if (route.Count < 2)
            return false;

        if (Vector3.Distance(transform.position, route[0]) > 0.0001f)
            route.Insert(0, transform.position);

        Vector2 sourceCenter = GetWorldCircleCenter(sourceCircle);
        float sourceRadius = GetWorldCircleRadius(sourceCircle);

        List<SweepTarget> blockers = new List<SweepTarget>();
        ArrowheadController[] activeHeads =
            FindObjectsByType<ArrowheadController>(FindObjectsSortMode.None);
        foreach (ArrowheadController other in activeHeads)
        {
            if (other == null || other == this ||
                !other.isActiveAndEnabled || !other.gameObject.activeInHierarchy ||
                other.lineController == null || other.lineController.IsCompleted() ||
                other.lineController == lineController)
                continue;

            CircleCollider2D otherCircle = other.GetComponent<CircleCollider2D>();
            if (otherCircle != null && otherCircle.enabled)
            {
                blockers.Add(new SweepTarget
                {
                    center = GetWorldCircleCenter(otherCircle),
                    radius = GetWorldCircleRadius(otherCircle)
                });
            }
        }

        Vector2 routeOrigin = route[0];
        for (int routeIndex = 0; routeIndex < route.Count; routeIndex++)
        {
            Vector2 routeOffset = (Vector2)route[routeIndex] - routeOrigin;
            Vector2 routeDelta = routeIndex + 1 < route.Count
                ? (Vector2)route[routeIndex + 1] - (Vector2)route[routeIndex]
                : Vector2.zero;

            foreach (SweepTarget target in blockers)
            {
                Vector2 movingCenter = sourceCenter + routeOffset;
                if (SweptCirclesOverlap(
                        movingCenter, routeDelta, sourceRadius,
                        target.center, target.radius))
                    return false;
            }
        }

        return true;
    }

    public static Vector2 GetWorldCircleCenter(CircleCollider2D circle)
    {
        return circle.transform.TransformPoint(circle.offset);
    }

    public static float GetWorldCircleRadius(CircleCollider2D circle)
    {
        Vector3 scale = circle.transform.lossyScale;
        return circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
    }

    private void CachePathEntry()
    {
        if (arrowPath == null || arrowPath.TotalLength <= 0f)
        {
            pathEntryPosition = spawnPosition;
            pathEntryLength = 0f;
            return;
        }

        pathEntryPosition = arrowPath.GetPointAtNormalizedDistance(
            movesFromStart ? 0f : 1f);
        pathEntryLength = Vector2.Distance(spawnPosition, pathEntryPosition);
    }

    /// <summary>
    /// Exact swept-circle test for one linear route segment against a
    /// stationary arrowhead circle. No padding is applied: these are the same
    /// radii used by the runtime CircleCollider2D trigger geometry.
    /// </summary>
    private static bool SweptCirclesOverlap(
        Vector2 movingCenter,
        Vector2 displacement,
        float movingRadius,
        Vector2 stationaryCenter,
        float stationaryRadius)
    {
        Vector2 segmentEnd = movingCenter + displacement;
        Vector2 segment = segmentEnd - movingCenter;
        float segmentLengthSquared = segment.sqrMagnitude;
        float t = segmentLengthSquared > 0.0000001f
            ? Mathf.Clamp01(Vector2.Dot(stationaryCenter - movingCenter, segment) /
                            segmentLengthSquared)
            : 0f;
        Vector2 closest = movingCenter + segment * t;
        float combinedRadius = movingRadius + stationaryRadius;
        return (stationaryCenter - closest).sqrMagnitude <=
               combinedRadius * combinedRadius;
    }

    public static void NotifyGameplayStateChanged()
    {
        GameplayStateChanged?.Invoke();
    }

    public void ResetArrowhead(Vector3 originalPosition)
    {
        isMoving = false;
        progress = 0f;
        spawnPosition = originalPosition;
        CachePathEntry();
        transform.position = originalPosition;
        transform.rotation = spawnRotation;
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = originalPosition;
            body.rotation = spawnRotation.eulerAngles.z;
        }
        NotifyGameplayStateChanged();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isMoving)
            return;

        ArrowheadController otherArrowhead =
            other.GetComponent<ArrowheadController>();

        if (otherArrowhead == null)
            return;

        
        if (otherArrowhead.GetLineController() == lineController)
            return;

        Collider2D ownCollider = GetComponent<Collider2D>();
        ColliderDistance2D contact = ownCollider != null
            ? Physics2D.Distance(ownCollider, other)
            : default;
        Vector2 contactPosition = (contact.pointA + contact.pointB) * 0.5f;

        Debug.Log(
            "Arrowhead collision contact: " + gameObject.name +
            " [" + (lineController != null
                ? lineController.gameObject.name : "UnassignedLine") + "] at " +
            transform.position + " vs " + otherArrowhead.gameObject.name +
            " [" + (otherArrowhead.lineController != null
                ? otherArrowhead.lineController.gameObject.name : "UnassignedLine") + "] at " +
            otherArrowhead.transform.position + "; contact=" +
            contactPosition + "; separation=" + contact.distance);

        if (gameManager != null)
            gameManager.HandleCollision(this, otherArrowhead);
    }

    public bool IsMoving()
    {
        return isMoving;
    }
}
