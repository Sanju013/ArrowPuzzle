using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Captures the live, rendered HUD from the Game view in Play Mode.</summary>
public static class ArrowPuzzleHudCapture
{
    private const string Key = "ArrowPuzzle.HudCapture.Pending";
    private const string OutputPath =
        "C:/Unity/Internship3/ArrowPuzzle/Artifacts/arrow_puzzle_hud.png";
    private const string WinOutputPath =
        "C:/Unity/Internship3/ArrowPuzzle/Artifacts/arrow_puzzle_win.png";
    private const string LoseOutputPath =
        "C:/Unity/Internship3/ArrowPuzzle/Artifacts/arrow_puzzle_lose.png";
    private static float enteredAt;
    private static bool captureRequested;

    [MenuItem("Tools/Arrow Puzzle/Capture Play HUD")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity",
            OpenSceneMode.Single);
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        if (File.Exists(OutputPath))
            File.Delete(OutputPath);
        if (File.Exists(WinOutputPath))
            File.Delete(WinOutputPath);
        if (File.Exists(LoseOutputPath))
            File.Delete(LoseOutputPath);
        captureRequested = false;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode &&
            SessionState.GetBool(Key, false))
        {
            enteredAt = Time.realtimeSinceStartup;
            EditorApplication.update -= CaptureWhenReady;
            EditorApplication.update += CaptureWhenReady;
        }
    }

    private static void CaptureWhenReady()
    {
        if (!Application.isPlaying)
            return;

        if (!captureRequested && Time.realtimeSinceStartup - enteredAt >= 1f)
        {
            captureRequested = true;
            try
            {
                GameUIController ui = Object.FindFirstObjectByType<GameUIController>();
                GameManager manager = Object.FindFirstObjectByType<GameManager>();
                CaptureUnityGameView(OutputPath);
                ui.ShowWin(manager != null ? manager.CurrentLives : 3);
                CaptureUnityGameView(WinOutputPath);
                ui.ShowLose();
                CaptureUnityGameView(LoseOutputPath);
                EditorApplication.update -= CaptureWhenReady;
                SessionState.SetBool(Key, false);
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                EditorApplication.update -= CaptureWhenReady;
                SessionState.SetBool(Key, false);
                Debug.LogException(exception);
                EditorApplication.Exit(2);
            }
        }
    }

    private static void CaptureUnityGameView(string outputPath)
    {
        const int width = 640;
        const int height = 480;
        Camera camera = Camera.main;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (camera == null || canvas == null)
            throw new System.InvalidOperationException(
                "Main camera or HUD canvas was not found.");

        RenderMode oldMode = canvas.renderMode;
        Camera oldCanvasCamera = canvas.worldCamera;
        RenderTexture oldTarget = camera.targetTexture;
        float oldAspect = camera.aspect;
        RenderTexture target = new RenderTexture(width, height, 24,
            RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);

        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 1f;
            camera.targetTexture = target;
            camera.aspect = (float)width / height;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log("HUDCAPTURE saved=" + outputPath + " size=" +
                      width + "x" + height + " renderedBy=" +
                      camera.name + " with ScreenSpaceCamera canvas");
        }
        finally
        {
            RenderTexture.active = null;
            camera.targetTexture = oldTarget;
            camera.aspect = oldAspect;
            canvas.worldCamera = oldCanvasCamera;
            canvas.renderMode = oldMode;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
