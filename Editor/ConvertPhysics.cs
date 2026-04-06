using MagicaCloth2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using static VRC.Dynamics.PhysBoneManager;
using static VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
using PhysBone = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone;
using PhysBoneCollider = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider;

namespace Neigerium.PhysicsConverter.Editor
{
    public class ConvertPhysics
    {
        private List<ColliderPair> colliderPairs;
        private List<ColliderPair> AvatarColliderPairs;
        public ConvertPhysics()
        {
            colliderPairs = new List<ColliderPair>();
        }

        public void ConvertComponennts<T>(GameObject obj, List<ColliderPair> colliders = null, List<ColliderComponent> avatarColliders = null) where T : Component
        {
            // IgnoreBoneの処理のため、Armatureを取得
            var animator = obj.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("Animator component is required on the root object.");
                return;
            }
            var armature = animator.GetBoneTransform(HumanBodyBones.Hips).parent;
            if (armature == null)
            {
                Debug.LogError("Armature not found. Make sure the avatar has a valid humanoid rig.");
                return;
            }

            var components = obj.GetComponentsInChildren<T>(true);
            foreach (var component in components)
            {
                switch (typeof(T).Name)
                {
                    case "VRCPhysBone":
                        var physBone = component as PhysBone;
                        ConvertPhysbone(physBone, colliders, avatarColliders, armature);
                        break;

                    case "VRCPhysBoneCollider":
                        break;

                    case "VRCRotationConstraint":
                        var vrcRotate = component as VRCRotationConstraint;
                        RotationConstraint rotateConstraint;
                        if (vrcRotate.TargetTransform != null)
                            rotateConstraint = vrcRotate.TargetTransform.gameObject.AddComponent<RotationConstraint>();
                        else
                            rotateConstraint = component.gameObject.AddComponent<RotationConstraint>();

                        rotateConstraint.weight = vrcRotate.GlobalWeight;
                        foreach (var source in vrcRotate.Sources)
                        {
                            rotateConstraint.AddSource(new ConstraintSource()
                            {
                                sourceTransform = source.SourceTransform,
                                weight = source.Weight
                            });
                        }
                        Axis axis = Axis.None;
                        if (vrcRotate.AffectsRotationX) axis |= Axis.X;
                        if (vrcRotate.AffectsRotationY) axis |= Axis.Y;
                        if (vrcRotate.AffectsRotationZ) axis |= Axis.Z;

                        rotateConstraint.rotationAxis = axis;

                        var activate = typeof(RotationConstraint).GetMethod("ActivateAndPreserveOffset", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (activate != null)
                            activate.Invoke(rotateConstraint, null);
                        break;
                }
                GameObject.DestroyImmediate(component);
            }
        }

