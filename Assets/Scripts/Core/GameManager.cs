
using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Header("Game Settings")]
    [SerializeField] private int startingLives = 3;

    [Header("UI References")]
    [SerializeField] private GameUIController gameUI;
    [SerializeField] private TMP_Text livesText;

    private int currentLives;
    private int totalLines;
    private int completedLines;

    private bool isHandlingCollision;
    private bool isGameOver;

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

        UpdateLivesUI();

        Debug.Log("Game Started! Lives: " + currentLives);
        Debug.Log("Total Lines: " + totalLines);
    }

    private void UpdateLivesUI()
    {
        if (livesText != null)
            livesText.text = "LIVES: " + currentLives;
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

        isHandlingCollision = true;

        Debug.Log("Arrowhead collision detected!");

        LoseLife();

        first.GetLineController().ResetLine();
        second.GetLineController().ResetLine();

        isHandlingCollision = false;
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
            gameUI.ShowWin();
        else
            Debug.LogError("GameUIController is not assigned!");
    }
}
