using UnityEditor;
using UnityEngine;
namespace MarsWeather.Editor
{
    /// <summary>
    /// Rebuilds the reusable weather prefab and its sky and dust materials. The weather is
    /// previewed in the Mars scene itself (keys F5-F9), so there is no separate preview scene.
    /// </summary>
    public static class WeatherSceneBuilder
    {
        const string Root="Assets/MarsWeather";
        [MenuItem("Mars Weather/Rebuild Weather Prefab")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(Root+"/Materials"); System.IO.Directory.CreateDirectory(Root+"/Prefabs");
            AssetDatabase.Refresh();
            var sky=Material("WeatherSky","MarsWeather/Atmosphere");
            var dust=Material("SoftDust","MarsWeather/SoftDust");
            var weatherObject=new GameObject("Mars Weather"); var controller=weatherObject.AddComponent<WeatherController>(); controller.skyTemplate=sky;controller.particleMaterial=dust;
            PrefabUtility.SaveAsPrefabAsset(weatherObject,Root+"/Prefabs/MarsWeather.prefab"); Object.DestroyImmediate(weatherObject);
            AssetDatabase.SaveAssets();
            Debug.Log("Weather prefab rebuilt at "+Root+"/Prefabs/MarsWeather.prefab");
        }
        static Material Material(string name,string shader)
        {
            string path=Root+"/Materials/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) {m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
            return m;
        }
    }
}
