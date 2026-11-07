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
        ShowResult("YOU WON!");
    }

    public void ShowLose()
    {
        ShowResult("YOU LOST!");
    }

    private void ShowResult(string message)
    {
        resultText.text = message;
        gameOverPanel.SetActive(true);
    }

    public void Retry()
    {
        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex
        );
    }
}