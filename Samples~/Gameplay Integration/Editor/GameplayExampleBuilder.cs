using System.IO;
using EasyBuilding;
using EasyInteraction;
using EasyLocalization.Editor;
using EasyResources;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

namespace EasyLocalization.Integrations.Editor
{
    public static class GameplayExampleBuilder
    {
        [MenuItem("Tools/EasyLocalization/Create Gameplay Integration Scene")]
        public static void Menu(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())Create("Assets/EasyLocalization Gameplay",true);}
        public static string Create(string folder,bool open=false)
        {
            var mapping=LocalizationSetup.Create(folder,"en",new[]{"ru"},"EasyLocalization Gameplay");
            var collection=mapping.collection;
            var resource=AssetDatabase.LoadAssetAtPath<ResourceDefinition>(folder+"/Wood.asset");
            if(!resource){resource=ResourceDefinition.Create("Wood","easy-localization.wood");AssetDatabase.CreateAsset(resource,folder+"/Wood.asset");}
            var building=AssetDatabase.LoadAssetAtPath<BuildingDefinition>(folder+"/Wall.asset");
            if(!building){building=ScriptableObject.CreateInstance<BuildingDefinition>();building.DisplayName="Wall";AssetDatabase.CreateAsset(building,folder+"/Wall.asset");}
            var catalog=AssetDatabase.LoadAssetAtPath<LocalizedCatalog>(folder+"/Gameplay Catalog.asset");
            if(!catalog)
            {
                catalog=ScriptableObject.CreateInstance<LocalizedCatalog>();
                catalog.entries=new[]{
                    Add(collection,2001,"resource."+resource.Id,"Wood","Дерево"),
                    Add(collection,2002,"building."+building.Id,"Wall","Стена"),
                    Add(collection,2003,"building.result.CannotAfford","Not enough resources","Недостаточно ресурсов"),
                    Add(collection,2004,"interaction.Use","Use","Использовать"),
                    Add(collection,2005,"interaction.Busy","Busy","Занято")};
                AssetDatabase.CreateAsset(catalog,folder+"/Gameplay Catalog.asset");
            }
            AssetDatabase.SaveAssets();
            string path=folder+"/Gameplay.unity";if(File.Exists(path)){if(open)EditorSceneManager.OpenScene(path);return path;}
            var previous=SceneManager.GetActiveScene();bool restorePrevious=!string.IsNullOrEmpty(previous.path);
            if(!restorePrevious&&previous.isDirty)throw new System.InvalidOperationException("Save the current untitled scene before creating the example.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,restorePrevious?NewSceneMode.Additive:NewSceneMode.Single);SceneManager.SetActiveScene(scene);
            var camera=new GameObject("Camera",typeof(Camera));camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;camera.GetComponent<Camera>().backgroundColor=new Color(.035f,.055f,.08f);
            var root=new GameObject("Gameplay translations");var cache=root.AddComponent<LocalizedCatalogCache>();cache.catalog=catalog;
            var adapter=root.AddComponent<GameplayTranslations>();adapter.cache=cache;
            var actor=root.AddComponent<InteractionActor>();actor.View=camera.transform;actor.RequireVisibility=false;
            var presenter=root.AddComponent<InteractionPresenter>();presenter.TextProvider=adapter;adapter.presenter=presenter;
            var target=new GameObject("Demonstration action");target.transform.position=Vector3.forward*2;
            var action=target.AddComponent<BusyExampleAction>();action.LabelKey="interaction.Use";action.ActionLabel="Use";
            var example=root.AddComponent<GameplayExample>();example.translations=adapter;example.resource=resource;example.building=building;example.presenter=presenter;example.target=action.Target;
            EditorSceneManager.SaveScene(scene,path);
            if(!open&&restorePrevious){EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);}return path;
        }
        private static CatalogTranslation Add(StringTableCollection collection,long id,string key,string english,string russian)
        {
            if(collection.SharedData.GetEntry(id)==null)
            {
                collection.SharedData.AddKey(key,id);
                var en=(StringTable)collection.GetTable("en");var ru=(StringTable)collection.GetTable("ru");en.AddEntry(id,english);ru.AddEntry(id,russian);
                EditorUtility.SetDirty(collection.SharedData);EditorUtility.SetDirty(en);EditorUtility.SetDirty(ru);
            }
            return new CatalogTranslation{id=key,text=new LocalizedString(collection.SharedData.TableCollectionNameGuid,id)};
        }
    }
}
