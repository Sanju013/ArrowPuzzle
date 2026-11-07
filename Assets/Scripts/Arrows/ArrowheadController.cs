
using UnityEngine;
using UnityEngine.InputSystem;

public class ArrowheadController : MonoBehaviour
{
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

    public ArrowLineController GetLineController()
    {
        return lineController;
    }

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (!isMoving && Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePosition =
                Mouse.current.position.ReadValue();

            Vector3 worldPosition =
                mainCamera.ScreenToWorldPoint(mousePosition);

            RaycastHit2D hit =
                Physics2D.Raycast(worldPosition, Vector2.zero);

            if (hit.collider != null &&
                hit.collider.gameObject == gameObject)
            {
                if (oppositeArrowhead != null &&
                    oppositeArrowhead.isMoving)
                {
                    return;
                }

                isMoving = true;
                Debug.Log("Arrowhead started: " + gameObject.name);
            }
        }

        if (!isMoving)
            return;

        progress += moveSpeed * Time.deltaTime / arrowPath.TotalLength;
        progress = Mathf.Clamp01(progress);

        float pathProgress = movesFromStart
            ? progress
            : 1f - progress;

        transform.position =
            arrowPath.GetPointAtNormalizedDistance(pathProgress);

        if (progress >= 1f)
        {
            lineController.CompleteLine();
        }
    }

    public void ResetArrowhead(Vector3 originalPosition)
    {
        isMoving = false;
        progress = 0f;
        transform.position = originalPosition;
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

        Debug.Log("Collision with: " + otherArrowhead.gameObject.name);

        gameManager.HandleCollision(this, otherArrowhead);
    }
    public bool IsMoving()
    {
        return isMoving;
    }
}
