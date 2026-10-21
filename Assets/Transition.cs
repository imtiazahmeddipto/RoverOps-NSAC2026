using UnityEngine;
using UnityEngine.SceneManagement;

public class Transition : MonoBehaviour
{
    [SerializeField] private GameObject startingSceneTransition;
    [SerializeField] private GameObject endingSceneTransition;

    [SerializeField] private float startTransitionDuration = 1f;
    [SerializeField] private float endTransitionDuration = 1f;

    private bool isLoading;
    private string sceneToLoad;

    private void Start()
    {
        if (startingSceneTransition != null)
        {
            startingSceneTransition.SetActive(true);
            Invoke(nameof(DisableStartingTransition), startTransitionDuration);
        }
    }

    private void DisableStartingTransition()
    {
        if (startingSceneTransition != null)
        {
            startingSceneTransition.SetActive(false);
        }
    }


    public void LoadScene(string sceneName)
    {
        if (isLoading) return;
        isLoading = true;
        sceneToLoad = sceneName;

        if (endingSceneTransition != null) endingSceneTransition.SetActive(true);
        Invoke(nameof(LoadNextScene), endTransitionDuration);
    }

    private void LoadNextScene() => SceneManager.LoadScene(sceneToLoad);
}
