using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    [Header("Game Settings")]
    [SerializeField] private int startingLives = 3;

    [Header("UI References")]
    [SerializeField] private GameUIController gameUI;
    [SerializeField] private TMP_Text livesText;
    [SerializeField] private RectTransform livesContainer;
    [SerializeField] private Sprite filledHeartSprite;
    [SerializeField] private Sprite emptyHeartSprite;

    [Header("Lives Display")]
    [Tooltip("Target rendered Image size in screen pixels.")]
    [SerializeField] private Vector2 heartSize = new Vector2(76f, 76f);
    [SerializeField] private float heartSpacing = 16f;

    private int currentLives;
    private int totalLines;
    private int completedLines;

    private bool isHandlingCollision;
    private bool isGameOver;
    private readonly List<Image> heartImages = new List<Image>();
    private float lastHeartCanvasScale = -1f;
    private readonly Dictionary<CollisionPair, ColliderPair> activeCollisionPairs =
        new Dictionary<CollisionPair, ColliderPair>();

    private struct CollisionPair : System.IEquatable<CollisionPair>
    {
        private readonly int first;
        private readonly int second;

        public CollisionPair(ArrowheadController a, ArrowheadController b)
        {
            int idA = a.GetInstanceID();
            int idB = b.GetInstanceID();
            first = Mathf.Min(idA, idB);
            second = Mathf.Max(idA, idB);
        }

        public bool Equals(CollisionPair other)
        {
            return first == other.first && second == other.second;
        }

        public override bool Equals(object obj)
        {
            return obj is CollisionPair other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return (first * 397) ^ second; }
        }
    }

    private struct ColliderPair
    {
        public readonly Collider2D first;
        public readonly Collider2D second;

        public ColliderPair(Collider2D first, Collider2D second)
        {
            this.first = first;
            this.second = second;
        }
    }

    public int CurrentLives => currentLives;

    private void Start()
    {
        currentLives = startingLives;
        isHandlingCollision = false;
        isGameOver = false;
        completedLines = 0;

        totalLines = FindObjectsByType<ArrowLineController>(
            FindObjectsSortMode.None
        ).Length;

        InitializeLivesDisplay();
        UpdateLivesUI();

        Debug.Log("Game Started! Lives: " + currentLives);
        Debug.Log("Total Lines: " + totalLines);
    }

    private void UpdateLivesUI()
    {
        for (int i = 0; i < heartImages.Count; i++)
        {
            bool filled = i < currentLives;
            heartImages[i].sprite = filled
                ? filledHeartSprite
                : emptyHeartSprite;
            heartImages[i].color = filled
                ? new Color(1f, 0.25f, 0.39f, 1f)
                : new Color(0.48f, 0.5f, 0.56f, 0.78f);
        }
    }

    private void InitializeLivesDisplay()
    {
        if (livesText != null)
        {
            livesText.text = string.Empty;
            livesText.enabled = false;
            if (livesContainer == null)
                livesContainer = livesText.rectTransform;
        }

        if (livesContainer == null)
        {
            Debug.LogError("GameManager: livesContainer is not assigned.");
            return;
        }

        if (filledHeartSprite == null || emptyHeartSprite == null)
        {
            Debug.LogError("GameManager: heart sprites are not assigned.");
            return;
        }

        const int heartCount = 3;
        for (int i = 0; i < heartCount; i++)
        {
            GameObject heartObject = new GameObject(
                "LifeHeart_" + (i + 1), typeof(RectTransform), typeof(Image));
            RectTransform rect = heartObject.GetComponent<RectTransform>();
            rect.SetParent(livesContainer, false);
            Image image = heartObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            heartImages.Add(image);
        }

        UpdateLivesLayout(true);
    }

    private void UpdateLivesLayout(bool force)
    {
        if (livesContainer == null || heartImages.Count == 0)
            return;

        Canvas canvas = livesContainer.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        if (scale <= 0f)
            scale = 1f;
        if (!force && Mathf.Approximately(scale, lastHeartCanvasScale))
            return;

        lastHeartCanvasScale = scale;
        const float screenMargin = 16f;
        const int heartCount = 3;
        float step = heartSize.x + heartSpacing;

        livesContainer.anchorMin = new Vector2(0f, 1f);
        livesContainer.anchorMax = new Vector2(0f, 1f);
        livesContainer.pivot = new Vector2(0f, 1f);
        livesContainer.anchoredPosition =
            new Vector2(screenMargin / scale, -screenMargin / scale);
        livesContainer.sizeDelta = new Vector2(
            (heartCount * heartSize.x + (heartCount - 1) * heartSpacing) / scale,
            heartSize.y / scale);

        for (int i = 0; i < heartImages.Count; i++)
        {
            RectTransform rect = heartImages[i].rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(step * i / scale, 0f);
            rect.sizeDelta = heartSize / scale;
        }
    }

    private void Update()
    {
        UpdateLivesLayout(false);
        PruneSeparatedCollisionPairs();
    }

    public void HandleCollision(
        ArrowheadController first,
        ArrowheadController second)
    {
        if (isHandlingCollision || isGameOver || currentLives <= 0)
            return;

        if (first == null || second == null)
            return;

        if (first.GetLineController() == second.GetLineController())
            return;

        ArrowLineController firstLine = first.GetLineController();
        ArrowLineController secondLine = second.GetLineController();
        if (firstLine == null || secondLine == null)
            return;

        CollisionPair pairKey = new CollisionPair(first, second);
        if (activeCollisionPairs.ContainsKey(pairKey))
        {
            Debug.Log("Duplicate collision callback ignored for " +
                      first.gameObject.name + " and " + second.gameObject.name);
            return;
        }

        activeCollisionPairs.Add(pairKey, new ColliderPair(
            first.GetComponent<Collider2D>(),
            second.GetComponent<Collider2D>()));

        isHandlingCollision = true;
        try
        {
            Debug.Log("Confirmed arrowhead collision: " +
                      first.gameObject.name + " on " + firstLine.gameObject.name +
                      " and " + second.gameObject.name + " on " +
                      secondLine.gameObject.name);

            LoseLife();
            Debug.Log("Life deducted for collision pair: " +
                      first.gameObject.name + " / " + second.gameObject.name +
                      "; lives now " + currentLives);

            Debug.Log("Resetting collision lines: " + firstLine.gameObject.name +
                      " and " + secondLine.gameObject.name);
            firstLine.ResetLine();
            secondLine.ResetLine();
        }
        finally
        {
            isHandlingCollision = false;
        }
    }

    private void PruneSeparatedCollisionPairs()
    {
        if (activeCollisionPairs.Count == 0)
            return;

        List<CollisionPair> separated = null;
        foreach (KeyValuePair<CollisionPair, ColliderPair> entry in activeCollisionPairs)
        {
            Collider2D a = entry.Value.first;
            Collider2D b = entry.Value.second;
            bool shouldRelease = a == null || b == null ||
                !a.enabled || !b.enabled ||
                !a.gameObject.activeInHierarchy ||
                !b.gameObject.activeInHierarchy;

            if (!shouldRelease)
                shouldRelease = !Physics2D.Distance(a, b).isOverlapped;

            if (shouldRelease)
            {
                if (separated == null)
                    separated = new List<CollisionPair>();
                separated.Add(entry.Key);
            }
        }

        if (separated == null)
            return;

        foreach (CollisionPair pair in separated)
            activeCollisionPairs.Remove(pair);
    }

    private void LoseLife()
    {
        currentLives--;

        UpdateLivesUI();

        Debug.Log("Lives remaining: " + currentLives);

        if (currentLives <= 0)
        {
            currentLives = 0;
            GameOver();
        }
    }

    public void LineCompleted()
    {
        if (isGameOver)
            return;

        completedLines++;

        Debug.Log(
            "Lines completed: " + completedLines + "/" + totalLines
        );

        if (completedLines >= totalLines)
            WinGame();
    }

    /// <summary>
    /// Restores the current scene's level state without depending on an
    /// editor scene reload. This is also safe when called from a Retry button.
    /// </summary>
    public void RestartLevel()
    {
        currentLives = startingLives;
        completedLines = 0;
        isHandlingCollision = false;
        isGameOver = false;
        activeCollisionPairs.Clear();

        LevelLoader loader = FindFirstObjectByType<LevelLoader>();
        ArrowLineController[] lines = loader != null
            ? new List<ArrowLineController>(loader.SpawnedLines).ToArray()
            : FindObjectsByType<ArrowLineController>(
                FindObjectsSortMode.None);
        foreach (ArrowLineController line in lines)
        {
            if (line != null)
                line.ResetLine();
        }

        UpdateLivesUI();
        if (gameUI != null)
            gameUI.HideResult();
        ArrowheadController.NotifyGameplayStateChanged();

        Debug.Log("Level restarted. Lives: " + currentLives +
                  ", lines reset: " + lines.Length);
    }

    private void GameOver()
    {
        if (isGameOver)
            return;

        isGameOver = true;
        Debug.Log("You Lost!");

        if (gameUI != null)
            gameUI.ShowLose();
        else
            Debug.LogError("GameUIController is not assigned!");
    }

    public void WinGame()
    {
        if (isGameOver)
            return;

        isGameOver = true;
        Debug.Log("You Won!");

        if (gameUI != null)
            gameUI.ShowWin(currentLives);
        else
            Debug.LogError("GameUIController is not assigned!");
    }
}
