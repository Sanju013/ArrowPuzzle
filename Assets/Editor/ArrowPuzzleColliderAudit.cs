using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Audits all 28 runtime circular arrowhead hitboxes.</summary>
public static class ArrowPuzzleColliderAudit
{
    private const string Key = "ArrowPuzzle.ColliderAudit.Pending";
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private static bool completed;

    [MenuItem("Tools/Arrow Puzzle/Audit Runtime Colliders")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        completed = false;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode &&
            SessionState.GetBool(Key, false))
        {
            completed = false;
            EditorApplication.update -= AuditWhenReady;
            EditorApplication.update += AuditWhenReady;
        }
    }

    private static void AuditWhenReady()
    {
        if (completed || !Application.isPlaying)
            return;

        ArrowLineController[] lines =
            UnityEngine.Object.FindObjectsByType<ArrowLineController>(
                FindObjectsSortMode.None);
        if (lines.Length != 14)
            return;

        completed = true;
        EditorApplication.update -= AuditWhenReady;
        SessionState.SetBool(Key, false);
        Array.Sort(lines, (a, b) => string.CompareOrdinal(a.name, b.name));
        Physics2D.SyncTransforms();

        int audited = 0;
        int failures = 0;
        foreach (ArrowLineController line in lines)
        {
            Audit(line, line.GetStartArrowhead(), ref audited, ref failures);
            Audit(line, line.GetEndArrowhead(), ref audited, ref failures);
        }

        Debug.Log("COLLIDERAUDIT SUMMARY heads=" + audited +
                  ", failures=" + failures +
                  ", screen=" + Screen.width + "x" + Screen.height);
        if (audited != 28 || failures != 0)
            Debug.LogError("COLLIDERAUDIT FAILED");
        else
            Debug.Log("COLLIDERAUDIT PASS all runtime heads use centered, consistently sized circles");

        EditorApplication.isPlaying = false;
        EditorApplication.Exit(failures == 0 && audited == 28 ? 0 : 3);
    }

    private static void Audit(
        ArrowLineController line,
        ArrowheadController head,
        ref int audited,
        ref int failures)
    {
        SpriteRenderer renderer = head != null
            ? head.GetComponent<SpriteRenderer>() : null;
        CircleCollider2D collider = head != null
            ? head.GetComponent<CircleCollider2D>() : null;
        if (renderer == null || renderer.sprite == null || collider == null)
        {
            failures++;
            Debug.LogError("COLLIDERAUDIT missing sprite/collider/path: " +
                           line.name + "/" + (head != null ? head.name : "null"));
            return;
        }

        float worldRadius = ArrowheadController.GetWorldCircleRadius(collider);
        float visibleWidth = renderer.sprite.bounds.size.x *
                             Mathf.Abs(renderer.transform.lossyScale.x);
        float visibleHeight = renderer.sprite.bounds.size.y *
                              Mathf.Abs(renderer.transform.lossyScale.y);
        float radiusToTriangleWidth = visibleWidth > 0f
            ? worldRadius / visibleWidth : float.PositiveInfinity;
        Vector2 worldCenter = ArrowheadController.GetWorldCircleCenter(collider);
        Vector2 spriteCentroid = Vector2.zero;
        Vector2[] spriteVertices = renderer.sprite.vertices;
        foreach (Vector2 vertex in spriteVertices)
        {
            Vector2 visibleVertex = vertex;
            if (renderer.flipX) visibleVertex.x = -visibleVertex.x;
            if (renderer.flipY) visibleVertex.y = -visibleVertex.y;
            spriteCentroid += (Vector2)renderer.transform.TransformPoint(visibleVertex);
        }
        if (spriteVertices.Length > 0)
            spriteCentroid /= spriteVertices.Length;
        float centerError = Vector2.Distance(worldCenter, spriteCentroid);
        ArrowPath path = line.GetArrowPath();
        bool fromStart = (bool)typeof(ArrowheadController).GetField(
            "movesFromStart", System.Reflection.BindingFlags.Instance |
                             System.Reflection.BindingFlags.NonPublic)
            .GetValue(head);
        float endpointT = fromStart ? 0f : 1f;
        Vector3 endpoint = path.GetPointAtNormalizedDistance(endpointT);
        Vector3 expectedDirection = path.GetTangentAtNormalizedDistance(endpointT) *
                                    (fromStart ? 1f : -1f);
        float endpointGap = Vector3.Distance(head.transform.position, endpoint);
        float tangentDot = Vector3.Dot(head.transform.up.normalized,
                                       expectedDirection.normalized);
        bool valid = collider.enabled && collider.isTrigger &&
                     collider.offset.sqrMagnitude <= 0.00000001f &&
                     Mathf.Abs(collider.radius - 0.35f) <= 0.0001f &&
                     radiusToTriangleWidth >= 0.35f &&
                     radiusToTriangleWidth <= 0.45f && centerError <= 0.0001f &&
                     endpointGap <= 0.0001f && tangentDot > 0.995f;
        if (!valid)
            failures++;

        Vector2 intendedMove = FirstMovementVector(head);
        float moveDot = intendedMove.sqrMagnitude > 0.000001f
            ? Vector2.Dot(head.transform.up.normalized, intendedMove.normalized)
            : 0f;
        audited++;
        Debug.Log("COLLIDERAUDIT " + line.name + "/" + head.name +
                  " localRadius=" + collider.radius.ToString("F4") +
                  " worldRadius=" + worldRadius.ToString("F5") +
                  " visibleTriangle=" + visibleWidth.ToString("F5") + "x" +
                  visibleHeight.ToString("F5") +
                  " radius/visibleWidth=" + radiusToTriangleWidth.ToString("F3") +
                  " centerDelta=" + centerError.ToString("F7") +
                  " endpointGap=" + endpointGap.ToString("F7") +
                  " tipVsTangentDot=" + tangentDot.ToString("F5") +
                  " offset=" + collider.offset +
                  " localRotation=" + head.transform.localEulerAngles.z.ToString("F2") +
                  " worldRotation=" + head.transform.eulerAngles.z.ToString("F2") +
                  " scale=" + head.transform.lossyScale +
                  " tipVsMoveDot=" + moveDot.ToString("F3") +
                  " isTrigger=" + collider.isTrigger + " centeredCircleFit=" + valid);
    }

    private static Vector2 FirstMovementVector(ArrowheadController head)
    {
        bool fromStart = (bool)typeof(ArrowheadController).GetField(
            "movesFromStart", System.Reflection.BindingFlags.Instance |
                             System.Reflection.BindingFlags.NonPublic)
            .GetValue(head);
        ArrowPath path = head.GetLineController().GetArrowPath();
        var route = path.GetRoutePoints(fromStart ? 0f : 1f,
                                        fromStart ? 1f : 0f);
        Vector2 connector = route.Count > 0
            ? (Vector2)(route[0] - head.transform.position) : Vector2.zero;
        if (connector.sqrMagnitude > 0.000001f)
            return connector;
        return route.Count > 1 ? (Vector2)(route[1] - route[0]) : Vector2.zero;
    }

}
