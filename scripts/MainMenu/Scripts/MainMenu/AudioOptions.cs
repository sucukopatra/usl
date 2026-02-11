using System;
using UnityEngine;
using UnityEngine.Audio;

public class AudioOptions : MonoBehaviour {
    public AudioMixer audioMixer;
    public void SetMaster(float volume){
        audioMixer.SetFloat("Master", MathF.Log10(volume) * 20);
    }
    public void SetSoundFX(float volume){
        audioMixer.SetFloat("SoundFX", MathF.Log10(volume) * 20);
    }
    public void SetMusic(float volume){
        audioMixer.SetFloat("Music", MathF.Log10(volume) * 20);
    }
}
