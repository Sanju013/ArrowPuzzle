
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ArrowPath : MonoBehaviour
{
    [Header("Path Settings")]
    [SerializeField] private Color pathColor = Color.blue;
    [SerializeField] private float lineWidth = 0.06f;
    [SerializeField] private int samplesPerSegment = 20;

    [Header("Control Points")]
    [SerializeField]
    private List<Transform> controlPoints =
        new List<Transform>();

    private LineRenderer lineRenderer;

    private readonly List<Vector3> sampledPoints =
        new List<Vector3>();

    private readonly List<float> cumulativeDistances =
        new List<float>();

    private List<Vector3> runtimeControlPoints;

    private float visibleFrom;
    private float visibleTo = 1f;

    public float TotalLength { get; private set; }

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        lineRenderer = GetComponent<LineRenderer>();

        ApplyRendererSettings();
        GeneratePath();
    }

    private void ApplyRendererSettings()
    {
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer == null)
            return;

        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = pathColor;
        lineRenderer.endColor = pathColor;
        lineRenderer.numCapVertices = 8;
        lineRenderer.numCornerVertices = 8;
    }

    /// <summary>
    /// Configures a procedurally generated path. Used by LevelLoader so the
    /// level can be created without hundreds of control-point Transforms.
    /// </summary>
    public void Configure(
        Color color,
        float width,
        int samples,
        IReadOnlyList<Vector3> worldPoints)
    {
        pathColor = color;
        lineWidth = width;
        samplesPerSegment = Mathf.Max(16, samples);

        runtimeControlPoints = new List<Vector3>(worldPoints);

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        ApplyRendererSettings();
        GeneratePath();
    }

    public void GeneratePath()
    {
        sampledPoints.Clear();
        cumulativeDistances.Clear();
        TotalLength = 0f;

        int pointCount = ControlPointCount;

        if (pointCount < 2)
        {
            if (lineRenderer != null)
                lineRenderer.positionCount = 0;

            return;
        }

        for (int i = 0; i < pointCount - 1; i++)
        {
            Vector3 p0 = GetControlPoint(Mathf.Max(i - 1, 0));
            Vector3 p1 = GetControlPoint(i);
            Vector3 p2 = GetControlPoint(i + 1);
            Vector3 p3 = GetControlPoint(
                Mathf.Min(i + 2, pointCount - 1));

            // Include t = 1.0 so each segment ends exactly at p2,
            // guaranteeing continuous rendering across segment boundaries.
            for (int j = 0; j <= samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;

                Vector3 point = CatmullRom(
                    p0, p1, p2, p3, t
                );

                AddSample(point);
            }
        }

        AddSample(GetControlPoint(pointCount - 1));

        ShowFullPath();
    }

    private int ControlPointCount
    {
        get
        {
            return runtimeControlPoints != null
                ? runtimeControlPoints.Count
                : controlPoints.Count;
        }
    }

    private Vector3 GetControlPoint(int index)
    {
        return runtimeControlPoints != null
            ? runtimeControlPoints[index]
            : controlPoints[index].position;
    }

    private Vector3 CatmullRom(
        Vector3 p0, Vector3 p1,
        Vector3 p2, Vector3 p3,
        float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void AddSample(Vector3 point)
    {
        if (sampledPoints.Count > 0)
        {
            TotalLength += Vector3.Distance(
                sampledPoints[sampledPoints.Count - 1],
                point
            );
        }

        sampledPoints.Add(point);
        cumulativeDistances.Add(TotalLength);
    }

    public Vector3 GetPointAtDistance(float distance)
    {
        if (sampledPoints.Count == 0)
            return transform.position;

        distance = Mathf.Clamp(distance, 0f, TotalLength);

        for (int i = 1; i < cumulativeDistances.Count; i++)
        {
            if (cumulativeDistances[i] >= distance)
            {
                float segmentLength =
                    cumulativeDistances[i] -
                    cumulativeDistances[i - 1];

                float t = segmentLength > 0f
                    ? (distance - cumulativeDistances[i - 1])
                      / segmentLength
                    : 0f;

                return Vector3.Lerp(
                    sampledPoints[i - 1],
                    sampledPoints[i],
                    t
                );
            }
        }

        return sampledPoints[sampledPoints.Count - 1];
    }

    public Vector3 GetPointAtNormalizedDistance(float t)
    {
        return GetPointAtDistance(
            Mathf.Clamp01(t) * TotalLength
        );
    }

    /// <summary>
    /// Returns the tangent vector at the given normalized distance along the path.
    /// Used by arrowheads to orient themselves along the path.
    /// </summary>
    public Vector3 GetTangentAtNormalizedDistance(float normalizedDistance)
    {
        if (sampledPoints.Count < 2 || TotalLength <= 0.000001f)
            return Vector3.up;

        float distance = Mathf.Clamp01(normalizedDistance) * TotalLength;
        float delta = Mathf.Clamp(TotalLength * 0.002f, 0.002f, 0.025f);
        float before = Mathf.Max(0f, distance - delta);
        float after = Mathf.Min(TotalLength, distance + delta);
        Vector3 tangent = GetPointAtDistance(after) - GetPointAtDistance(before);
        return tangent.sqrMagnitude > 0.0000001f
            ? tangent.normalized
            : Vector3.up;
    }

    /// <summary>
    public List<Vector3> GetRoutePoints(float from, float to)
    {
        List<Vector3> route = new List<Vector3>();
        if (sampledPoints.Count == 0)
            return route;

        from = Mathf.Clamp01(from);
        to = Mathf.Clamp01(to);
        float fromDistance = from * TotalLength;
        float toDistance = to * TotalLength;
        bool forward = toDistance >= fromDistance;

        route.Add(GetPointAtDistance(fromDistance));

        if (forward)
        {
            for (int i = 0; i < cumulativeDistances.Count; i++)
            {
                float distance = cumulativeDistances[i];
                if (distance > fromDistance + 0.0001f &&
                    distance < toDistance - 0.0001f)
                {
                    AddVisiblePoint(route, sampledPoints[i]);
                }
            }
        }
        else
        {
            for (int i = cumulativeDistances.Count - 1; i >= 0; i--)
            {
                float distance = cumulativeDistances[i];
                if (distance < fromDistance - 0.0001f &&
                    distance > toDistance + 0.0001f)
                {
                    AddVisiblePoint(route, sampledPoints[i]);
                }
            }
        }

        AddVisiblePoint(route, GetPointAtDistance(toDistance));
        return route;
    }

    /// <summary>
    /// Shows only the section of the path between two normalised distances.
    /// Used to make the path disappear progressively behind moving heads.
    /// </summary>
    public void SetVisibleNormalizedRange(float from, float to)
    {
        from = Mathf.Clamp01(from);
        to = Mathf.Clamp01(to);

        if (from > to)
        {
            float swap = from;
            from = to;
            to = swap;
        }

        if (Mathf.Abs(from - visibleFrom) < 0.0001f &&
            Mathf.Abs(to - visibleTo) < 0.0001f)
        {
            return;
        }

        visibleFrom = from;
        visibleTo = to;

        ApplyVisibleRange();
    }

    public void ShowFullPath()
    {
        visibleFrom = 0f;
        visibleTo = 1f;

        ApplyVisibleRange();
    }

    private void ApplyVisibleRange()
    {
        if (lineRenderer == null)
            return;

        if (sampledPoints.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        if (visibleFrom <= 0f && visibleTo >= 1f)
        {
            lineRenderer.positionCount = sampledPoints.Count;
            lineRenderer.SetPositions(sampledPoints.ToArray());
            return;
        }

        float startDistance = visibleFrom * TotalLength;
        float endDistance = visibleTo * TotalLength;

        List<Vector3> visiblePoints = new List<Vector3>();

        AddVisiblePoint(visiblePoints,
            GetPointAtDistance(startDistance));

        for (int i = 0; i < sampledPoints.Count; i++)
        {
            float distance = cumulativeDistances[i];

            if (distance > startDistance + 0.0001f &&
                distance < endDistance - 0.0001f)
            {
                AddVisiblePoint(visiblePoints, sampledPoints[i]);
            }
        }

        AddVisiblePoint(visiblePoints,
            GetPointAtDistance(endDistance));

        lineRenderer.positionCount = visiblePoints.Count;
        lineRenderer.SetPositions(visiblePoints.ToArray());
    }

    private static void AddVisiblePoint(
        List<Vector3> points, Vector3 point)
    {
        if (points.Count > 0 &&
            Vector3.SqrMagnitude(
                points[points.Count - 1] - point) < 0.0000001f)
        {
            return;
        }

        points.Add(point);
    }
}
