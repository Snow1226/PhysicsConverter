using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteInEditMode]
public class ShapeChangerDelete : MonoBehaviour
{
    private sealed class MeshState
    {
        public MeshState(Mesh mesh)
        {
            OriginalTriangles = Enumerable.Range(0, mesh.subMeshCount)
                .Select(mesh.GetTriangles)
                .ToArray();
        }

        public int[][] OriginalTriangles { get; }

        public Dictionary<int, HashSet<int>> HiddenVerticesByOwner { get; } = new Dictionary<int, HashSet<int>>();
    }

    private static readonly Dictionary<Mesh, MeshState> MeshStates = new Dictionary<Mesh, MeshState>();

    private SkinnedMeshRenderer _renderer;
    private Mesh _sourceMesh;

    private void OnEnable()
    {
        RestoreHiddenVertices();

        var stringStart = "NaNimatedBone for ".Length;
        var stringEnd = this.transform.name.IndexOf("(");
        var meshName = this.transform.name.Substring(stringStart, stringEnd - stringStart).Trim();
        Debug.Log($"Found renderer: {meshName}");
        _renderer = this.transform.root.gameObject.transform.Find(meshName).GetComponent<SkinnedMeshRenderer>();

        if (_renderer != null)
        {
            HideWeightedVertices();
        }
    }

    private void OnDisable()
    {
        RestoreHiddenVertices();
    }

    private void HideWeightedVertices()
    {
        var sourceMesh = _renderer.sharedMesh;
        if (sourceMesh == null)
            return;

        var boneIndex = System.Array.IndexOf(_renderer.bones, transform);
        if (boneIndex < 0)
            return;

        var boneWeights = sourceMesh.boneWeights;
        if (boneWeights == null || boneWeights.Length != sourceMesh.vertexCount)
            return;

        var hiddenVertices = new HashSet<int>();
        for (var i = 0; i < boneWeights.Length; i++)
        {
            var boneWeight = boneWeights[i];
            if (HasBoneWeight(boneWeight, boneIndex))
            {
                hiddenVertices.Add(i);
            }
        }

        if (hiddenVertices.Count == 0)
            return;

        if (!MeshStates.TryGetValue(sourceMesh, out var meshState))
        {
            meshState = new MeshState(sourceMesh);
            MeshStates[sourceMesh] = meshState;
        }

        meshState.HiddenVerticesByOwner[GetInstanceID()] = hiddenVertices;
        _sourceMesh = sourceMesh;

        ApplyHiddenVertices(sourceMesh, meshState);
        _renderer.sharedMesh = sourceMesh;
    }

    private void RestoreHiddenVertices()
    {
        if (_sourceMesh == null)
            return;

        if (!MeshStates.TryGetValue(_sourceMesh, out var meshState))
        {
            _sourceMesh = null;
            return;
        }

        meshState.HiddenVerticesByOwner.Remove(GetInstanceID());

        if (meshState.HiddenVerticesByOwner.Count == 0)
        {
            RestoreOriginalTriangles(_sourceMesh, meshState);
            MeshStates.Remove(_sourceMesh);
        }
        else
        {
            ApplyHiddenVertices(_sourceMesh, meshState);
        }

        _sourceMesh = null;
    }

    private static void ApplyHiddenVertices(Mesh sourceMesh, MeshState meshState)
    {
        var mergedHiddenVertices = new HashSet<int>();
        foreach (var hiddenVertices in meshState.HiddenVerticesByOwner.Values)
        {
            mergedHiddenVertices.UnionWith(hiddenVertices);
        }

        for (var subMeshIndex = 0; subMeshIndex < meshState.OriginalTriangles.Length; subMeshIndex++)
        {
            var triangles = meshState.OriginalTriangles[subMeshIndex];
            var filteredTriangles = new List<int>(triangles.Length);
            for (var i = 0; i < triangles.Length; i += 3)
            {
                if (mergedHiddenVertices.Contains(triangles[i]) || mergedHiddenVertices.Contains(triangles[i + 1]) || mergedHiddenVertices.Contains(triangles[i + 2]))
                {
                    continue;
                }

                filteredTriangles.Add(triangles[i]);
                filteredTriangles.Add(triangles[i + 1]);
                filteredTriangles.Add(triangles[i + 2]);
            }

            sourceMesh.SetTriangles(filteredTriangles, subMeshIndex);
        }

        sourceMesh.RecalculateBounds();
    }

    private static void RestoreOriginalTriangles(Mesh sourceMesh, MeshState meshState)
    {
        for (var subMeshIndex = 0; subMeshIndex < meshState.OriginalTriangles.Length; subMeshIndex++)
        {
            sourceMesh.SetTriangles(meshState.OriginalTriangles[subMeshIndex], subMeshIndex);
        }

        sourceMesh.RecalculateBounds();
    }

    private static bool HasBoneWeight(BoneWeight boneWeight, int boneIndex)
    {
        return (boneWeight.boneIndex0 == boneIndex && boneWeight.weight0 > 0f)
            || (boneWeight.boneIndex1 == boneIndex && boneWeight.weight1 > 0f)
            || (boneWeight.boneIndex2 == boneIndex && boneWeight.weight2 > 0f)
            || (boneWeight.boneIndex3 == boneIndex && boneWeight.weight3 > 0f);
    }

}
