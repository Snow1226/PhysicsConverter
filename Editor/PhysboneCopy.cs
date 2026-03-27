using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using PhysBone = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone;
using PhysBoneCollider = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider;

namespace Neigerium
{
    public static class PhysboneCopy
    {
        public sealed class CopyResult
        {
            public List<PhysBoneCollider> Colliders { get; } = new List<PhysBoneCollider>();
            public List<PhysBone> PhysBones { get; } = new List<PhysBone>();
        }

        public static CopyResult CopyPhysbonesAndColliders(GameObject sourceRoot, GameObject targetRoot)
        {
            if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
            if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));

            var transformMap = BuildTransformMap(sourceRoot.transform, targetRoot.transform);
            var objectMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var result = new CopyResult();

            foreach (var pair in transformMap)
            {
                objectMap[pair.Key] = pair.Value;
                objectMap[pair.Key.gameObject] = pair.Value.gameObject;
            }

            foreach (var sourceCollider in sourceRoot.GetComponentsInChildren<PhysBoneCollider>(true))
            {
                if (!transformMap.TryGetValue(sourceCollider.transform, out var targetTransform))
                {
                    Debug.LogWarning($"PhysBoneCollider copy skipped: {sourceCollider.name}", sourceRoot);
                    continue;
                }

                var copiedCollider = CopyAsNewComponent(sourceCollider, targetTransform.gameObject);
                if (copiedCollider == null)
                {
                    continue;
                }

                objectMap[sourceCollider] = copiedCollider;
                result.Colliders.Add(copiedCollider);
            }

            foreach (var sourcePhysBone in sourceRoot.GetComponentsInChildren<PhysBone>(true))
            {
                if (!transformMap.TryGetValue(sourcePhysBone.transform, out var targetTransform))
                {
                    Debug.LogWarning($"PhysBone copy skipped: {sourcePhysBone.name}", sourceRoot);
                    continue;
                }

                var copiedPhysBone = CopyAsNewComponent(sourcePhysBone, targetTransform.gameObject);
                if (copiedPhysBone == null)
                {
                    continue;
                }

                objectMap[sourcePhysBone] = copiedPhysBone;
                result.PhysBones.Add(copiedPhysBone);
            }

            foreach (var collider in result.Colliders)
            {
                RemapObjectReferences(collider, sourceRoot.transform, transformMap, objectMap);
            }

            foreach (var physBone in result.PhysBones)
            {
                RemapObjectReferences(physBone, sourceRoot.transform, transformMap, objectMap);
            }

            return result;
        }

        private static T CopyAsNewComponent<T>(T sourceComponent, GameObject targetObject) where T : Component
        {
            ComponentUtility.CopyComponent(sourceComponent);
            if (!ComponentUtility.PasteComponentAsNew(targetObject))
            {
                return null;
            }

            var targetComponents = targetObject.GetComponents<T>();
            return targetComponents.Length > 0 ? targetComponents[targetComponents.Length - 1] : null;
        }

        private static void RemapObjectReferences(Component targetComponent, Transform sourceRoot, IReadOnlyDictionary<Transform, Transform> transformMap, IReadOnlyDictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
        {
            var serializedObject = new SerializedObject(targetComponent);
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                enterChildren = true;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                var sourceReference = iterator.objectReferenceValue;
                if (sourceReference == null)
                {
                    continue;
                }

                if (!TryMapReference(sourceReference, sourceRoot, transformMap, objectMap, out var mappedReference))
                {
                    if (targetComponent is PhysBone && sourceReference is PhysBoneCollider)
                    {
                        iterator.objectReferenceValue = null;
                    }

                    continue;
                }

                iterator.objectReferenceValue = mappedReference;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(targetComponent);
        }

        private static bool TryMapReference(UnityEngine.Object sourceReference, Transform sourceRoot, IReadOnlyDictionary<Transform, Transform> transformMap, IReadOnlyDictionary<UnityEngine.Object, UnityEngine.Object> objectMap, out UnityEngine.Object mappedReference)
        {
            mappedReference = null;

            if (objectMap.TryGetValue(sourceReference, out mappedReference))
            {
                return true;
            }

            switch (sourceReference)
            {
                case GameObject sourceGameObject:
                    if (!transformMap.TryGetValue(sourceGameObject.transform, out var mappedGameObjectTransform))
                    {
                        return false;
                    }

                    mappedReference = mappedGameObjectTransform.gameObject;
                    return true;

                case Transform sourceTransform:
                    if (!transformMap.TryGetValue(sourceTransform, out var mappedTransform))
                    {
                        return false;
                    }

                    mappedReference = mappedTransform;
                    return true;

                case Component sourceComponent:
                    if (!IsUnderRoot(sourceComponent.transform, sourceRoot))
                    {
                        return false;
                    }

                    if (!TryGetMatchingComponent(sourceComponent, transformMap, out var mappedComponent))
                    {
                        return false;
                    }

                    mappedReference = mappedComponent;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryGetMatchingComponent(Component sourceComponent, IReadOnlyDictionary<Transform, Transform> transformMap, out Component targetComponent)
        {
            targetComponent = null;
            if (!transformMap.TryGetValue(sourceComponent.transform, out var targetTransform))
            {
                return false;
            }

            var sourceIndex = GetComponentIndex(sourceComponent);
            if (sourceIndex < 0)
            {
                return false;
            }

            var targetComponents = targetTransform.GetComponents(sourceComponent.GetType());
            if (sourceIndex >= targetComponents.Length)
            {
                return false;
            }

            targetComponent = targetComponents[sourceIndex];
            return true;
        }

        private static int GetComponentIndex(Component component)
        {
            var components = component.GetComponents(component.GetType());
            for (var i = 0; i < components.Length; i++)
            {
                if (ReferenceEquals(components[i], component))
                {
                    return i;
                }
            }

            return -1;
        }

        private static Dictionary<Transform, Transform> BuildTransformMap(Transform sourceRoot, Transform targetRoot)
        {
            var targetPathMap = new Dictionary<string, Transform>();
            foreach (var targetTransform in targetRoot.GetComponentsInChildren<Transform>(true))
            {
                var path = GetRelativePath(targetRoot, targetTransform);
                targetPathMap[path] = targetTransform;
            }

            var map = new Dictionary<Transform, Transform>();
            foreach (var sourceTransform in sourceRoot.GetComponentsInChildren<Transform>(true))
            {
                var path = GetRelativePath(sourceRoot, sourceTransform);
                if (targetPathMap.TryGetValue(path, out var targetTransform))
                {
                    map[sourceTransform] = targetTransform;
                }
            }

            return map;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                throw new ArgumentException("Target transform is not under the specified root.", nameof(target));
            }

            return string.Join("/", names.ToArray());
        }

        private static bool IsUnderRoot(Transform target, Transform root)
        {
            var current = target;
            while (current != null)
            {
                if (current == root)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
