using MagicaCloth2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using PhysBone = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone;
using PhysBoneCollider = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider;
using nadena.dev.ndmf;

#if UNITY_EDITOR
namespace Neigerium.PhysicsConverter.Editor
{
    public class AvatarConvertWindow : EditorWindow
    {
        private GameObject _targetAvatar;

        private GameObject _physicsSourceAvatar;
        private GameObject _physicsTargetAvatar;

        private bool _destroyObjectFoldOpen = true;
        private bool _magicaClothFoldOpen = false; 
        private Vector2 _clothScrollPosition = Vector2.zero;
        private Vector2 _colliderScrollPosition = Vector2.zero;

        private List<MagicaCloth> _magicaClothList = new List<MagicaCloth>();
        private List<ColliderComponent> _magicaColliderList = new List<ColliderComponent>();

        private bool _invokeChange = false;
        private bool _isPlaying = false;

        [SerializeField] public List<DestroyCondition> conditions = new List<DestroyCondition>();
        private GameObject _prevTarget;
        private Vector2 _destroyListScrollPosition = Vector2.zero;

        [MenuItem("Tools/Neigerium/Physics Converter(Physbone to MagicaCloth2)")]
        public static void Init()
        {
            var window = GetWindow<AvatarConvertWindow>("Physics Converter");
        }

        private void OnEnable()
        {
            _invokeChange = true;
        }

        private void Update()
        {
            var playmode = EditorApplication.isPlaying;
            if(playmode != _isPlaying)
            {
                _invokeChange = true;
                _isPlaying = playmode;
            }
        }

