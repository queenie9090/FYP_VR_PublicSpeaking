using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
[InitializeOnLoad] public static class SceneMotionReview {
 const string dir="Library/SceneMotionReview/", path="Assets/Editor/MotionReviewTemporary.unity", flag="SceneMotionReviewActive";
 static void Log(string s)=>File.AppendAllText(dir+"result.txt",s+"\n");
 static SceneMotionReview(){
 EditorApplication.delayCall+=()=>{if(SessionState.GetBool(flag,false) && EditorApplication.isPlaying && File.Exists(dir+"go")) EditorApplication.ExitPlaymode();else Prepare();};
 EditorApplication.playModeStateChanged+=s=>{
 if(!SessionState.GetBool(flag,false))return;
 if(s==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=Run;
 if(s==PlayModeStateChange.EnteredEditMode){string old=SessionState.GetString(flag+"Old","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(old)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(old);SessionState.SetBool(flag,false);AssetDatabase.DeleteAsset(path);Log("RESTORED");EditorApplication.delayCall+=Prepare;}
 }; }
 static void Prepare(){
 if(!File.Exists(dir+"go")||File.Exists(path)||EditorApplication.isPlayingOrWillChangePlaymode)return;
 File.Delete(dir+"go");Scene original=SceneManager.GetActiveScene();Scene fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
 try{
 int index=0;
 foreach(string p in Directory.GetFiles("Assets/Scenes","*.unity")){
 var scene=EditorSceneManager.OpenPreviewScene(p.Replace((char)92,(char)47));try{
 var actors=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<NPCEmotionController>(true)).Where(f=>f.gameObject.activeSelf && f.GetComponent<Animator>()).ToArray();
 Log(scene.name+" rigs="+actors.Length);
 var map=new Dictionary<Animator,Animator>();
 foreach(var f in actors){
 var copy=Object.Instantiate(f.gameObject);SceneManager.MoveGameObjectToScene(copy,fixture);copy.name=Path.GetFileNameWithoutExtension(p)+"_"+f.name;copy.transform.position=new Vector3(index++*4,0,0);copy.transform.rotation=Quaternion.identity;
 foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true))if(b && !(b is NPCEmotionController || b is BusinessmanEmotionGesture || b is NPCBodyAnimation || b is NPCPersona || b is CafeteriaEmotionGestures || b is ClassroomStudentGestures)) b.enabled=false;
 var a=copy.GetComponent<Animator>();a.cullingMode=AnimatorCullingMode.AlwaysAnimate;map[f.GetComponent<Animator>()]=a;
 }
 var hall=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<HallAudienceCelebration>(true)).FirstOrDefault();
 if(hall){var go=new GameObject("Hall motion fixture");SceneManager.MoveGameObjectToScene(go,fixture);
 var roster=go.AddComponent<HallNPCRealismScreen>();roster.enabled=false;
 var h=go.AddComponent<HallAudienceCelebration>();EditorUtility.CopySerialized(hall,h);
 var src=new SerializedObject(hall);var dest=new SerializedObject(h);var rec=new SerializedObject(roster);
 foreach(var n in new[]{"judge","audienceA","audienceB"}){var old=(Animator)src.FindProperty(n).objectReferenceValue;var a=map[old];dest.FindProperty(n).objectReferenceValue=a;rec.FindProperty(n).objectReferenceValue=a.GetComponent<NPCEmotionController>();}
 dest.FindProperty("soundVolume").floatValue=0;dest.ApplyModifiedPropertiesWithoutUndo();rec.ApplyModifiedPropertiesWithoutUndo();
 }
 } finally{EditorSceneManager.ClosePreviewScene(scene);}
 }
 EditorSceneManager.SaveScene(fixture,path);
 }catch(Exception e){Log("ERROR "+e);return;}finally{SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(fixture,true);}
 SessionState.SetString(flag+"Old",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)??"");EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(path);SessionState.SetBool(flag,true);EditorApplication.EnterPlaymode();
 }
 static void Run(){var runner=new GameObject("Motion sampling").AddComponent<SpeechInteractionController>();runner.StartCoroutine(Sample());}
 static IEnumerator Sample(){
 var actors=Object.FindObjectsByType<NPCEmotionController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
 var hall=Object.FindFirstObjectByType<HallAudienceCelebration>();var cam=new GameObject("Review camera").AddComponent<Camera>();cam.tag="MainCamera";cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.gray;
 var lamp=new GameObject("Review light").AddComponent<Light>();lamp.type=LightType.Directional;lamp.transform.rotation=Quaternion.Euler(35,155,0);lamp.intensity=2;
 var rt=new RenderTexture(320,320,24);cam.targetTexture=rt;cam.enabled=false;
 Time.captureDeltaTime=1f/60f;
 var poses=new[]{"NEUTRAL","ANGRY","THINKING","NERVOUS","SAD","HAPPY","NEUTRAL"};
 for(int k=0;k<poses.Length;k++){
 var last=new Dictionary<Animator,Vector3>();var max=new Dictionary<Animator,float>();var travel=new Dictionary<Animator,float>();
 foreach(var f in actors){f.currentEmotion=poses[k];f.emotionIntensity=k==0||k==6?0:80;var body=f.GetComponent<NPCBodyAnimation>();if(body && body.enabled)body.SetActivity((NPCBodyActivity)(k%4));}
 if(hall&&k==5)hall.OnSessionStarted();
 for(int t=0;t<(k==1?130:50);t++){
 yield return new WaitForEndOfFrame();
 foreach(var f in actors){var a=f.GetComponent<Animator>();if(!a.isHuman)continue;var hand=a.GetBoneTransform(HumanBodyBones.RightHand);if(!hand)continue;
 var pos=a.transform.InverseTransformPoint(hand.position);if(last.TryGetValue(a,out var prev)){float d=Vector3.Distance(pos,prev);max[a]=Mathf.Max(max.TryGetValue(a,out var m)?m:0,d);travel[a]=(travel.TryGetValue(a,out var v)?v:0)+d;}last[a]=pos;
 if(t==40 && (k==1||k==5)){var chest=a.GetBoneTransform(HumanBodyBones.Chest);if(!chest)continue;Vector3 center=chest.position;cam.transform.position=center+a.transform.forward*2.0f+a.transform.right*.35f+Vector3.up*.05f;cam.transform.LookAt(center);cam.Render();RenderTexture.active=rt;var tex=new Texture2D(320,320,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,320,320),0,0);tex.Apply();File.WriteAllBytes(dir+f.name+"_"+poses[k]+".png",tex.EncodeToPNG());Object.Destroy(tex);RenderTexture.active=null;}
 }}
 foreach(var a in max.Keys)Log($"{a.name} {poses[k]} maxStep={max[a]:F3} travel={travel[a]:F3}");
 }
 Time.captureDeltaTime=0;EditorApplication.ExitPlaymode();
 }
}



 
