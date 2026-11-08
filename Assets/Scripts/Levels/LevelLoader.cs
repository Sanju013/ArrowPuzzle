using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Builds the playable level from the validated tracing JSON at runtime.
/// The loader is data driven: it reads the colour groups, spline control
/// points and arrowhead metadata from assets instead of hard-coding paths.
/// </summary>
public class LevelLoader : MonoBehaviour
{
    [Serializable]
    private struct ArrowheadSpawnAdjustment
    {
        public string colorName;
        public int pathIndex;
        public int headIndex;
        public Vector2 sourcePixelOffset;
    }

    [Header("Level Data")]
    [SerializeField] private TextAsset levelJson;
    [SerializeField] private TextAsset arrowheadJson;

    [Header("Scene References")]
    [SerializeField] private SpriteRenderer referenceOverlay;
    [SerializeField] private Transform levelRoot;
    [SerializeField] private GameManager gameManager;

    [Header("Arrowheads")]
    [SerializeField] private Sprite arrowheadSprite;
    [SerializeField] private float arrowheadScale = 0.25f;
    [SerializeField, Range(0.35f, 0.45f)] private float arrowheadCollisionRadius = 0.35f;
    [SerializeField] private float arrowheadZ = -0.1f;
    [SerializeField] private float arrowheadMoveSpeed = 2f;

    [Header("Gameplay Spawn Adjustments")]
    [Tooltip("Small per-head placement corrections in source-image pixels. " +
             "These do not modify the traced paths or source JSON.")]
    [SerializeField] private List<ArrowheadSpawnAdjustment> arrowheadSpawnAdjustments =
        new List<ArrowheadSpawnAdjustment>();

    [Header("Paths")]
    [SerializeField] private float lineWidth = 0.06f;
    [SerializeField] private int samplesPerSegment = 20;

    private readonly Dictionary<string, float> arrowheadRotations =
        new Dictionary<string, float>();

    private readonly List<ArrowLineController> spawnedLines =
        new List<ArrowLineController>();

    private bool hasBuilt;

    public IReadOnlyList<ArrowLineController> SpawnedLines => spawnedLines;

    private void Awake()
    {
        BuildLevel();
    }

    /// <summary>
    /// Creates every path and arrowhead. Safe to call from the editor for
    /// visual validation, and from Awake at runtime.
    /// </summary>
    public void BuildLevel()
    {
        if (hasBuilt)
            return;

        if (levelJson == null)
        {
            Debug.LogError("LevelLoader: levelJson is not assigned.");
            return;
        }

        if (referenceOverlay == null || referenceOverlay.sprite == null)
        {
            Debug.LogError(
                "LevelLoader: referenceOverlay (with a sprite) is not assigned.");
            return;
        }

        if (arrowheadSprite == null)
        {
            Debug.LogError("LevelLoader: arrowheadSprite is not assigned.");
            return;
        }

        Transform root = levelRoot != null ? levelRoot : transform;

        Dictionary<string, object> document;
        try
        {
            document = MiniJson.Parse(levelJson.text)
                as Dictionary<string, object>;
        }
        catch (Exception exception)
        {
            Debug.LogError("LevelLoader: failed to parse level JSON. " +
                           exception.Message);
            return;
        }

        if (document == null ||
            !(document.TryGetValue("colors", out object colorsValue) &&
              colorsValue is Dictionary<string, object> colors))
        {
            Debug.LogError("LevelLoader: level JSON has no 'colors' object.");
            return;
        }

        LoadArrowheadRotations();

        int pathCount = 0;
        int arrowheadCount = 0;

        foreach (KeyValuePair<string, object> colorEntry in colors)
        {
            string colorName = colorEntry.Key;

            if (!(colorEntry.Value is Dictionary<string, object> colorData))
                continue;

            if (!colorData.TryGetValue("target", out object targetValue) ||
                !(targetValue is List<object> target) ||
                target.Count < 3)
            {
                Debug.LogWarning(
                    "LevelLoader: colour '" + colorName + "' has no target.");
                continue;
            }

            Color color = new Color(
                ToFloat(target[0]) / 255f,
                ToFloat(target[1]) / 255f,
                ToFloat(target[2]) / 255f,
                1f);

            if (!colorData.TryGetValue("results", out object resultsValue) ||
                !(resultsValue is List<object> results))
            {
                continue;
            }

            for (int pathIndex = 0; pathIndex < results.Count; pathIndex++)
            {
                if (!(results[pathIndex] is Dictionary<string, object> result))
                    continue;

                if (!TryReadControlPoints(result, out List<Vector3> worldPoints))
                {
                    Debug.LogWarning(
                        "LevelLoader: " + colorName + "[" + pathIndex +
                        "] has no usable control points.");
                    continue;
                }

                if (!TryReadHeadPair(result, out Vector2 head0, out Vector2 head1))
                {
                    Debug.LogWarning(
                        "LevelLoader: " + colorName + "[" + pathIndex +
                        "] has no usable head pair.");
                    continue;
                }

                ArrowLineController line = CreateLine(
                    root, colorName, pathIndex, color,
                    worldPoints, head0, head1);

                spawnedLines.Add(line);
                pathCount++;
                arrowheadCount += 2;
            }
        }

        hasBuilt = true;

        Debug.Log("LevelLoader: built " + pathCount + " paths and " +
                  arrowheadCount + " arrowheads.");
    }

