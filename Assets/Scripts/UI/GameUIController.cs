using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameUIController : MonoBehaviour
{
    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text resultText;

    private void Start()
    {
        gameOverPanel.SetActive(false);
    }

    public void ShowWin()
    {
        GameManager manager = FindFirstObjectByType<GameManager>();
        int lives = manager != null ? manager.CurrentLives : 0;
        ShowWin(lives);
    }

    public void ShowWin(int remainingLives)
    {
        ShowResult("<size=130%><b>LEVEL COMPLETED</b></size>\n" +
                   "<size=75%>All 14 paths cleared.</size>\n" +
                   "<size=75%>Lives remaining: " + remainingLives + " / 3</size>");
    }

    public void ShowLose()
    {
        ShowResult("<size=130%><b>GAME OVER</b></size>\n" +
                   "<size=75%>All three lives were lost.</size>\n" +
                   "<size=75%>Press Retry to try again.</size>");
    }

    private void ShowResult(string message)
    {
        resultText.text = message;
        resultText.fontSize = 72f;
        resultText.enableAutoSizing = true;
        resultText.fontSizeMin = 48f;
        resultText.fontSizeMax = 72f;
        resultText.alignment = TextAlignmentOptions.Center;
        resultText.color = Color.white;
        resultText.overflowMode = TextOverflowModes.Overflow;
        gameOverPanel.SetActive(true);
    }

    public void Retry()
    {
        GameManager manager = FindFirstObjectByType<GameManager>();
        if (manager != null)
        {
            manager.RestartLevel();
            return;
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void HideResult()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }
}
