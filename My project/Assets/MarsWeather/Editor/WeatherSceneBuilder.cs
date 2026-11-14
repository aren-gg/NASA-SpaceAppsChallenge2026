using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace MarsWeather.Editor
{
    public static class WeatherSceneBuilder
    {
        const string Root="Assets/MarsWeather";
        [MenuItem("Mars Weather/Rebuild Preview Scene")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(Root+"/Materials"); System.IO.Directory.CreateDirectory(Root+"/Prefabs"); System.IO.Directory.CreateDirectory(Root+"/Scenes");
            AssetDatabase.Refresh();
            var sky=Material("WeatherSky","MarsWeather/Atmosphere",Color.white);
            var dust=Material("SoftDust","MarsWeather/SoftDust",Color.white);
            var ground=Material("PreviewSand","Universal Render Pipeline/Lit",new Color(.48f,.28f,.23f));
            var dark=Material("PreviewGraphite","Universal Render Pipeline/Lit",new Color(.18f,.15f,.20f));
            var ceramic=Material("PreviewCeramic","Universal Render Pipeline/Lit",new Color(.72f,.53f,.46f));
            var copper=Material("PreviewCopper","Universal Render Pipeline/Lit",new Color(.8f,.39f,.19f));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sunObject=new GameObject("Preview Sun"); var sun=sunObject.AddComponent<Light>(); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(19,-30,0); sun.color=new Color(1,.69f,.48f); sun.intensity=1.2f; sun.shadows=LightShadows.Soft;
            var cameraObject=new GameObject("Preview Camera",typeof(Camera),typeof(AudioListener)); cameraObject.tag="MainCamera";
            cameraObject.transform.position=new Vector3(15,7,-22); cameraObject.transform.LookAt(new Vector3(0,3,12));
            var camera=cameraObject.GetComponent<Camera>(); camera.fieldOfView=52;camera.farClipPlane=300;camera.allowHDR=true;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var stage=new GameObject("Preview stage — remove when merging");
            Primitive("Flat preview surface",PrimitiveType.Plane,Vector3.zero,new Vector3(60,1,60),ground,stage.transform);
            Primitive("Instrument platform",PrimitiveType.Cylinder,new Vector3(0,.12f,5),new Vector3(7,.12f,7),dark,stage.transform);
            for(int i=0;i<7;i++)
            {
                float x=(i%2==0?-1:1)*(5+i*1.8f), z=2+i*7;
                Primitive("Visibility marker "+i,PrimitiveType.Cylinder,new Vector3(x,1.5f,z),new Vector3(.45f,1.5f,.45f),ceramic,stage.transform);
                for(int band=0;band<4;band++) Primitive("Copper band",PrimitiveType.Cylinder,new Vector3(x,.7f+band*.6f,z),new Vector3(.48f,.07f,.48f),copper,stage.transform);
                Primitive("Instrument cap",PrimitiveType.Sphere,new Vector3(x,3.05f,z),new Vector3(.6f,.38f,.6f),dark,stage.transform);
            }
            var sphere=Primitive("Atmosphere reference globe",PrimitiveType.Sphere,new Vector3(0,2.4f,5),Vector3.one*3.2f,ceramic,stage.transform);
            Primitive("Globe plinth",PrimitiveType.Cylinder,new Vector3(0,.65f,5),new Vector3(1.4f,.65f,1.4f),dark,stage.transform);
            for(int i=0;i<3;i++)
            {
                var ring=new GameObject("Instrument ring",typeof(MeshFilter),typeof(MeshRenderer)); ring.transform.SetParent(stage.transform); ring.transform.position=sphere.transform.position;
                ring.transform.rotation=Quaternion.Euler(i*35,0,25+i*25);
                var mesh=Ring(2.05f,.045f); string path=Root+"/Materials/Ring"+i+".asset";
                var old=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(old==null) AssetDatabase.CreateAsset(mesh,path); else {EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;}
                ring.GetComponent<MeshFilter>().sharedMesh=mesh;ring.GetComponent<MeshRenderer>().sharedMaterial=copper;
            }
            var weatherObject=new GameObject("Mars Weather"); var controller=weatherObject.AddComponent<WeatherController>(); controller.skyTemplate=sky;controller.particleMaterial=dust;
            var prefab=PrefabUtility.SaveAsPrefabAsset(weatherObject,Root+"/Prefabs/MarsWeather.prefab"); Object.DestroyImmediate(weatherObject);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);controller=instance.GetComponent<WeatherController>();controller.sun=sun;
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            var demo=new GameObject("Preview controls").AddComponent<WeatherPreview>();demo.weather=controller;
            RenderSettings.skybox=sky;RenderSettings.fog=true;RenderSettings.fogDensity=.006f;RenderSettings.fogColor=new Color(.64f,.58f,.55f);
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/WeatherPreview.unity");
            AssetDatabase.SaveAssets();
            Debug.Log("WEATHER_PREVIEW_CREATED");
        }
        static Material Material(string name,string shader,Color color)
        {
            string path=Root+"/Materials/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) {m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
            if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.12f);return m;
        }
        static GameObject Primitive(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material,Transform parent)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent);go.transform.position=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        static Mesh Ring(float radius,float tube)
        {
            const int segments=96,sides=12;var v=new Vector3[(segments+1)*(sides+1)];var n=new Vector3[v.Length];var tris=new int[segments*sides*6];int index=0;
            for(int i=0;i<=segments;i++)for(int j=0;j<=sides;j++)
            {
                float a=i*Mathf.PI*2/segments,b=j*Mathf.PI*2/sides;int k=i*(sides+1)+j;
                n[k]=new Vector3(Mathf.Cos(a)*Mathf.Cos(b),Mathf.Sin(b),Mathf.Sin(a)*Mathf.Cos(b));
                v[k]=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius)+n[k]*tube;
                if(i<segments&&j<sides){int next=k+sides+1;tris[index++]=k;tris[index++]=k+1;tris[index++]=next;tris[index++]=k+1;tris[index++]=next+1;tris[index++]=next;}
            }
            var mesh=new Mesh();mesh.vertices=v;mesh.normals=n;mesh.triangles=tris;return mesh;
        }
    }
}
