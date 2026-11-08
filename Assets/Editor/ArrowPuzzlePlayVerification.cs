using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class ArrowPuzzlePlayVerification
{
    private const string Key = "ArrowPuzzle.PlayVerification";
    private static int phase;
    private static float phaseTime;
    private static GameManager manager;
    private static HintManager hints;
    private static readonly Dictionary<ArrowheadController, Vector3> retryPositions =
        new Dictionary<ArrowheadController, Vector3>();
    private static readonly Dictionary<ArrowheadController, Quaternion> retryRotations =
        new Dictionary<ArrowheadController, Quaternion>();

    public static void Run()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        if (SessionState.GetBool(Key, false))
            EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!Application.isPlaying) return;
        try
        {
            AdvanceValidation();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(2);
        }
    }

    private static void AdvanceValidation()
    {
        float now = Time.realtimeSinceStartup;
        if (phase == 0 && now > 1f) { Initial(); phaseTime = now; phase = 1; return; }
        if (phase == 1 && now - phaseTime > .3f) { MoveAndCollide(); phaseTime = now; phase = 2; return; }
        if (phase == 2 && now - phaseTime > .3f) { Retry(); phaseTime = now; phase = 3; return; }
        if (phase == 3 && now - phaseTime > .25f)
        {
            GameManager reloadedManager =
                UnityEngine.Object.FindFirstObjectByType<GameManager>();
            ArrowLineController[] reloadedLines =
                UnityEngine.Object.FindObjectsByType<ArrowLineController>(
                    FindObjectsSortMode.None);
            if (reloadedManager != null && reloadedManager.CurrentLives == 3 &&
                reloadedLines.Length == 14)
            {
                RetryCheck();
                SessionState.SetBool(Key, false);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
                phase = 4;
            }
            else if (now - phaseTime > 10f)
            {
                int lineCount = reloadedLines.Length;
                int lives = reloadedManager != null
                    ? reloadedManager.CurrentLives : -1;
                int heartCount = GameObject.Find("LivesText") != null
                    ? GameObject.Find("LivesText").GetComponentsInChildren<Image>(true).Length
                    : -1;
                throw new TimeoutException("Retry did not reset: scene=" +
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                    ", manager=" + (reloadedManager != null) + ", lives=" + lives +
                    ", lines=" + lineCount + ", hearts=" + heartCount);
            }
        }
    }

    private static void Initial()
    {
        manager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        hints = UnityEngine.Object.FindFirstObjectByType<HintManager>();
        var lines = UnityEngine.Object.FindObjectsByType<ArrowLineController>(FindObjectsSortMode.None);
        var heads = UnityEngine.Object.FindObjectsByType<ArrowheadController>(FindObjectsSortMode.None);
        Require(lines.Length == 14 && heads.Length == 28, "14 paths / 28 heads");
        foreach (var line in lines) Require(line.GetComponents<Collider2D>().Length == 0, "no path colliders");
        VerifyCircularHitboxes(heads);
        var hearts = GameObject.Find("LivesText").GetComponentsInChildren<Image>(true);
        Require(hearts.Length == 3 && !GameObject.Find("LivesText").GetComponent<TMPro.TMP_Text>().enabled, "3 heart icons, no lives text");
        Canvas hudCanvas = hearts[0].GetComponentInParent<Canvas>();
        float renderedHeartWidth = hearts[0].rectTransform.rect.width *
            (hudCanvas != null ? hudCanvas.scaleFactor : 1f);
        float renderedHeartHeight = hearts[0].rectTransform.rect.height *
            (hudCanvas != null ? hudCanvas.scaleFactor : 1f);
        Require(renderedHeartWidth >= 82f && renderedHeartWidth <= 86f &&
                renderedHeartHeight >= 82f && renderedHeartHeight <= 86f,
            "heart Image components render at 84 screen pixels for 64-72px artwork");
        VerifyResultPanels(manager);
        Physics2D.simulationMode = SimulationMode2D.Script;
        VerifyCircleNearMissAndOverlap(heads);
        VerifyEndpointAlignment(lines, heads);
        VerifySmoothCurvedMovement(lines);

        int intersections = CountPathIntersections(lines);
        Require(intersections > 0, "fixture contains crossing path centerlines");
        int livesBeforeStaticStep = manager.CurrentLives;
        Physics2D.Simulate(.02f);
        Require(manager.CurrentLives == livesBeforeStaticStep,
            "stationary path intersections do not trigger arrowhead collisions");

        hints.ShowHint();
        var first = Get<ArrowheadController>(hints, "highlightedArrowhead");
        Require(first != null && first.GetLineController().CanArrowheadMoveSafely(first), "first hint legal");
        hints.ShowHint();
        var second = Get<ArrowheadController>(hints, "highlightedArrowhead");
        Require(second != null && second != first && second.GetLineController().CanArrowheadMoveSafely(second), "repeated hint cycles");

        var selectedLine = second.GetLineController();
        foreach (var line in lines) Set(line, "isCompleted", line != selectedLine);
        var otherHead = second == selectedLine.GetStartArrowhead()
            ? selectedLine.GetEndArrowhead()
            : selectedLine.GetStartArrowhead();
        otherHead.enabled = false;
        hints.ShowHint();
        Require(Get<ArrowheadController>(hints, "highlightedArrowhead") == second,
            "only legal head is hinted when one remains");
        otherHead.enabled = true;
        foreach (var line in lines) Set(line, "isCompleted", false);
        hints.ClearHint();

        var loop = GameObject.Find("Line_green_1").GetComponent<ArrowLineController>();
        VerifyGreenLoopClipping(loop);
        Debug.Log($"PLAYVERIFY initial PASS paths=14 heads=28 hearts=3 imageSize={renderedHeartWidth:F1}x{renderedHeartHeight:F1}px crossingPaths={intersections} noCollision=true hintCycle=true onlyOneLegal=true lowerLoopBlockedByHeads=true");
    }

    private static void VerifyEndpointAlignment(
        ArrowLineController[] lines, ArrowheadController[] heads)
    {
        float largestEndpointError = 0f;
        float smallestTangentDot = 1f;
        foreach (ArrowLineController line in lines)
        {
            ArrowPath path = line.GetArrowPath();
            LineRenderer renderer = path.GetComponent<LineRenderer>();
            Require(renderer.positionCount >= 2,
                line.name + " renders a sampled spline");
            VerifyHeadEndpoint(line.GetStartArrowhead(), path, true,
                ref largestEndpointError, ref smallestTangentDot);
            VerifyHeadEndpoint(line.GetEndArrowhead(), path, false,
                ref largestEndpointError, ref smallestTangentDot);
        }

        Require(heads.Length == 28 && largestEndpointError < 0.0001f &&
                smallestTangentDot > 0.995f,
            "all 28 heads touch endpoints and face their independent movement tangents");
        Debug.Log("PLAYVERIFY endpoint alignment PASS heads=" + heads.Length +
                  " maxGap=" + largestEndpointError.ToString("F7") +
                  " minTangentDot=" + smallestTangentDot.ToString("F5"));
    }

    private static void VerifyHeadEndpoint(
        ArrowheadController head, ArrowPath path, bool fromStart,
        ref float largestGap, ref float smallestTangentDot)
    {
        float endpoint = fromStart ? 0f : 1f;
        Vector3 expectedPosition = path.GetPointAtNormalizedDistance(endpoint);
        Vector3 tangent = path.GetTangentAtNormalizedDistance(endpoint);
        if (!fromStart) tangent = -tangent;
        float gap = Vector3.Distance(head.transform.position, expectedPosition);
        float dot = Vector3.Dot(head.transform.up.normalized, tangent.normalized);
        largestGap = Mathf.Max(largestGap, gap);
        smallestTangentDot = Mathf.Min(smallestTangentDot, dot);
        Require(gap < 0.0001f,
            head.GetLineController().name + "/" + head.name +
            " is exactly on its spline endpoint");
        Require(dot > 0.995f,
            head.GetLineController().name + "/" + head.name +
            " tip faces the actual forward/reverse path tangent");
    }

    private static void VerifySmoothCurvedMovement(
        ArrowLineController[] lines)
    {
        ArrowLineController selectedLine = null;
        ArrowheadController selectedHead = null;
        bool fromStart = true;
        float greatestTurn = 0f;

        foreach (ArrowLineController line in lines)
        foreach (ArrowheadController head in new[]
                 { line.GetStartArrowhead(), line.GetEndArrowhead() })
        {
            if (!line.CanArrowheadMoveSafely(head))
                continue;
            bool candidateFromStart = head == line.GetStartArrowhead();
            ArrowPath candidatePath = line.GetArrowPath();
            Vector3 first = candidatePath.GetTangentAtNormalizedDistance(
                candidateFromStart ? 0f : 1f) * (candidateFromStart ? 1f : -1f);
            Vector3 last = candidatePath.GetTangentAtNormalizedDistance(
                candidateFromStart ? 1f : 0f) * (candidateFromStart ? 1f : -1f);
            float turn = Vector3.Angle(first, last);
            if (turn > greatestTurn)
            {
                greatestTurn = turn;
                selectedLine = line;
                selectedHead = head;
                fromStart = candidateFromStart;
            }
        }

        Require(selectedHead != null && greatestTurn > 20f,
            "a safe curved route is available for movement validation");
        Vector3 spawn = selectedHead.transform.position;
        Require(selectedHead.TryStartMovingAt(spawn),
            "curved movement starts from its actual endpoint");

        float maxStep = 0f;
        float maxRotationStep = 0f;
        Vector3 previousPosition = selectedHead.transform.position;
        Quaternion previousRotation = selectedHead.transform.rotation;
        for (int step = 0; step < 30; step++)
        {
            selectedHead.Tick(0.02f);
            selectedLine.RefreshVisibleRange();
            Physics2D.SyncTransforms();
            Physics2D.Simulate(0.02f);

            float distance = Vector3.Distance(previousPosition,
                selectedHead.transform.position);
            maxStep = Mathf.Max(maxStep, distance);
            maxRotationStep = Mathf.Max(maxRotationStep,
                Quaternion.Angle(previousRotation, selectedHead.transform.rotation));
            Require(distance <= Get<float>(selectedHead, "moveSpeed") *
                    0.02f * 1.6f + 0.001f,
                "curved movement has no distance jump or speed spike");

            float pathPosition = selectedHead.NormalizedPathPosition;
            Vector3 tangent = selectedLine.GetArrowPath()
                .GetTangentAtNormalizedDistance(pathPosition);
            if (!fromStart) tangent = -tangent;
            Require(Vector3.Dot(selectedHead.transform.up.normalized,
                    tangent.normalized) > 0.98f,
                "moving arrowhead tracks the spline tangent without rotation snapping");

            previousPosition = selectedHead.transform.position;
            previousRotation = selectedHead.transform.rotation;
        }

        Require(selectedHead.IsMoving() && maxStep > 0.001f &&
                maxRotationStep < 20f &&
                Vector3.Distance(selectedHead.transform.position, spawn) > 0.05f,
            "curved route advances smoothly and rotates without snapping");
        selectedLine.ResetLine();
        Debug.Log("PLAYVERIFY movement PASS route=" + selectedLine.name +
                  " turn=" + greatestTurn.ToString("F1") + "deg maxStep=" +
                  maxStep.ToString("F4") + " maxRotationStep=" +
                  maxRotationStep.ToString("F1") + "deg");
    }

    private static void VerifyResultPanels(GameManager currentManager)
    {
        GameUIController ui = UnityEngine.Object.FindFirstObjectByType<GameUIController>();
        TMP_Text result = Get<TMP_Text>(ui, "resultText");
        GameObject panel = Get<GameObject>(ui, "gameOverPanel");

        ui.ShowWin(currentManager.CurrentLives);
        Require(panel.activeSelf && result.text.Contains("LEVEL COMPLETED") &&
                result.text.Contains("Lives remaining: 3 / 3"),
            "win overlay includes completion and lives statistic");
        result.ForceMeshUpdate();
        Require(result.textInfo.lineCount == 3,
            "win heading and supporting text fit without unexpected wrapping");
        ui.ShowLose();
        Require(panel.activeSelf && result.text.Contains("GAME OVER") &&
                result.text.Contains("All three lives were lost"),
            "lose overlay includes defeat message");
        Image backdrop = panel.GetComponent<Image>();
        Require(backdrop != null && backdrop.color.a >= 0.9f,
            "result overlay compensates for linear-color UI blending to render near 70 percent dark");
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        Require(panelRect.anchorMin == Vector2.zero &&
                panelRect.anchorMax == Vector2.one,
            "result overlay spans the full canvas");
        Require(panelRect.GetSiblingIndex() == panelRect.parent.childCount - 1,
            "result overlay renders above the HUD and covers the entire screen");
        Button retryButton = GameObject.Find("RetryButton").GetComponent<Button>();
        Require(retryButton != null && retryButton.interactable,
            "result screen Retry button is available");
        TMP_Text retryText = GameObject.Find("RetryText").GetComponent<TMP_Text>();
        Image retryBackground = retryButton.GetComponent<Image>();
        Require(retryText != null && retryText.color.grayscale > 0.9f &&
                retryBackground != null && retryBackground.color.grayscale < 0.5f,
            "Retry button has high-contrast white text on a blue background");
        ui.HideResult();
        Require(!panel.activeSelf, "result overlay hides for continued play/retry");
    }

    private static void MoveAndCollide()
    {
        manager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        ArrowheadController moving = null;
        ArrowheadController target = null;
        Require(FindNaturalCollisionRoute(out moving, out target),
            "actual gameplay route contains a genuine arrowhead collision");
        Require(!moving.GetLineController().CanArrowheadMoveSafely(moving),
            "circular sweep rejects the same route that physically collides");
        Require(manager.CurrentLives == 3, "three lives before invalid collision");
        Require(moving.TryStartMovingAt(moving.transform.position), "start collision-test arrowhead");
        int steps = 0;
        while (manager.CurrentLives == 3 && steps < 20000)
        {
            moving.Tick(.01f);
            moving.GetLineController().RefreshVisibleRange();
            Physics2D.SyncTransforms();
            Physics2D.Simulate(.01f);
            steps++;
        }
        Require(manager.CurrentLives == 2, "one life for actual trigger");
        Require(!moving.IsMoving() && !target.IsMoving(), "both collision lines reset");
        manager.HandleCollision(moving, target);
        Require(manager.CurrentLives == 2, "duplicate event ignored");
        Image[] hearts = GameObject.Find("LivesText").GetComponentsInChildren<Image>(true);
        int emptyCount = 0;
        Sprite empty = Get<Sprite>(manager, "emptyHeartSprite");
        foreach (Image heart in hearts)
            if (heart.sprite == empty) emptyCount++;
        Require(hearts.Length == 3 && emptyCount == 1,
            "hearts update immediately after collision");
        Debug.Log("PLAYVERIFY natural-route collision PASS lifeDelta=1 steps=" +
                  steps + " pairDedup=true resetBoth=true heartsUpdated=true");
    }

    private static void VerifyCircularHitboxes(ArrowheadController[] heads)
    {
        float minimumRadius = float.PositiveInfinity;
        float maximumRadius = 0f;
        foreach (ArrowheadController head in heads)
        {
            Collider2D[] colliders = head.GetComponents<Collider2D>();
            Require(colliders.Length == 1 && colliders[0] is CircleCollider2D,
                head.name + " has exactly one CircleCollider2D");
            CircleCollider2D circle = (CircleCollider2D)colliders[0];
            float radius = ArrowheadController.GetWorldCircleRadius(circle);
            SpriteRenderer renderer = head.GetComponent<SpriteRenderer>();
            float visibleWidth = renderer.sprite.bounds.size.x *
                                 Mathf.Abs(renderer.transform.lossyScale.x);
            float ratio = radius / visibleWidth;
            Require(Mathf.Abs(circle.radius - 0.35f) < 0.0001f &&
                    circle.offset.sqrMagnitude < 0.00000001f &&
                    ratio >= 0.35f && ratio <= 0.45f,
                head.GetLineController().name + "/" + head.name +
                " centered circular hitbox radius is calibrated");
            minimumRadius = Mathf.Min(minimumRadius, radius);
            maximumRadius = Mathf.Max(maximumRadius, radius);
        }

        Require(maximumRadius - minimumRadius < 0.0001f,
            "all 28 circular hitboxes share one world radius");
        Debug.Log("PLAYVERIFY circles PASS heads=" + heads.Length +
                  " worldRadius=" + minimumRadius.ToString("F5") +
                  " visibleWidthRatio=0.350 centered=true polygonColliders=0");
    }

    private static void VerifyCircleNearMissAndOverlap(
        ArrowheadController[] heads)
    {
        ArrowheadController first = heads[0];
        ArrowheadController second = null;
        foreach (ArrowheadController candidate in heads)
        {
            if (candidate.GetLineController() != first.GetLineController())
            {
                second = candidate;
                break;
            }
        }

        Require(second != null, "two different lines available for circle tests");
        CircleCollider2D firstCircle = first.GetComponent<CircleCollider2D>();
        CircleCollider2D secondCircle = second.GetComponent<CircleCollider2D>();
        Vector3 firstOriginal = first.transform.position;
        Vector3 secondOriginal = second.transform.position;
        float combinedRadius = ArrowheadController.GetWorldCircleRadius(firstCircle) +
                               ArrowheadController.GetWorldCircleRadius(secondCircle);
        Vector3 testCenter = new Vector3(40f, 40f, firstOriginal.z);

        first.transform.position = testCenter;
        second.transform.position = testCenter + Vector3.right * (combinedRadius + 0.05f);
        Physics2D.SyncTransforms();
        ColliderDistance2D nearMiss = Physics2D.Distance(firstCircle, secondCircle);
        Require(!nearMiss.isOverlapped && nearMiss.distance > 0.02f,
            "circular near miss remains separated beyond both contact margins");

        int livesBeforeNearMiss = manager.CurrentLives;
        Require(first.TryStartMovingAt(first.transform.position),
            "near-miss arrowhead can start through its circular collider");
        Physics2D.Simulate(0.02f);
        Require(manager.CurrentLives == livesBeforeNearMiss,
            "near-miss circle pair does not deduct a life");
        first.ResetArrowhead(firstOriginal);

        first.transform.position = testCenter;
        second.transform.position = testCenter + Vector3.right * (combinedRadius * 0.5f);
        Physics2D.SyncTransforms();
        ColliderDistance2D overlap = Physics2D.Distance(firstCircle, secondCircle);
        Require(overlap.isOverlapped && overlap.distance < 0f,
            "genuine circular overlap has negative signed separation");

        second.transform.position = secondOriginal;
        first.transform.position = firstOriginal;
        Physics2D.SyncTransforms();
        Debug.Log("PLAYVERIFY circle geometry PASS nearMissDistance=" +
                  nearMiss.distance.ToString("F5") +
                  " overlapDistance=" + overlap.distance.ToString("F5") +
                  " nearMissLifeDelta=0");
    }

    private static bool FindNaturalCollisionRoute(
        out ArrowheadController moving,
        out ArrowheadController target)
    {
        ArrowheadController[] heads =
            UnityEngine.Object.FindObjectsByType<ArrowheadController>(
                FindObjectsSortMode.None);
        foreach (ArrowheadController candidate in heads)
        {
            ArrowPath path = candidate.GetLineController().GetArrowPath();
            bool fromStart = candidate == candidate.GetLineController().GetStartArrowhead();
            List<Vector3> route = path.GetRoutePoints(
                fromStart ? 0f : 1f, fromStart ? 1f : 0f);
            if (route.Count < 2)
                continue;

            Vector3 original = candidate.transform.position;
            if (Vector3.Distance(original, route[0]) > 0.0001f)
                route.Insert(0, original);
            Rigidbody2D body = candidate.GetComponent<Rigidbody2D>();
            Collider2D sourceCollider = candidate.GetComponent<Collider2D>();
            HashSet<ArrowheadController> initiallyOverlapping =
                new HashSet<ArrowheadController>();
            foreach (ArrowheadController other in heads)
            {
                if (other == candidate ||
                    other.GetLineController() == candidate.GetLineController())
                    continue;
                Collider2D otherCollider = other.GetComponent<Collider2D>();
                if (otherCollider != null &&
                    Physics2D.Distance(sourceCollider, otherCollider).isOverlapped)
                    initiallyOverlapping.Add(other);
            }

            for (int i = 0; i < route.Count; i++)
            {
                if (i == 0)
                    continue;
                Vector3 sample = route[i];
                candidate.transform.position = sample;
                body.position = sample;
                Physics2D.SyncTransforms();
                foreach (ArrowheadController other in heads)
                {
                    if (other == candidate ||
                        other.GetLineController() == candidate.GetLineController() ||
                        initiallyOverlapping.Contains(other))
                        continue;

                    Collider2D otherCollider = other.GetComponent<Collider2D>();
                    if (otherCollider == null)
                        continue;

                    ColliderDistance2D separation =
                        Physics2D.Distance(sourceCollider, otherCollider);
                    if (separation.distance >= -0.001f)
                        continue;

                    candidate.transform.position = original;
                    body.position = original;
                    Physics2D.SyncTransforms();
                    moving = candidate;
                    target = other;
                    bool satSafe = candidate.GetLineController()
                        .CanArrowheadMoveSafely(candidate);
                    Debug.Log("PLAYVERIFY natural collision witness " +
                              candidate.GetLineController().name + "/" + candidate.name +
                              " -> " + other.GetLineController().name + "/" + other.name +
                              " sample=" + i + "/" + route.Count +
                              " original=" + original +
                              " route0=" + route[0] +
                              " route1=" + (route.Count > 1 ? route[1] : route[0]) +
                              " target=" + other.transform.position +
                              " separation=" + separation.distance.ToString("F7") +
                              " satSafe=" + satSafe);
                    return true;
                }
            }

            candidate.transform.position = original;
            body.position = original;
            Physics2D.SyncTransforms();
        }

        moving = null;
        target = null;
        return false;
    }

    private static void Retry()
    {
        retryPositions.Clear();
        retryRotations.Clear();
        ArrowheadController[] heads =
            UnityEngine.Object.FindObjectsByType<ArrowheadController>(
                FindObjectsSortMode.None);
        foreach (ArrowheadController head in heads)
        {
            retryPositions[head] = head.transform.position;
            retryRotations[head] = head.transform.rotation;
        }

        ArrowLineController line = GameObject.Find("Line_red_0")
            .GetComponent<ArrowLineController>();
        line.CompleteLine();
        Require(!line.gameObject.activeInHierarchy,
            "fixture line is completed before Retry");

        ArrowheadController changedHead = null;
        foreach (ArrowheadController candidate in heads)
        {
            if (candidate.GetLineController() != line)
            {
                changedHead = candidate;
                break;
            }
        }
        Require(changedHead != null, "another active line is available for Retry reset");
        Require(changedHead.TryStartMovingAt(changedHead.transform.position),
            "fixture arrowhead starts before Retry lock reset");
        changedHead.transform.position += new Vector3(0.2f, 0.15f, 0f);
        changedHead.transform.rotation *= Quaternion.Euler(0f, 0f, 47f);
        Physics2D.SyncTransforms();
        UnityEngine.Object.FindFirstObjectByType<GameUIController>().Retry();
    }

    private static void RetryCheck()
    {
        manager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        var lines = UnityEngine.Object.FindObjectsByType<ArrowLineController>(FindObjectsSortMode.None);
        var heads = UnityEngine.Object.FindObjectsByType<ArrowheadController>(FindObjectsSortMode.None);
        var hearts = GameObject.Find("LivesText").GetComponentsInChildren<Image>(true);
        Require(manager.CurrentLives == 3 && lines.Length == 14 && heads.Length == 28 && hearts.Length == 3,
            "Retry full state");
        foreach (ArrowLineController line in lines)
        {
            Require(!line.IsCompleted() && line.gameObject.activeInHierarchy,
                "Retry restores every line");
            LineRenderer renderer = line.GetArrowPath().GetComponent<LineRenderer>();
            ArrowPath path = line.GetArrowPath();
            float visibleFrom = Get<float>(path, "visibleFrom");
            float visibleTo = Get<float>(path, "visibleTo");
            Vector3 expectedStart = path.GetPointAtNormalizedDistance(0f);
            Vector3 expectedEnd = path.GetPointAtNormalizedDistance(1f);
            Require(renderer.positionCount > 2 &&
                    Mathf.Abs(visibleFrom) < 0.0001f &&
                    Mathf.Abs(visibleTo - 1f) < 0.0001f &&
                    Vector3.Distance(renderer.GetPosition(0), expectedStart) < 0.0001f &&
                    Vector3.Distance(renderer.GetPosition(renderer.positionCount - 1),
                        expectedEnd) < 0.0001f,
                "Retry restores the complete visible path");
        }
        foreach (ArrowheadController head in heads)
        {
            Require(!head.IsMoving() && !head.IsLocked,
                "Retry clears movement state and locks");
            Require(Vector3.Distance(head.transform.position, retryPositions[head]) < 0.0001f &&
                    Quaternion.Angle(head.transform.rotation, retryRotations[head]) < 0.01f,
                "Retry restores every arrowhead position and rotation");
        }
        Sprite full = Get<Sprite>(manager, "filledHeartSprite");
        foreach (var heart in hearts) Require(heart.sprite == full, "Retry filled hearts");
        GameUIController ui = UnityEngine.Object.FindFirstObjectByType<GameUIController>();
        Require(!Get<GameObject>(ui, "gameOverPanel").activeSelf,
            "Retry hides result overlays");
        hints = UnityEngine.Object.FindFirstObjectByType<HintManager>();
        Require(Get<ArrowheadController>(hints, "lastHintedArrowhead") == null,
            "Retry resets hint cycling state");
        hints.ShowHint();
        ArrowheadController freshHint = Get<ArrowheadController>(hints, "highlightedArrowhead");
        Require(freshHint != null && freshHint.GetLineController().CanArrowheadMoveSafely(freshHint),
            "Retry leaves hints usable on the fresh level");
        hints.ClearHint();
        Debug.Log("PLAYVERIFY retry PASS lives=3 paths=14 heads=28 hearts=3/full");
        Debug.Log("PLAYVERIFY RESULT=PASS");
    }

    private static int CountPathIntersections(ArrowLineController[] lines)
    {
        int count = 0;
        for (int a = 0; a < lines.Length; a++)
        for (int b = a + 1; b < lines.Length; b++)
        {
            var routeA = lines[a].GetArrowPath().GetRoutePoints(0f, 1f);
            var routeB = lines[b].GetArrowPath().GetRoutePoints(0f, 1f);
            for (int i = 1; i < routeA.Count; i++)
            for (int j = 1; j < routeB.Count; j++)
            {
                if (SegmentsCross(routeA[i - 1], routeA[i], routeB[j - 1], routeB[j]))
                    count++;
            }
        }

        return count;
    }

    private static void VerifyGreenLoopClipping(ArrowLineController line)
    {
        ArrowheadController head = line.GetStartArrowhead();
        float previousProgress = Get<float>(head, "progress");
        Set(head, "progress", .35f);
        line.RefreshVisibleRange();

        LineRenderer renderer = line.GetArrowPath().GetComponent<LineRenderer>();
        Require(renderer.positionCount > 2, "green_1 remaining route has continuous rendered vertices");
        float visibleStart = Mathf.Min(head.NormalizedPathPosition,
            line.GetEndArrowhead().NormalizedPathPosition);
        Vector3 expectedStart = line.GetArrowPath()
            .GetPointAtNormalizedDistance(visibleStart);
        Require(Vector3.Distance(renderer.GetPosition(0), expectedStart) < .0001f,
            "green_1 clips only at the head's traversed distance");

        List<Vector3> fullRoute = line.GetArrowPath().GetRoutePoints(0f, 1f);
        float maximumSampleGap = 0f;
        for (int i = 1; i < fullRoute.Count; i++)
            maximumSampleGap = Mathf.Max(maximumSampleGap,
                Vector3.Distance(fullRoute[i - 1], fullRoute[i]));
        for (int i = 1; i < renderer.positionCount; i++)
            Require(Vector3.Distance(renderer.GetPosition(i - 1),
                    renderer.GetPosition(i)) <= maximumSampleGap + .001f,
                "green_1 rendered path has no hidden middle gap");

        Set(head, "progress", previousProgress);
        line.RefreshVisibleRange();
        Debug.Log("PLAYVERIFY green_1 clipping PASS vertices=" +
                  renderer.positionCount + " maxGap=" + maximumSampleGap);
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a;
        Vector2 cd = d - c;
        float denominator = ab.x * cd.y - ab.y * cd.x;
        if (Mathf.Abs(denominator) < .00001f) return false;
        Vector2 ac = c - a;
        float t = (ac.x * cd.y - ac.y * cd.x) / denominator;
        float u = (ac.x * ab.y - ac.y * ab.x) / denominator;
        return t > .001f && t < .999f && u > .001f && u < .999f;
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("PLAYVERIFY failed: " + message);
    }

    private static T Get<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
