using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// [BGM] Import settings for the music tracks in Assets/Resources/Audio: every clip whose file name starts
    /// with "music_" is Vorbis-compressed and streamed from disk (loaded in the background) so the 13 BGM tracks
    /// do not sit decompressed in memory. Sound effects are left untouched.
    /// </summary>
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        const string MusicFolder = "Assets/Resources/Audio/";
        const string MusicPrefix = "music_";
        const float VorbisQuality = 0.7f;

        static bool IsMusic(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(MusicFolder)) return false;
            return System.IO.Path.GetFileName(path).StartsWith(MusicPrefix);
        }

        void OnPreprocessAudio()
        {
            if (!IsMusic(assetPath)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = VorbisQuality;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
        }
    }
}
