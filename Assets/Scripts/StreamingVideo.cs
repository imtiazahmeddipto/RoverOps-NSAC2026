using UnityEngine;
using UnityEngine.Video;

// Plays a video from Assets/StreamingAssets by URL. WebGL can't play Video Clip assets, only URLs,
// so this is how videos work in the browser (and it works the same in the editor and on PC).
// Put it next to a VideoPlayer and type the file name, e.g. "glitch.mp4".
[RequireComponent(typeof(VideoPlayer))]
public class StreamingVideo : MonoBehaviour
{
    [Tooltip("The file inside Assets/StreamingAssets, e.g. glitch.mp4 (or Videos/glitch.mp4)")]
    public string fileName = "glitch.mp4";

    void Awake()
    {
        VideoPlayer player = GetComponent<VideoPlayer>();
        player.source = VideoSource.Url;
        player.url = Application.streamingAssetsPath + "/" + fileName;
        player.audioOutputMode = VideoAudioOutputMode.None;    // browsers only autoplay silent videos
        if (player.playOnAwake) player.Play();
    }
}
