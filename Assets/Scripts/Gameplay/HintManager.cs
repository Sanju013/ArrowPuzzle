
using System.Collections.Generic;
using UnityEngine;

public class HintManager : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;

    private ArrowLineController highlightedLine;
    private ArrowLineController previousHintLine;

    public void ShowHint()
    {
        previousHintLine = highlightedLine;
        ClearHint();

        ArrowLineController[] lines =
            FindObjectsByType<ArrowLineController>(
                FindObjectsSortMode.None
            );

        List<ArrowLineController> availableLines =
            new List<ArrowLineController>();

        foreach (ArrowLineController line in lines)
        {
            if (line == null || line.IsCompleted())
                continue;

            if (line.GetStartArrowhead().IsMoving() ||
                line.GetEndArrowhead().IsMoving())
                continue;

            if (!line.CanMoveSafely())
                continue;

            availableLines.Add(line);
        }

        if (availableLines.Count == 0)
        {
            Debug.Log("No available hint lines.");
            previousHintLine = null;
            return;
        }

        if (availableLines.Count > 1 &&
            previousHintLine != null)
        {
            availableLines.Remove(previousHintLine);
        }

        int randomIndex = Random.Range(0, availableLines.Count);

        highlightedLine = availableLines[randomIndex];
        highlightedLine.SetHintHighlight(true);

        Debug.Log("Hint: " + highlightedLine.gameObject.name);
    }

    public void ClearHint()
    {
        if (highlightedLine != null)
        {
            highlightedLine.SetHintHighlight(false);
            highlightedLine = null;
        }
    }
}
