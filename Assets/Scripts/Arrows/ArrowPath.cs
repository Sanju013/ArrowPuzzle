
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

    public float TotalLength { get; private set; }

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = pathColor;
        lineRenderer.endColor = pathColor;
        lineRenderer.numCapVertices = 8;
        lineRenderer.numCornerVertices = 8;

        GeneratePath();
    }

    public void GeneratePath()
    {
        sampledPoints.Clear();
        cumulativeDistances.Clear();
        TotalLength = 0f;

        if (controlPoints.Count < 2)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            Vector3 p0 = controlPoints[Mathf.Max(i - 1, 0)].position;
            Vector3 p1 = controlPoints[i].position;
            Vector3 p2 = controlPoints[i + 1].position;
            Vector3 p3 = controlPoints[
                Mathf.Min(i + 2, controlPoints.Count - 1)
            ].position;

            for (int j = 0; j < samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;

                Vector3 point = CatmullRom(
                    p0, p1, p2, p3, t
                );

                AddSample(point);
            }
        }

        AddSample(controlPoints[controlPoints.Count - 1].position);

        lineRenderer.positionCount = sampledPoints.Count;
        lineRenderer.SetPositions(sampledPoints.ToArray());
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
}
