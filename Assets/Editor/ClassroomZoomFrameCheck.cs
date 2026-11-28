using System.IO;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] static class ClassroomZoomFrameCheck {
 const string dir="Library/ZoomFrameCheck/",key="ZoomFrameCheck1";
 static ClassroomZoomFrameCheck(){EditorApplication.update+=Start;EditorApplication.playModeStateChanged+=s=>{if(!SessionState.GetBool(key,false))return;if(s==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=()=>Object.FindFirstObjectByType<InteractionManager>().StartCoroutine(Test());if(s==PlayModeStateChange.EnteredEditMode){var old=SessionState.GetString(key+"old","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(old)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(old);SessionState.SetBool(key,false);File.AppendAllText(dir+"result","Restored\n");}};}
 static void Start(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||File.Exists(dir+"result"))return;Directory.CreateDirectory(dir);File.WriteAllText(dir+"result","Running\n");SessionState.SetString(key+"old",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)??"");SessionState.SetBool(key,true);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Classroom.unity");EditorApplication.EnterPlaymode();}
 static IEnumerator Test(){yield return new WaitForSeconds(1f);var z=Object.FindFirstObjectByType<NPCFaceZoom>(FindObjectsInactive.Include);for(var t=z.transform;t;t=t.parent)t.gameObject.SetActive(true);z.OpenFaceZoom();var p=GameObject.Find("NPC Face Zoom Panel");File.AppendAllText(dir+"result","Panel lift "+(p.transform.position.y-Camera.main.transform.position.y)+"\n");var cam=z.GetComponentInChildren<Camera>();var buttons=p.GetComponentsInChildren<Button>().Where(b=>b.GetComponentInChildren<TMPro.TMP_Text>() && b.GetComponentInChildren<TMPro.TMP_Text>().text!="Exit NPC Face").ToArray();foreach(var b in buttons){b.onClick.Invoke();yield return new WaitForSeconds(.5f);yield return new WaitForEndOfFrame();var rt=cam.targetTexture;var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();var name=b.GetComponentInChildren<TMPro.TMP_Text>().text;File.WriteAllBytes(dir+name+".jpg",tex.EncodeToJPG(82));RenderTexture.active=old;Object.Destroy(tex);File.AppendAllText(dir+"result",name+"\n");}EditorApplication.ExitPlaymode();}
}
