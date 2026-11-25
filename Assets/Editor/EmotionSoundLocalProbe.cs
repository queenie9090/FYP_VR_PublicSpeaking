using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad] static class EmotionSoundLocalProbe {
 const string K="EmotionSfxNov25", log="Library/EmotionSoundCheck.txt";
 static string[] scenes={"Hall","Classroom","TutorialScene","Interview","Cafeteria"};
 static string[] cues={"HAPPY","SAD","ANGRY","NERVOUS","THINKING","NEUTRAL","ANGRY"};
 static int scene,step,frame;static bool pending;
 static AudioSource voice;
 static void Log(string s)=>File.AppendAllText(log,s+"\n");
 static EmotionSoundLocalProbe(){EditorApplication.update+=Tick;}
 static void Tick(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(!EditorApplication.isPlayingOrWillChangePlaymode){
 if(SessionState.GetInt(K,0)==2){var old=SessionState.GetString(K+"old","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(old)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(old);SessionState.SetInt(K,0);Log("Restored");return;}
 if(!File.Exists("Library/EmotionSoundCheck.go"))return;File.Delete("Library/EmotionSoundCheck.go");File.WriteAllText(log,"Start\n");SessionState.SetString(K+"old",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)??"");EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Hall.unity");SessionState.SetInt(K,1);EditorApplication.EnterPlaymode();return;}
 if(!EditorApplication.isPlaying||SessionState.GetInt(K,0)!=1||Time.frameCount<frame||Time.frameCount<15)return;
 try{var active=SceneManager.GetActiveScene();var actors=Object.FindObjectsByType<NPCEmotionController>(FindObjectsSortMode.InstanceID).Where(x=>x.isActiveAndEnabled && x.gameObject.scene==active).ToArray();if(actors.Length==0)throw new System.Exception("no actors");
 foreach(var f in actors){var src=f.transform.Find("Emotion sound").GetComponent<AudioSource>();
 if(!pending){if(step==0)Log(active.name+" "+f.name+" startupSilent="+(!src.isPlaying && !src.clip));
 if(step<7) f.SetEmotion(cues[step],step==2?20:85);
 else if(step==7){src.Stop();f.SetEmotion("ANGRY",70);}
 else if(step==8) f.SetEmotion("SAD",80);
 else if(step==9) f.SetEmotion("HAPPY",80);
 }else{
 string expected=step==6?"ANGRY_Strong":step<6?cues[step]:step==7?"ANGRY_Strong":"SAD";
 bool shouldPlay=step!=7&&step!=9;
 bool ok=src.clip && src.clip.name==expected && src.isPlaying==shouldPlay && src.spatialBlend==1 && (step!=8 || src.volume<.15f);
 Log(active.name+" "+f.name+" step="+step+" clip="+src.clip?.name+" playing="+src.isPlaying+" vol="+src.volume+" "+(ok?"PASS":"FAIL"));
 }}
 if(!pending){if(scene==0 && step==8){var speech=Object.FindFirstObjectByType<SpeechInteractionController>();voice=new GameObject("local probe voice").AddComponent<AudioSource>();voice.clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Own Sound/Andrew_Speech.mp3");voice.Play();typeof(SpeechInteractionController).GetField("activeSpeechSource",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(speech,voice);}
 if(scene==0 && step==9)Object.FindFirstObjectByType<HallAudienceCelebration>().OnJudgeHappy();
 frame=Time.frameCount+2;pending=true;return;}
 pending=false;if(++step<(scene==0?10:1))return;step=0;
 if(++scene<scenes.Length){SceneManager.LoadScene(scenes[scene]);frame=Time.frameCount+10;return;}
 }catch(System.Exception ex){Log(ex.ToString());}
 SessionState.SetInt(K,2);EditorApplication.ExitPlaymode();}
}
