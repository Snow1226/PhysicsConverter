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
using PhysBone = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone;
using PhysBoneCollider = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider;

namespace Neigerium.PhysicsConverter.Editor
{
    public class ConvertPhysics
    {
        private List<ColliderPair> colliderPairs;

        public ConvertPhysics()
        {
            colliderPairs = new List<ColliderPair>();
        }

        public void ConvertComponennts<T>(GameObject obj, List<ColliderPair> colliders = null) where T : Component
        {
            var components = obj.GetComponentsInChildren<T>(true);
            foreach (var component in components)
            {
                switch (typeof(T).Name)
                {
                    case "VRCPhysBone":
                        var physBone = component as PhysBone;
                        ConvertPhysbone(physBone, colliders);
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
                        var activate = typeof(RotationConstraint).GetMethod("ActivateAndPreserveOffset", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (activate != null)
                            activate.Invoke(rotateConstraint, null);
                        break;
                }
                GameObject.DestroyImmediate(component);
            }
        }

        public void ConvertPhysbone(PhysBone physbone, List<ColliderPair> colliders)
        {
            List<Transform> mcRootBones = new List<Transform>();
            GameObject rootBone = physbone.rootTransform != null ? physbone.rootTransform.gameObject : physbone.gameObject;

            // MagicaClothはRootBoneがBoneTaleとして回転してしまうため、RootBoneの子をRootBoneとして追加する
            //mcRootBones.Add(rootBone.transform);

            var ignoreList = physbone.ignoreTransforms.ToList();
            foreach (Transform t in rootBone.transform)
            {
                //根本でignoreTransformsで分割している場合はスキップ
                if (ignoreList.Contains(t)) continue;

                if(t.childCount > 0)
                    mcRootBones.Add(t);
                else
                {
                    //子がいない場合はRootBoneに入れる
                    mcRootBones.Add(rootBone.transform);

                }
            }

            // ボーンの途中でIgnoreをRootBoneに入れると軸が止まってしまうため無視
            /*
            foreach (var endTransform in physbone.ignoreTransforms)
            {
                if (endTransform != null)
                    mcRootBones.Add(endTransform);
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
            sd.gravity = physbone.gravity;
            sd.gravityFalloff = physbone.gravityFalloff;
            sd.damping = new CurveSerializeData()
            {
                curve = physbone.pullCurve,
                value = Math.Clamp(1 - physbone.pull , 0, 1),
                useCurve = physbone.pullCurve.keys.Length == 0 ? false : true
            };
            sd.stablizationTimeAfterReset = physbone.pull;

            // Angle Restoration
            sd.angleRestorationConstraint.stiffness = new CurveSerializeData()
            {
                curve = physbone.stiffnessCurve,
                value = physbone.pull, //Math.Clamp(physbone.stiffness + physbone.spring, 0, 1),
                useCurve = physbone.stiffnessCurve.keys.Length == 0 ? false : true
            };
            //if (physbone.integrationType == VRC.Dynamics.VRCPhysBoneBase.IntegrationType.Simplified)
            //    sd.angleRestorationConstraint.stiffness.value = Math.Clamp(physbone.pull, 0, 1);
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
                        sd.colliderCollisionConstraint.colliderList.Add(targetCollider);
                }
            }

            // Self Collision


            // Build
            magicaCloth.BuildAndRun();
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

                // どのPhysboneColliderがMagicaClothColliderになるか対応させるためのペアを作成
                ColliderPair pair;
                switch (physBoneCollider.shapeType)
                {
                    case VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Sphere:
                        var magicaSphereCollider = colObj.gameObject.AddComponent<MagicaSphereCollider>();
                        magicaSphereCollider.SetSize(physBoneCollider.radius);

                        pair = new ColliderPair()
                        {
                            referencePhysboneCollider = physBoneCollider,
                            targetMagicaclothCollider = magicaSphereCollider
                        };

                        colliders.Add(pair);
                        break;
                    case VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Capsule:
                        var magicaCapsuleCollider = colObj.gameObject.AddComponent<MagicaCapsuleCollider>();
                        magicaCapsuleCollider.SetSize(physBoneCollider.radius, physBoneCollider.radius, physBoneCollider.height);
                        magicaCapsuleCollider.direction = MagicaCapsuleCollider.Direction.Y;

                        pair = new ColliderPair()
                        {
                            referencePhysboneCollider = physBoneCollider,
                            targetMagicaclothCollider = magicaCapsuleCollider
                        };

                        colliders.Add(pair);
                        break;
                    case VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Plane:
                        var magicaPlaneCollider = colObj.gameObject.AddComponent<MagicaPlaneCollider>();

                        pair = new ColliderPair()
                        {
                            referencePhysboneCollider = physBoneCollider,
                            targetMagicaclothCollider = magicaPlaneCollider
                        };

                        colliders.Add(pair);
                        break;
                    default:
                        break;
                }
            }
            return colliders;
        }
    }

}
