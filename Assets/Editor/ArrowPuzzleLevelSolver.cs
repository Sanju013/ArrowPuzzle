using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor-only exhaustive solver for the current circular arrowhead-collision rules.
/// A move removes one complete line, so the search state is a 14-bit set of
/// remaining lines. Safety is queried from the production swept-circle check.
/// </summary>
public static class ArrowPuzzleLevelSolver
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string RunSolverKey = "ArrowPuzzle.LevelSolver.RunInPlayMode";
    private static bool runtimeSolveStarted;
    private static readonly FieldInfo CompletedField =
        typeof(ArrowLineController).GetField(
            "isCompleted", BindingFlags.Instance | BindingFlags.NonPublic);

    private static ArrowLineController[] lines;
    private static int[,] blockerMasks;
    private static readonly List<Move> path = new List<Move>();
    private static readonly HashSet<int> deadStates = new HashSet<int>();

    private struct Move
    {
        public int lineIndex;
        public int headIndex;

        public Move(int lineIndex, int headIndex)
        {
            this.lineIndex = lineIndex;
            this.headIndex = headIndex;
        }
    }

    [InitializeOnLoadMethod]
    private static void RegisterPlayModeRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        try
        {
            path.Clear();
            deadStates.Clear();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunSolverKey, true);
            runtimeSolveStarted = false;
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(2);
        }
    }

    private static bool Search(int remainingMask)
    {
        if (remainingMask == 0)
            return true;

        if (deadStates.Contains(remainingMask))
            return false;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            int bit = 1 << lineIndex;
            if ((remainingMask & bit) == 0)
                continue;

            for (int headIndex = 0; headIndex < 2; headIndex++)
            {
                if ((blockerMasks[lineIndex, headIndex] & remainingMask) != 0)
                    continue;

                path.Add(new Move(lineIndex, headIndex));
                if (Search(remainingMask & ~bit))
                    return true;
                path.RemoveAt(path.Count - 1);
            }
        }

        deadStates.Add(remainingMask);
        return false;
    }

    private static void SetCompletionMask(int remainingMask)
    {
        for (int i = 0; i < lines.Length; i++)
            CompletedField.SetValue(lines[i], (remainingMask & (1 << i)) == 0);
    }

    private static void PrecomputeBlockers()
    {
        blockerMasks = new int[lines.Length, 2];
        for (int candidateIndex = 0; candidateIndex < lines.Length; candidateIndex++)
        {
            for (int blockerIndex = 0; blockerIndex < lines.Length; blockerIndex++)
            {
                if (candidateIndex == blockerIndex)
                    continue;

                int activePair = (1 << candidateIndex) | (1 << blockerIndex);
                SetCompletionMask(activePair);

                for (int headIndex = 0; headIndex < 2; headIndex++)
                {
                    ArrowheadController head = headIndex == 0
                        ? lines[candidateIndex].GetStartArrowhead()
                        : lines[candidateIndex].GetEndArrowhead();

                    if (!lines[candidateIndex].CanArrowheadMoveSafely(head))
                        blockerMasks[candidateIndex, headIndex] |=
                            1 << blockerIndex;
                }
            }
        }

        SetCompletionMask((1 << lines.Length) - 1);
    }

    private static void ReportInitialHeadOverlaps()
    {
        int overlapCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            ArrowheadController[] firstHeads =
            {
                lines[i].GetStartArrowhead(),
                lines[i].GetEndArrowhead()
            };

            for (int j = i + 1; j < lines.Length; j++)
            {
                ArrowheadController[] secondHeads =
                {
                    lines[j].GetStartArrowhead(),
                    lines[j].GetEndArrowhead()
                };

                foreach (ArrowheadController first in firstHeads)
                foreach (ArrowheadController second in secondHeads)
                {
                    Collider2D firstCollider = first.GetComponent<Collider2D>();
                    Collider2D secondCollider = second.GetComponent<Collider2D>();
                    if (firstCollider == null || secondCollider == null ||
                        !Physics2D.Distance(firstCollider, secondCollider).isOverlapped)
                        continue;

                    overlapCount++;
                    Debug.Log("LEVELSOLVER INITIAL_CONTACT_MARGIN_PAIR " +
                                   lines[i].name + "/" + first.name + " at " +
                                   first.transform.position + " with " +
                                   lines[j].name + "/" + second.name + " at " +
                                   second.transform.position);
                }
            }
        }

        Debug.Log("LEVELSOLVER initial head pairs within Physics2D.Distance contact range=" +
                  overlapCount);
    }

    private static void ReportArrowheadSpriteAndColliderSize()
    {
        int audited = 0;
        float minRadius = float.PositiveInfinity;
        float maxRadius = 0f;
        foreach (ArrowLineController line in lines)
        {
            AuditCircle(line, line.GetStartArrowhead(), ref audited,
                ref minRadius, ref maxRadius);
            AuditCircle(line, line.GetEndArrowhead(), ref audited,
                ref minRadius, ref maxRadius);
        }

        Debug.Log("LEVELSOLVER circle audit heads=" + audited +
                  ", worldRadiusRange=" + minRadius.ToString("F5") + ".." +
                  maxRadius.ToString("F5") + ", expectedLocalRadius=0.35000");
        if (audited != 28 || Mathf.Abs(minRadius - maxRadius) > 0.0001f)
            throw new InvalidOperationException("Runtime circle audit failed.");
    }

    private static void AuditCircle(
        ArrowLineController line,
        ArrowheadController head,
        ref int audited,
        ref float minRadius,
        ref float maxRadius)
    {
        CircleCollider2D collider = head != null
            ? head.GetComponent<CircleCollider2D>() : null;
        SpriteRenderer renderer = head != null
            ? head.GetComponent<SpriteRenderer>() : null;
        if (collider == null || renderer == null || renderer.sprite == null)
        {
            throw new InvalidOperationException(
                "Missing runtime circle/sprite on " + line.name + "/" +
                (head != null ? head.name : "null"));
        }

        float radius = ArrowheadController.GetWorldCircleRadius(collider);
        minRadius = Mathf.Min(minRadius, radius);
        maxRadius = Mathf.Max(maxRadius, radius);
        audited++;
        Debug.Log("LEVELSOLVER circle " + line.name + "/" + head.name +
                  " radiusLocal=" + collider.radius.ToString("F4") +
                  " radiusWorld=" + radius.ToString("F5") +
                  " centerOffset=" + collider.offset +
                  " scale=" + collider.transform.lossyScale +
                  " rotation=" + head.transform.eulerAngles.z.ToString("F2"));
    }

    private static void ReportDeadlockBlockers()
    {
        int fullMask = (1 << lines.Length) - 1;
        SetCompletionMask(fullMask);

        for (int i = 0; i < lines.Length; i++)
        {
            ArrowLineController line = lines[i];
            for (int headIndex = 0; headIndex < 2; headIndex++)
            {
                ArrowheadController head = headIndex == 0
                    ? line.GetStartArrowhead()
                    : line.GetEndArrowhead();

                if (blockerMasks[i, headIndex] == 0)
                    continue;

                List<string> linesThatClearRoute = new List<string>();
                for (int blockerIndex = 0; blockerIndex < lines.Length; blockerIndex++)
                {
                    if ((blockerMasks[i, headIndex] & (1 << blockerIndex)) != 0)
                        linesThatClearRoute.Add(lines[blockerIndex].name);
                }

                Debug.Log("LEVELSOLVER blocked " + line.name + "/" + head.name +
                          "; removing individually: " +
                          (linesThatClearRoute.Count == 0
                              ? "no single blocker clears it"
                              : string.Join(", ", linesThatClearRoute)));
            }
        }

        SetCompletionMask(fullMask);
    }

    private static void ReportSmallestDeadlock()
    {
        int deadlockMask = 0;
        int fewestRemaining = int.MaxValue;
        foreach (int state in deadStates)
        {
            int remaining = CountBits(state);
            if (remaining < fewestRemaining)
            {
                deadlockMask = state;
                fewestRemaining = remaining;
            }
        }

        if (deadlockMask == 0)
            return;

        List<string> remainingLines = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if ((deadlockMask & (1 << i)) != 0)
                remainingLines.Add(lines[i].name);
        }

        Debug.LogError("LEVELSOLVER reachable deadlock with " + fewestRemaining +
                       " lines remaining: " + string.Join(", ", remainingLines));

        for (int i = 0; i < lines.Length; i++)
        {
            if ((deadlockMask & (1 << i)) == 0)
                continue;

            for (int headIndex = 0; headIndex < 2; headIndex++)
            {
                List<string> blockers = new List<string>();
                for (int j = 0; j < lines.Length; j++)
                {
                    if ((blockerMasks[i, headIndex] & deadlockMask & (1 << j)) != 0)
                        blockers.Add(lines[j].name);
                }

                string headName = headIndex == 0
                    ? lines[i].GetStartArrowhead().name
                    : lines[i].GetEndArrowhead().name;
                Debug.Log("LEVELSOLVER deadlock blocker " + lines[i].name + "/" +
                          headName + " <- " + string.Join(", ", blockers));
            }
        }
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode &&
            SessionState.GetBool(RunSolverKey, false))
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            runtimeSolveStarted = false;
            EditorApplication.update -= SolveRuntimeLevelWhenReady;
            EditorApplication.update += SolveRuntimeLevelWhenReady;
        }
    }

    private static void SolveRuntimeLevelWhenReady()
    {
        if (!Application.isPlaying || runtimeSolveStarted)
            return;

        GameManager manager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        ArrowLineController[] runtimeLines =
            UnityEngine.Object.FindObjectsByType<ArrowLineController>(
                FindObjectsSortMode.None);
        if (manager == null || manager.CurrentLives <= 0 || runtimeLines.Length != 14)
            return;

        runtimeSolveStarted = true;
        EditorApplication.update -= SolveRuntimeLevelWhenReady;
        SessionState.SetBool(RunSolverKey, false);

        try
        {
            if (manager.CurrentLives != 3)
                throw new InvalidOperationException(
                    "Runtime scene lost a life before solver startup: " +
                    manager.CurrentLives);

            path.Clear();
            deadStates.Clear();
            lines = runtimeLines;
            Array.Sort(lines, (a, b) => string.CompareOrdinal(a.name, b.name));
            Physics2D.SyncTransforms();
            ReportInitialHeadOverlaps();
            ReportArrowheadSpriteAndColliderSize();
            PrecomputeBlockers();

            int initiallyLegal = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (blockerMasks[i, 0] == 0)
                    initiallyLegal++;
                if (blockerMasks[i, 1] == 0)
                    initiallyLegal++;
            }

            Debug.Log("LEVELSOLVER initial legal heads=" + initiallyLegal +
                      ", lines=" + lines.Length +
                      ", defaultContactOffset=" + Physics2D.defaultContactOffset);

            bool found = Search((1 << lines.Length) - 1);
            SetCompletionMask((1 << lines.Length) - 1);

            if (!found)
            {
                Debug.LogError("LEVELSOLVER NO_COMPLETE_SOLUTION under the current circular-collider rules.");
                ReportSmallestDeadlock();
                ReportDeadlockBlockers();
                Debug.Log("LEVELSOLVER states=" + deadStates.Count +
                          ", result=UNSOLVABLE");
                EditorApplication.Exit(3);
                return;
            }

            string[] solution = new string[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                Move move = path[i];
                ArrowheadController head = move.headIndex == 0
                    ? lines[move.lineIndex].GetStartArrowhead()
                    : lines[move.lineIndex].GetEndArrowhead();
                solution[i] = lines[move.lineIndex].name + "/" + head.name;
            }

            Debug.Log("LEVELSOLVER SOLUTION=" + string.Join(" -> ", solution));
            ExecuteActualGameplayPlan(manager);
            Debug.Log("LEVELSOLVER ACTUAL_GAMEPLAY_RESULT=PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(4);
        }
    }

    private static void ExecuteActualGameplayPlan(GameManager manager)
    {
        Dictionary<string, ArrowLineController> linesByName =
            new Dictionary<string, ArrowLineController>();
        foreach (ArrowLineController line in
                 UnityEngine.Object.FindObjectsByType<ArrowLineController>(
                     FindObjectsSortMode.None))
        {
            linesByName.Add(line.name, line);
        }

        Physics2D.simulationMode = SimulationMode2D.Script;
        const float step = 0.02f;
        foreach (Move move in path)
        {
            ArrowLineController line = lines[move.lineIndex];
            if (!linesByName.ContainsKey(line.name))
                throw new InvalidOperationException("Missing runtime line: " + line.name);

            ArrowheadController head = move.headIndex == 0
                ? line.GetStartArrowhead()
                : line.GetEndArrowhead();

            if (!line.CanArrowheadMoveSafely(head))
                throw new InvalidOperationException(
                    "Production circular sweep rejected solver move " + line.name + "/" + head.name);

            if (!head.TryStartMovingAt(head.transform.position))
                throw new InvalidOperationException(
                    "Actual gameplay could not start solver move " + line.name + "/" + head.name);

            int steps = 0;
            while (!line.IsCompleted() && steps < 20000)
            {
                head.Tick(step);
                line.RefreshVisibleRange();
                Physics2D.SyncTransforms();
                Physics2D.Simulate(step);
                steps++;

                if (manager.CurrentLives != 3)
                    throw new InvalidOperationException(
                        "Solver move lost a life at " + line.name + "/" + head.name);
            }

            if (!line.IsCompleted())
                throw new TimeoutException("Solver move did not complete: " + line.name);

            Debug.Log("LEVELSOLVER PLAYED " + line.name + "/" + head.name +
                      " in " + steps + " physics steps, lives=" + manager.CurrentLives);
        }

        if (linesByName.Count != 14 || manager.CurrentLives != 3)
            throw new InvalidOperationException("Final level/lives state was incorrect.");
    }
}