        private void OnGUI()
        {
            using(new GUILayout.VerticalScope())
            {
                //////////////////////////////////////////////////////////////////////////////////////////
                // for VMC
                GUILayout.Label("Avatar Convert ( for VirtualMotionCapture Mod )", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                {
                    EditorGUILayout.LabelField("Target Avatar");
                    var allowSceneObjects = !EditorUtility.IsPersistent(_targetAvatar);

                    EditorGUI.BeginChangeCheck();
                    _targetAvatar = (GameObject)EditorGUILayout.ObjectField(_targetAvatar, typeof(GameObject), allowSceneObjects);
                    if (EditorGUI.EndChangeCheck() || _invokeChange)
                    {
                        _magicaClothList.Clear();
                        _magicaColliderList.Clear();

                        if (_targetAvatar != null && _targetAvatar.GetComponent<VRCAvatarDescriptor>() == null)
                        {
                            var clothList = _targetAvatar.GetComponentsInChildren<MagicaCloth>(true);
                            _magicaClothList.AddRange(clothList);

                            var colliderList = _targetAvatar.GetComponentsInChildren<ColliderComponent>(true);
                            _magicaColliderList.AddRange(colliderList);
                        }
                        if (_targetAvatar != _prevTarget)
                        {
                            var animator = _targetAvatar.GetComponent<Animator>();
                            if (animator != null)
                            {
                                var armature = animator.GetBoneTransform(HumanBodyBones.Hips).parent;
                                if (armature != null)
                                {
                                    conditions.Clear();
                                    foreach (Transform t in _targetAvatar.transform)
                                    {
                                        if (t != armature)
                                        {
                                            conditions.Add(new DestroyCondition()
                                            {
                                                IsDestroy = false,
                                                ObjectName = t.gameObject.name
                                            });
                                        }
                                    }
                                }
                            }
                            _prevTarget = _targetAvatar;
                        }
                        _invokeChange = false;
                    }
                }

                if (_targetAvatar != null)
                {
                    if (_targetAvatar.GetComponent<VRCAvatarDescriptor>() != null)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("\nConvert & Export Avatar\n"))
                            {
                                var exportpath = EditorUtility.SaveFilePanel("VMCMod Avatar Save", "", _targetAvatar.name, "avatar");

                                var bakedAvatar = BakeModularAvatar(_targetAvatar);
                                var convertAvatar = ConvertAvatar(bakedAvatar);
                                SaveAvatar(convertAvatar, exportpath);

                                DestroyImmediate(convertAvatar);
                                DestroyImmediate(bakedAvatar);
                            }
                            if (GUILayout.Button("\nConvert Avatar\n"))
                            {
                                var bakedAvatar = BakeModularAvatar(_targetAvatar);
                                _targetAvatar = ConvertAvatar(bakedAvatar);
                                _invokeChange = true;

                                DestroyImmediate(bakedAvatar);
                            }
                        }

                        EditorGUILayout.Space();
                        _destroyObjectFoldOpen = EditorGUILayout.Foldout(_destroyObjectFoldOpen, "DestroyObject Setting");
                        if (_destroyObjectFoldOpen)
                        {
                            using(new EditorGUILayout.VerticalScope(GUI.skin.box))
                            {
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.LabelField("Destroy", GUILayout.Width(60));
                                    EditorGUILayout.LabelField("Object Name");
                                }

                                _destroyListScrollPosition = EditorGUILayout.BeginScrollView(_destroyListScrollPosition);

                                foreach (var condition in conditions)
                                {
                                    using(new EditorGUILayout.HorizontalScope())
                                    {
                                        condition.IsDestroy = EditorGUILayout.Toggle(condition.IsDestroy, GUILayout.Width(60));
                                        EditorGUILayout.LabelField(condition.ObjectName);
                                    }
                                }

                                EditorGUILayout.EndScrollView();
                            }
                        }
                    }
                    else if (_targetAvatar.GetComponent<VRCAvatarDescriptor>() == null)
                    {
                        if (GUILayout.Button("\nExport Avatar\n"))
                        {
                            SaveAvatar(_targetAvatar);
                        }

                        _magicaClothFoldOpen = EditorGUILayout.Foldout(_magicaClothFoldOpen, "MagicaClothV2 Component");
                        if (_magicaClothFoldOpen)
                        {
                            //EditorGUILayout.LabelField("MagicaClothV2 Component");
                            using (new GUILayout.HorizontalScope())
                            {
                                using (new GUILayout.VerticalScope())
                                {
                                    EditorGUILayout.LabelField("Cloth");
                                    _clothScrollPosition = EditorGUILayout.BeginScrollView(_clothScrollPosition);
                                    foreach (var cloth in _magicaClothList)
                                    {
                                        EditorGUILayout.ObjectField(cloth, typeof(MagicaCloth), true);
                                    }

                                    EditorGUILayout.EndScrollView();

                                }
                                using (new GUILayout.VerticalScope())
                                {
                                    EditorGUILayout.LabelField("Collider");
                                    _colliderScrollPosition = EditorGUILayout.BeginScrollView(_colliderScrollPosition);
                                    foreach (var collider in _magicaColliderList)
                                    {
                                        EditorGUILayout.ObjectField(collider, typeof(ColliderComponent), true);
                                    }

                                    EditorGUILayout.EndScrollView();

                                }
                            }
                        }

                    }
                }
                else
                {
                    GUIStyle style = new GUIStyle(EditorStyles.label);
                    style.wordWrap = true;
                    EditorGUILayout.LabelField("Enter Avatar object with  \"VRC Avatar Descriptor\" in \"Target Avatar\".", style);
                }

            }
        }
        private GameObject BakeModularAvatar(GameObject baseAvatar)
        {
            GameObject cloneAvatar = GameObject.Instantiate(baseAvatar);
            GameObject bakedAvatar = null;
            //bool enableBake = false;

            // 不要オブジェクトの削除
            try
            {
                // foreach(transform in RootObject.transform)ではリストされない子オブジェクトがいる？
                var childrens = new Transform[cloneAvatar.transform.childCount];
                for (int i = 0; i < childrens.Length; i++)
                    childrens[i] = cloneAvatar.transform.GetChild(i);

                foreach (Transform t in childrens)
                {
                    if (t != null)
                    {
                        foreach (DestroyCondition condition in conditions)
                        {
                            if (condition.IsDestroy)
                            {
                                Debug.Log($"Clone Object : {t.gameObject.name} / DestroyCondition {condition.ObjectName}");
                                if (t.gameObject.name == condition.ObjectName)
                                {
                                    GameObject.DestroyImmediate(t.gameObject);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Destroy Object exception {e}");
            }

            // ManualBake
            bakedAvatar = AvatarProcessor.ManualProcessAvatar(cloneAvatar);
            bakedAvatar.transform.transform.position = Vector3.zero;
            bakedAvatar.name = baseAvatar.name;
            GameObject.DestroyImmediate(cloneAvatar);

            return bakedAvatar;
        }

        private GameObject ConvertAvatar(GameObject baseAvatar)
        {
            if (baseAvatar == null) return null;

            var converter = new ConvertPhysics();

            var cloneAvatar = GameObject.Instantiate(baseAvatar);
            cloneAvatar.name = baseAvatar.name + "(Convert)";

            var avatarColliders = converter.ConvertAvatarColliders(cloneAvatar);
            if(avatarColliders == null)
                Debug.LogWarning("No colliders found in the avatar. Please check if the avatar has colliders or if they are properly set up.");
            var colliders = converter.ConvertColliders<PhysBoneCollider>(cloneAvatar);
            converter.ConvertComponennts<PhysBone>(cloneAvatar, colliders, avatarColliders);

            converter.ConvertComponennts<PhysBoneCollider>(cloneAvatar);
            converter.ConvertComponennts<VRCRotationConstraint>(cloneAvatar);

            var comps = cloneAvatar.GetComponentsInChildren<Behaviour>(true);
            var destroyList = new string[] { "PortableDynamicBone", "VRC", "NDMF", "Pipeline" };
            foreach (var comp in comps)
            {
                foreach (string d in destroyList)
                {
                    if (comp.GetType().Name.ToLower().Contains(d.ToLower()))
                        GameObject.DestroyImmediate(comp);
                }
            }


            return cloneAvatar;
        }
        private void SaveAvatar(GameObject avatar, string path = "")
        {
            string exportpath;
            if (string.IsNullOrEmpty(path))
                exportpath = EditorUtility.SaveFilePanel("VMCMod Avatar Save", "", avatar.name, "avatar");
            else
                exportpath = path;


            if (!string.IsNullOrEmpty(exportpath))
            {
                var instance = GameObject.Instantiate(avatar);
                instance.name = avatar.name;
                instance.transform.position = avatar.transform.position;

                Animator animator = instance.GetComponent<Animator>();
                if (animator != null)
                    animator.runtimeAnimatorController = null;

                string localPath = "Assets/" + avatar.name + ".prefab";
                PrefabUtility.SaveAsPrefabAssetAndConnect(instance, localPath, InteractionMode.UserAction);

                AssetBundleBuild[] assetbundleMap = new AssetBundleBuild[1];
                assetbundleMap[0].assetBundleName = Path.GetFileName(exportpath);
                assetbundleMap[0].assetNames = new string[] { localPath };

                string outputPath = $"{Application.temporaryCachePath}/{Path.GetRandomFileName()}";
                Directory.CreateDirectory(outputPath);
                if (BuildPipeline.BuildAssetBundles(outputPath, assetbundleMap, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows))
                {
                    if (File.Exists(exportpath)) File.Delete(exportpath);
                    File.Copy(outputPath + "/" + assetbundleMap[0].assetBundleName, exportpath);
                    Directory.Delete(Application.temporaryCachePath, true);
                }

                GameObject.DestroyImmediate(instance);
                File.Delete(localPath);
                AssetDatabase.Refresh();
                UnityEditor.EditorUtility.DisplayDialog("VMCMod Avatar Save", "Avatar出力完了", "OK");
            }
        }
    }
}
#endif