        public void ConvertPhysbone(PhysBone physbone, List<ColliderPair> colliders, List<ColliderComponent> avatarColliders, Transform armature)
        {
            List<Transform> mcRootBones = new List<Transform>();
            GameObject rootBone = physbone.rootTransform != null ? physbone.rootTransform.gameObject : physbone.gameObject;

            var ignoreList = physbone.ignoreTransforms.ToList();
            bool childrenHasIgnoreBone = false;
            foreach (Transform t in rootBone.transform)
            {
                // ルート直下でIgnore分岐している場合はIgnore以外の子をRootBoneにいれる。
                if (ignoreList.Contains(t))
                {
                    childrenHasIgnoreBone = true;
                    break;
                }
            }
            if (childrenHasIgnoreBone)
            {
                var boneCount = mcRootBones.Count;
                foreach (Transform t in rootBone.transform)
                {
                    if (ignoreList.Contains(t)) continue;
                    mcRootBones.Add(t);
                }
                if(boneCount == mcRootBones.Count)
                {
                    // PB Constraintは現状期待した動きをしないため、AimConstraintにて代替
                    // ルート直下すべてがIgnoreBone且つコライダーがInsideの場合。
                    bool hasInsideCollider = false;
                    int insideColliderIndex = -1;
                    for(int i = 0; i< physbone.colliders.Count;i++)
                    {

                        if (physbone.colliders[i].insideBounds == true)
                        {
                            insideColliderIndex = i;
                            hasInsideCollider = true;
                            break;
                        }
                    }
                    if (hasInsideCollider && insideColliderIndex >= 0)
                    {
                        var aimConstraint = rootBone.AddComponent<AimConstraint>();
                        aimConstraint.AddSource(new ConstraintSource()
                        {
                            sourceTransform = physbone.colliders[0].transform,
                            weight = 1,
                        });
                        var activate = typeof(AimConstraint).GetMethod("ActivateAndPreserveOffset", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (activate != null)
                            activate.Invoke(aimConstraint, null);
                        return;
                    }

                    // ルート直下がすべてIgnoreBoneの場合はRootBoneをRootBoneにいれる。
                    mcRootBones.Add(rootBone.transform);
                    childrenHasIgnoreBone = false;
                }

            }
            else
            {
                // ルート直下にIgnoreがない場合はRootBoneをRootBoneにいれる。
                mcRootBones.Add(rootBone.transform);
            }
            // RootBonesが空の場合RootBoneを入れる。
            if (mcRootBones.Count == 0)
                mcRootBones.Add(rootBone.transform);


            /*
            // 理想は以下のコードでPBConstraintを実施したい。
            // ルート直下以外、もしくはルート直下のすべてにIgnoreBoneがある場合はParentConstraintをつけてIgnoreBoneをRootBoneから切り離す。
            if (!childrenHasIgnoreBone && ignoreList.count > 0)
            {
                // IgnoreBoneをRootBoneから切り離すためのTransformを作成
                Transform ignoreBones;
                ignoreBones = armature.Find("ignoreBones");
                if (ignoreBones == null)
                {
                    ignoreBones = new GameObject("ignoreBones").transform;
                    ignoreBones.SetParent(armature);
                    ignoreBones.localPosition = Vector3.zero;
                    ignoreBones.localRotation = Quaternion.identity;
                }

                foreach (var ignoreBone in ignoreList)
                {
                    if (ignoreBone != null)
                    {
                        var parentConstraint = ignoreBone.gameObject.AddComponent<ParentConstraint>();
                        parentConstraint.AddSource(new ConstraintSource()
                        {
                            sourceTransform = ignoreBone.parent,
                            weight = 1
                        });
                        var activate = typeof(ParentConstraint).GetMethod("ActivateAndPreserveOffset", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (activate != null)
                            activate.Invoke(parentConstraint, null);
                        ignoreBone.SetParent(ignoreBones);
                    }
                }
            }
            */

            //Endpointがある場合Leaf Boneを追加
            var boneTree = rootBone.GetComponentsInChildren<Transform>(true);
            if (physbone.endpointPosition != Vector3.zero)
            {
                foreach (var childTransform in boneTree)
                {
                    if (childTransform.childCount == 0)
                    {
                        var endObj = new GameObject(childTransform.name + "_End");
                        endObj.transform.SetParent(childTransform);
                        endObj.transform.localPosition = physbone.endpointPosition;
                    }
                }
            }

            //重複削除
            mcRootBones = mcRootBones.Distinct().ToList();

            // MagicaCloth2
            MagicaCloth magicaCloth = physbone.gameObject.AddComponent<MagicaCloth>();
            var sd = magicaCloth.SerializeData;
            sd.clothType = ClothProcess.ClothType.BoneCloth;
            sd.rootBones = mcRootBones;

            sd.normalAlignmentSetting.alignmentMode = NormalAlignmentSettings.AlignmentMode.Transform;
            sd.normalAlignmentSetting.adjustmentTransform = magicaCloth.gameObject.transform;

            // ## Parameters ##
            // PB Momentum = Spring

            // Force
            sd.gravity = Mathf.Clamp(physbone.gravity * (1 / physbone.pull),0,10);
            sd.gravityFalloff = physbone.gravityFalloff;
            sd.damping = new CurveSerializeData()
            {
                curve = physbone.pullCurve,
                value = Math.Clamp(0.5f - physbone.pull , 0, 1),
                useCurve = physbone.pullCurve.keys.Length == 0 ? false : true
            };
            if(sd.gravity > 1)
                sd.damping.value = Math.Clamp(1 - physbone.pull, 0, 1);

            sd.stablizationTimeAfterReset = physbone.pull;

            // Angle Restoration
            sd.angleRestorationConstraint.stiffness = new CurveSerializeData()
            {
                curve = physbone.stiffnessCurve,
                value = Math.Clamp(physbone.pull + physbone.spring, 0, 1),
                useCurve = physbone.stiffnessCurve.keys.Length == 0 ? false : true
            };
            if (physbone.integrationType == VRC.Dynamics.VRCPhysBoneBase.IntegrationType.Simplified)
            {
                if(sd.gravity > 1)
                    sd.angleRestorationConstraint.stiffness.value = Math.Clamp(physbone.pull / 10, 0, 1);
                else
                    sd.angleRestorationConstraint.stiffness.value = Math.Clamp(physbone.pull, 0, 1);
            }
            //sd.angleRestorationConstraint.velocityAttenuation = physbone.pull;
            sd.angleRestorationConstraint.gravityFalloff = physbone.gravityFalloff;

            // Angle Limit
            if (physbone.limitType != VRC.Dynamics.VRCPhysBoneBase.LimitType.None)
            {
                sd.angleLimitConstraint = new AngleConstraint.LimitSerializeData()
                {
                    useAngleLimit = true,
                    limitAngle = new CurveSerializeData()
                    {
                        curve = physbone.maxAngleXCurve,
                        value = physbone.maxAngleX,
                        useCurve = physbone.maxAngleXCurve.keys.Length == 0 ? false : true
                    }
                };
            }
            else
            {
                sd.angleLimitConstraint.useAngleLimit = false;
            }
            if (physbone.integrationType == VRC.Dynamics.VRCPhysBoneBase.IntegrationType.Simplified)
                sd.angleLimitConstraint.stiffness = physbone.pull;

            // Shape Restoration
            /*
            sd.distanceConstraint.stiffness = new CurveSerializeData()
            {
                curve = physbone.stiffnessCurve,
                value = physbone.stiffness,
                useCurve = physbone.stiffnessCurve.keys.Length == 0 ? false : true
            };
            sd.tetherConstraint.distanceCompression = physbone.pull;
            sd.triangleBendingConstraint.stiffness = 1;
            */
            // Inertia
            /*
            sd.inertiaConstraint = new InertiaConstraint.SerializeData();
            if(physbone.immobileType == VRC.Dynamics.VRCPhysBoneBase.ImmobileType.AllMotion)
            {
                sd.inertiaConstraint.worldInertia = Mathf.Clamp(1 - physbone.immobile * 2, 0, 1);
                sd.inertiaConstraint.localInertia = Mathf.Clamp(1 - physbone.immobile / 2, 0, 1);
            }
            else
            {
                sd.inertiaConstraint.worldInertia = 1;
                sd.inertiaConstraint.localInertia = Mathf.Clamp(1 - physbone.immobile / 2, 0, 1);
            }
            */

            // Movement Limit
            if (sd.angleLimitConstraint.useAngleLimit && physbone.limitRotation != Vector3.zero)
            {
                sd.motionConstraint.useBackstop = true;
            }
            else
            {
                sd.motionConstraint.useBackstop = false;
            }

            // Collider Collision
            sd.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Edge;
            sd.radius = new CurveSerializeData()
            {
                curve = physbone.radiusCurve,
                value = physbone.radius,
                useCurve = physbone.radiusCurve.keys.Length == 0 ? false : true
            };
            foreach (var collider in physbone.colliders)
            {
                if (collider != null)
                {
                    var targetCollider = colliders.Find(x => x.referencePhysboneCollider == collider).targetMagicaclothCollider;
                    if (targetCollider != null)
                    {
                        foreach (var col in targetCollider)
                            sd.colliderCollisionConstraint.colliderList.Add(col);
                    }
                }
            }
            // PBのAllow Collision Trueの場合AvatarColliderを追加
            if (physbone.allowCollision == VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.True)
            {
                foreach (var avatarCollider in avatarColliders)
                    sd.colliderCollisionConstraint.colliderList.Add(avatarCollider);
            }

            // Self Collision

            // Build
            magicaCloth.BuildAndRun();
        }

        private ColliderComponent SetHandCollider(ColliderConfig colliderConfig)
        {
            MagicaCapsuleCollider magicaCollider = null;
            try
            {
                if(colliderConfig.transform == null)
                {
                    Debug.LogError($"Collider transform is not set.");
                    return null;
                }
                var colObj = new GameObject(colliderConfig.transform.name + "_AvatarCollider");
                colObj.transform.SetParent(colliderConfig.transform);
                colObj.transform.localPosition = colliderConfig.position;

                colObj.transform.localRotation = colliderConfig.rotation;
                colObj.transform.localScale = Vector3.one;

                magicaCollider = colObj.AddComponent<MagicaCapsuleCollider>();
                magicaCollider.SetSize(colliderConfig.radius, colliderConfig.radius, colliderConfig.height);

                // Maya製だけ手が90度回ってる。保留。
                var vector = colliderConfig.transform.localPosition - colliderConfig.transform.parent.localPosition;
                if(Mathf.Abs(vector.x) > Mathf.Abs(vector.y) && Mathf.Abs(vector.x) > Mathf.Abs(vector.z))
                    magicaCollider.direction = MagicaCapsuleCollider.Direction.X;
                else if (Mathf.Abs(vector.y) > Mathf.Abs(vector.x) && Mathf.Abs(vector.y) > Mathf.Abs(vector.z))
                    magicaCollider.direction = MagicaCapsuleCollider.Direction.Y;
                else
                    magicaCollider.direction = MagicaCapsuleCollider.Direction.Z;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to set hand collider : {ex.Message}");
                return null;
            }

            return magicaCollider;
        }
        public List<ColliderComponent> ConvertAvatarColliders(GameObject obj)
        {
            List <ColliderComponent> colliders = new List<ColliderComponent>();

            VRCAvatarDescriptor descriptor = obj.GetComponent<VRCAvatarDescriptor>();
            if(descriptor == null) return null;

            ColliderComponent[] avatarColliders = new ColliderComponent[10];

            avatarColliders[0] = SetHandCollider(descriptor.collider_handL);
            avatarColliders[1] = SetHandCollider(descriptor.collider_fingerIndexL);
            avatarColliders[2] = SetHandCollider(descriptor.collider_fingerMiddleL);
            avatarColliders[3] = SetHandCollider(descriptor.collider_fingerRingL);
            avatarColliders[4] = SetHandCollider(descriptor.collider_fingerLittleL);

            avatarColliders[5] = SetHandCollider(descriptor.collider_handR);
            avatarColliders[6] = SetHandCollider(descriptor.collider_fingerIndexR);
            avatarColliders[7] = SetHandCollider(descriptor.collider_fingerMiddleR);
            avatarColliders[8] = SetHandCollider(descriptor.collider_fingerRingR);
            avatarColliders[9] = SetHandCollider(descriptor.collider_fingerLittleR);

            foreach (var avatarCollider in avatarColliders)
            {
                if (avatarCollider != null)
                    colliders.Add(avatarCollider);
            }

            if (avatarColliders.Length == 0)
            {
                // 全部失敗した場合、手にスフィアだけ入れておく
                var animator = obj.GetComponent<Animator>();
                var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

                var leftCol = new GameObject("LeftHand_AvatarCollider").AddComponent<MagicaSphereCollider>();
                leftCol.transform.SetParent(leftHand);
                leftCol.transform.localPosition = Vector3.zero;
                leftCol.transform.localRotation = Quaternion.identity;
                leftCol.transform.localScale = Vector3.one; 
                leftCol.SetSize(0.3f);

                colliders.Add(leftCol); 

                var rightCol = new GameObject("RightHand_AvatarCollider").AddComponent<MagicaSphereCollider>();
                rightCol.transform.SetParent(rightHand);
                rightCol.transform.localPosition = Vector3.zero;
                rightCol.transform.localRotation = Quaternion.identity;
                rightCol.transform.localScale = Vector3.one;
                rightCol.SetSize(0.3f);

                colliders.Add(rightCol);
            }

            return colliders;
        }


        public List<ColliderPair> ConvertColliders<T>(GameObject obj) where T : Component
        {
            List<ColliderPair> colliders = new List<ColliderPair>();

            if (typeof(T).Name != "VRCPhysBoneCollider") return colliders;

            var components = obj.GetComponentsInChildren<T>(true);
            foreach (var component in components)
            {
                var physBoneCollider = component as PhysBoneCollider;

                // PhysboneのPositionとRotationを子オブジェクトで再現
                var colObj = new GameObject(physBoneCollider.gameObject.name + "_Collider");
                if (physBoneCollider.rootTransform != null)
                    colObj.transform.SetParent(physBoneCollider.rootTransform);
                else
                    colObj.transform.SetParent(physBoneCollider.gameObject.transform);

                colObj.transform.localPosition = physBoneCollider.position;
                colObj.transform.localRotation = physBoneCollider.rotation;
                colObj.transform.localScale = Vector3.one;

                // どのPhysboneColliderがMagicaClothColliderになるか対応させるためのペアを作成
                ColliderPair pair;

                Vector3[] insidePlaneRotate = new Vector3[]
                {
                    new Vector3(0, 0, 0),
                    new Vector3(0, 0, 90),
                    new Vector3(0, 0, 180),
                    new Vector3(0, 0, 270),
                    new Vector3(90, 0, 0),
                    new Vector3(270, 0, 0),
                };

                if (physBoneCollider.insideBounds)
                {
                    List<ColliderComponent> insideColliders = new List<ColliderComponent>();
                    foreach (var rotate in insidePlaneRotate)
                    {
                        var magicaPlaneCollider = new GameObject(physBoneCollider.gameObject.name + "insidePlane").AddComponent<MagicaPlaneCollider>();
                        magicaPlaneCollider.transform.SetParent(colObj.transform);
                        magicaPlaneCollider.transform.localPosition = Vector3.zero;
                        magicaPlaneCollider.transform.localRotation = Quaternion.Euler(rotate);
                        magicaPlaneCollider.center = new Vector3(0, -physBoneCollider.radius, 0);

                        insideColliders.Add(magicaPlaneCollider);
                    }

                    pair = new ColliderPair()
                    {
                        referencePhysboneCollider = physBoneCollider,
                        targetMagicaclothCollider = insideColliders.ToArray()
                    };
                    colliders.Add(pair);
                }
                else
                {
                    switch (physBoneCollider.shapeType)
                    {
                        case VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Sphere:
                            var magicaSphereCollider = colObj.gameObject.AddComponent<MagicaSphereCollider>();
                            magicaSphereCollider.SetSize(physBoneCollider.radius);

                            pair = new ColliderPair()
                            {
                                referencePhysboneCollider = physBoneCollider,
                                targetMagicaclothCollider = new ColliderComponent[] { magicaSphereCollider }                            };

                            colliders.Add(pair);
                            break;
                        case VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Capsule:
                            var magicaCapsuleCollider = colObj.gameObject.AddComponent<MagicaCapsuleCollider>();
                            magicaCapsuleCollider.SetSize(physBoneCollider.radius, physBoneCollider.radius, physBoneCollider.height);
                            magicaCapsuleCollider.direction = MagicaCapsuleCollider.Direction.Y;

                            pair = new ColliderPair()
                            {
                                referencePhysboneCollider = physBoneCollider,
                                targetMagicaclothCollider = new ColliderComponent[] { magicaCapsuleCollider }
                            };

                            colliders.Add(pair);
                            break;
                        case VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Plane:
                            var magicaPlaneCollider = colObj.gameObject.AddComponent<MagicaPlaneCollider>();

                            pair = new ColliderPair()
                            {
                                referencePhysboneCollider = physBoneCollider,
                                targetMagicaclothCollider = new ColliderComponent[] { magicaPlaneCollider }
                            };

                            colliders.Add(pair);
                            break;
                        default:
                            break;
                    }
                }

            }
            return colliders;
        }
    }

}
