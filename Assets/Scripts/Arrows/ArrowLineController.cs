
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

    private void Awake()
    {
        startPosition = startArrowhead.transform.position;
        endPosition = endArrowhead.transform.position;

        startRenderer = startArrowhead.GetComponent<SpriteRenderer>();
        endRenderer = endArrowhead.GetComponent<SpriteRenderer>();

        if (startRenderer != null)
            startOriginalColor = startRenderer.color;

        if (endRenderer != null)
            endOriginalColor = endRenderer.color;
    }

    public void ResetLine()
    {
        isCompleted = false;

        SetHintHighlight(false);

        startArrowhead.ResetArrowhead(startPosition);
        endArrowhead.ResetArrowhead(endPosition);

        arrowPath.gameObject.SetActive(true);
        arrowPath.GeneratePath();

        gameObject.SetActive(true);

        Debug.Log("Line reset: " + gameObject.name);
    }

    public void CompleteLine()
    {
        if (isCompleted)
            return;

        isCompleted = true;

        Debug.Log("Line completed: " + gameObject.name);

        gameObject.SetActive(false);

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

        if (startRenderer != null)
            startRenderer.color = highlighted ? hintColor : startOriginalColor;

        if (endRenderer != null)
            endRenderer.color = highlighted ? hintColor : endOriginalColor;
    }
    public bool CanMoveSafely()
    {
        if (isCompleted)
            return false;

        
        for (int direction = 0; direction < 2; direction++)
        {
            bool safe = true;

            for (int i = 1; i <= 100; i++)
            {
                float progress = i / 100f;

                if (direction == 1)
                    progress = 1f - progress;

                Vector3 position =
                    arrowPath.GetPointAtNormalizedDistance(progress);

                Collider2D[] hits =
                    Physics2D.OverlapCircleAll(position, 0.15f);

                foreach (Collider2D hit in hits)
                {
                    ArrowheadController other =
                        hit.GetComponent<ArrowheadController>();

                    if (other == null)
                        continue;

                    if (other.GetLineController() == this)
                        continue;

                    if (other.GetLineController() != null &&
                        !other.GetLineController().IsCompleted())
                    {
                        safe = false;
                        break;
                    }
                }

                if (!safe)
                    break;
            }

            if (safe)
                return true;
        }

        return false;
    }
}