    private ArrowLineController CreateLine(
        Transform root,
        string colorName,
        int pathIndex,
        Color color,
        List<Vector3> worldPoints,
        Vector2 startHeadPixels,
        Vector2 endHeadPixels)
    {
        GameObject lineObject = new GameObject(
            "Line_" + colorName + "_" + pathIndex);
        lineObject.transform.SetParent(root, false);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        lineObject.transform.localScale = Vector3.one;

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 8;
        lineRenderer.numCornerVertices = 8;
        lineRenderer.sortingOrder = -1;

        Material lineMaterial = referenceOverlay != null
            ? referenceOverlay.sharedMaterial
            : null;

        if (lineMaterial != null)
            lineRenderer.sharedMaterial = lineMaterial;

        ArrowPath path = lineObject.AddComponent<ArrowPath>();
        path.Configure(color, lineWidth, samplesPerSegment, worldPoints);

        ArrowLineController line =
            lineObject.AddComponent<ArrowLineController>();

        Vector3 startWorld = PixelToWorld(startHeadPixels) +
            PixelOffsetToWorld(GetSpawnOffset(colorName, pathIndex, 0));
        Vector3 endWorld = PixelToWorld(endHeadPixels) +
            PixelOffsetToWorld(GetSpawnOffset(colorName, pathIndex, 1));

        ArrowheadController startArrowhead = CreateArrowhead(
            lineObject.transform, "Arrowhead_Start", color,
            startWorld, GetArrowheadRotation(colorName, pathIndex, 0),
            true, path, line);

        ArrowheadController endArrowhead = CreateArrowhead(
            lineObject.transform, "Arrowhead_End", color,
            endWorld, GetArrowheadRotation(colorName, pathIndex, 1),
            false, path, line);

        startArrowhead.SetOpposite(endArrowhead);
        endArrowhead.SetOpposite(startArrowhead);

        line.Configure(path, startArrowhead, endArrowhead, gameManager);

        return line;
    }

    private ArrowheadController CreateArrowhead(
        Transform parent,
        string name,
        Color color,
        Vector3 worldPosition,
        float rotationZ,
        bool movesFromStart,
        ArrowPath path,
        ArrowLineController line)
    {
        GameObject arrowObject = new GameObject(name);
        arrowObject.transform.SetParent(parent, false);

        Vector3 localPosition = parent.InverseTransformPoint(worldPosition);
        localPosition.z = arrowheadZ;

        arrowObject.transform.localPosition = localPosition;
        arrowObject.transform.localRotation =
            Quaternion.Euler(0f, 0f, rotationZ);
        arrowObject.transform.localScale =
            Vector3.one * arrowheadScale;

        SpriteRenderer renderer = arrowObject.AddComponent<SpriteRenderer>();
        renderer.sprite = arrowheadSprite;
        renderer.color = color;
        renderer.sortingOrder = 0;

        Rigidbody2D body = arrowObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = true;
        body.useFullKinematicContacts = true;
        body.gravityScale = 0f;

        CircleCollider2D collider =
            arrowObject.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.offset = Vector2.zero;
        collider.radius = arrowheadCollisionRadius;

        ArrowheadController arrowhead =
            arrowObject.AddComponent<ArrowheadController>();

        arrowhead.Configure(
            path, line, gameManager, movesFromStart, arrowheadMoveSpeed);

        return arrowhead;
    }

