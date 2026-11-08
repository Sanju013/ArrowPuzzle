using UnityEngine;

public class ArrowLineController : MonoBehaviour
{
    [Header("Line References")]
    [SerializeField] private ArrowPath arrowPath;
    [SerializeField] private ArrowheadController startArrowhead;
    [SerializeField] private ArrowheadController endArrowhead;

    [Header("Game Manager")]
    [SerializeField] private GameManager gameManager;

    private Vector3 startPosition;
    private Vector3 endPosition;
    private bool isCompleted;

    private SpriteRenderer startRenderer;
    private SpriteRenderer endRenderer;

    private Color startOriginalColor;
    private Color endOriginalColor;
    private ArrowheadController highlightedArrowhead;

    private void Awake()
    {
        CacheInitialState();
    }

    /// <summary>
    /// Runtime wiring used by LevelLoader. The loaded level has no
    /// hand-assigned Inspector references, so the loader supplies them here.
    /// </summary>
    public void Configure(
        ArrowPath path,
        ArrowheadController start,
        ArrowheadController end,
        GameManager manager)
    {
        arrowPath = path;
        startArrowhead = start;
        endArrowhead = end;
        gameManager = manager;

        CacheInitialState();
    }

    private void CacheInitialState()
    {
        if (startArrowhead == null || endArrowhead == null)
            return;

        startPosition = startArrowhead.transform.position;
        endPosition = endArrowhead.transform.position;

        startRenderer = startArrowhead.GetComponent<SpriteRenderer>();
        endRenderer = endArrowhead.GetComponent<SpriteRenderer>();

        if (startRenderer != null)
            startOriginalColor = startRenderer.color;

        if (endRenderer != null)
            endOriginalColor = endRenderer.color;
    }

    private void LateUpdate()
    {
        RefreshVisibleRange();
    }

    /// <summary>
    /// Recomputes the visible path section from arrowhead progress.
    /// LateUpdate calls this; editor validation can call it directly.
    /// </summary>
    public void RefreshVisibleRange()
    {
        if (isCompleted || arrowPath == null)
            return;

        if (startArrowhead == null || endArrowhead == null)
            return;

        float startProgress = startArrowhead.NormalizedPathPosition;
        float endProgress = endArrowhead.NormalizedPathPosition;

        arrowPath.SetVisibleNormalizedRange(
            Mathf.Min(startProgress, endProgress),
            Mathf.Max(startProgress, endProgress)
        );
    }

    public void ResetLine()
    {
        isCompleted = false;

        SetHintHighlight(false);

        startArrowhead.ResetArrowhead(startPosition);
        endArrowhead.ResetArrowhead(endPosition);

        arrowPath.gameObject.SetActive(true);
        arrowPath.GeneratePath();
        arrowPath.ShowFullPath();

        gameObject.SetActive(true);

        Debug.Log("Line reset: " + gameObject.name);
        ArrowheadController.NotifyGameplayStateChanged();
    }

    public void CompleteLine()
    {
        if (isCompleted)
            return;

        isCompleted = true;

        Debug.Log("Line completed: " + gameObject.name);

        gameObject.SetActive(false);
        ArrowheadController.NotifyGameplayStateChanged();

        if (gameManager != null)
            gameManager.LineCompleted();
    }

    public ArrowheadController GetStartArrowhead()
    {
        return startArrowhead;
    }

    public ArrowheadController GetEndArrowhead()
    {
        return endArrowhead;
    }

    public ArrowPath GetArrowPath()
    {
        return arrowPath;
    }

    public bool IsCompleted()
    {
        return isCompleted;
    }

    public void SetHintHighlight(bool highlighted)
    {
        Color hintColor = Color.yellow;

        highlightedArrowhead = null;
        if (startRenderer != null)
            startRenderer.color = highlighted ? hintColor : startOriginalColor;

        if (endRenderer != null)
            endRenderer.color = highlighted ? hintColor : endOriginalColor;
    }

    public void SetArrowheadHint(ArrowheadController arrowhead, bool highlighted)
    {
        if (highlightedArrowhead != null)
            RestoreArrowheadColor(highlightedArrowhead);

        highlightedArrowhead = highlighted && arrowhead != null
            ? arrowhead
            : null;

        if (highlightedArrowhead != null)
            GetArrowheadRenderer(highlightedArrowhead).color = Color.yellow;
    }

    public bool CanArrowheadMoveSafely(ArrowheadController arrowhead)
    {
        if (isCompleted || arrowhead == null || arrowhead.IsMoving() ||
            arrowhead.IsLocked ||
            (arrowhead != startArrowhead && arrowhead != endArrowhead))
        {
            return false;
        }

        return arrowhead.CanReachDestinationSafely();
    }

    private SpriteRenderer GetArrowheadRenderer(
        ArrowheadController arrowhead)
    {
        if (arrowhead == startArrowhead)
            return startRenderer;

        return endRenderer;
    }

    private void RestoreArrowheadColor(ArrowheadController arrowhead)
    {
        SpriteRenderer renderer = GetArrowheadRenderer(arrowhead);
        if (renderer == null)
            return;

        renderer.color = arrowhead == startArrowhead
            ? startOriginalColor
            : endOriginalColor;
    }
}