    // ------------------------------------------------------------------
    // Coordinate conversion
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts a source-image pixel coordinate (origin top-left, pixel
    /// centres at integer + 0.5) into world space. The sprite is centered
    /// and scaled to fit the camera view while preserving aspect ratio.
    /// </summary>
    private Vector3 PixelToWorld(Vector2 pixel)
    {
        Sprite sprite = referenceOverlay.sprite;
        float pixelsPerUnit = sprite.pixelsPerUnit;
        float spriteWidth = sprite.rect.width / pixelsPerUnit;
        float spriteHeight = sprite.rect.height / pixelsPerUnit;

        // Local sprite space, centered at origin
        float localX = (pixel.x - sprite.rect.width * 0.5f) / pixelsPerUnit;
        float localY = -(pixel.y - sprite.rect.height * 0.5f) / pixelsPerUnit;

        // Scale to fit camera view preserving aspect ratio
        Camera cam = Camera.main;
        float cameraHeight = cam.orthographicSize * 2f;
        float cameraWidth = cameraHeight * cam.aspect;
        float scale = cameraHeight / spriteHeight;
        if (spriteWidth * scale > cameraWidth)
            scale = cameraWidth / spriteWidth;

        return new Vector3(localX * scale, localY * scale, 0f);
    }

    private Vector3 PixelOffsetToWorld(Vector2 pixelOffset)
    {
        Sprite sprite = referenceOverlay.sprite;
        float pixelsPerUnit = sprite.pixelsPerUnit;
        float spriteWidth = sprite.rect.width / pixelsPerUnit;
        float spriteHeight = sprite.rect.height / pixelsPerUnit;

        Camera cam = Camera.main;
        float cameraHeight = cam.orthographicSize * 2f;
        float cameraWidth = cameraHeight * cam.aspect;
        float scale = cameraHeight / spriteHeight;
        if (spriteWidth * scale > cameraWidth)
            scale = cameraWidth / spriteWidth;

        return new Vector3(
            pixelOffset.x / pixelsPerUnit * scale,
            -pixelOffset.y / pixelsPerUnit * scale,
            0f);
    }

    private Vector2 GetSpawnOffset(
        string colorName, int pathIndex, int headIndex)
    {
        foreach (ArrowheadSpawnAdjustment adjustment in arrowheadSpawnAdjustments)
        {
            if (adjustment.colorName == colorName &&
                adjustment.pathIndex == pathIndex &&
                adjustment.headIndex == headIndex)
            {
                return adjustment.sourcePixelOffset;
            }
        }

        return Vector2.zero;
    }

    // ------------------------------------------------------------------
    // Data parsing
    // ------------------------------------------------------------------

    private bool TryReadControlPoints(
        Dictionary<string, object> result,
        out List<Vector3> worldPoints)
    {
        worldPoints = new List<Vector3>();

        if (!result.TryGetValue(
                "control_points", out object controlPointsValue) ||
            !(controlPointsValue is List<object> controlPoints) ||
            controlPoints.Count < 2)
        {
            return false;
        }

        for (int i = 0; i < controlPoints.Count; i++)
        {
            if (!(controlPoints[i] is List<object> point) || point.Count < 2)
            {
                worldPoints.Clear();
                return false;
            }

            Vector2 pixel = new Vector2(
                ToFloat(point[0]), ToFloat(point[1]));

            worldPoints.Add(PixelToWorld(pixel));
        }

        return true;
    }

    private static bool TryReadHeadPair(
        Dictionary<string, object> result,
        out Vector2 head0,
        out Vector2 head1)
    {
        head0 = Vector2.zero;
        head1 = Vector2.zero;

        if (!result.TryGetValue("pair", out object pairValue) ||
            !(pairValue is List<object> pair) ||
            pair.Count < 2)
        {
            return false;
        }

        if (!(pair[0] is List<object> first) || first.Count < 2 ||
            !(pair[1] is List<object> second) || second.Count < 2)
        {
            return false;
        }

        head0 = new Vector2(ToFloat(first[0]), ToFloat(first[1]));
        head1 = new Vector2(ToFloat(second[0]), ToFloat(second[1]));
        return true;
    }

    private void LoadArrowheadRotations()
    {
        arrowheadRotations.Clear();

        if (arrowheadJson == null)
            return;

        Dictionary<string, object> document;

        try
        {
            document = MiniJson.Parse(arrowheadJson.text)
                as Dictionary<string, object>;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "LevelLoader: failed to parse arrowhead JSON. " +
                exception.Message);
            return;
        }

        if (document == null ||
            !document.TryGetValue("arrowheads", out object listValue) ||
            !(listValue is List<object> list))
        {
            return;
        }

        foreach (object entry in list)
        {
            if (!(entry is Dictionary<string, object> data))
                continue;

            if (!data.TryGetValue("color", out object colorValue) ||
                !(colorValue is string colorName))
            {
                continue;
            }

            int pathIndex = data.TryGetValue("path", out object pathValue)
                ? Mathf.RoundToInt(ToFloat(pathValue))
                : 0;

            int headIndex = data.TryGetValue("head", out object headValue)
                ? Mathf.RoundToInt(ToFloat(headValue))
                : 0;

            if (!data.TryGetValue("rotation", out object rotationValue))
                continue;

            string key = RotationKey(colorName, pathIndex, headIndex);
            arrowheadRotations[key] = ToFloat(rotationValue);
        }
    }

    private float GetArrowheadRotation(
        string colorName, int pathIndex, int headIndex)
    {
        string key = RotationKey(colorName, pathIndex, headIndex);

        if (arrowheadRotations.TryGetValue(key, out float rotation))
            return rotation;

        Debug.LogWarning("LevelLoader: no arrowhead rotation for " + key +
                         "; using 0.");
        return 0f;
    }

    private static string RotationKey(
        string colorName, int pathIndex, int headIndex)
    {
        return colorName + ":" + pathIndex + ":" + headIndex;
    }

    private static float ToFloat(object value)
    {
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }
}
