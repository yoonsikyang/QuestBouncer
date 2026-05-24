/// src* https://gist.github.com/andrew-raphael-lukasik/3559728d022a4c96f491924f8285e1bf
///
/// Copyright (C) 2023 Andrzej Rafa©© ¨©ukasik (also known as: Andrew Raphael Lukasik)
///
/// This program is free software: you can redistribute it and/or modify
/// it under the terms of the GNU General Public License as published by
/// the Free Software Foundation, version 3 of the License.
///
/// This program is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
/// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
/// See the GNU General Public License for details https://www.gnu.org/licenses/
///
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CultureInfo = System.Globalization.CultureInfo;
using IO = System.IO;
using NumberStyles = System.Globalization.NumberStyles;

public class ObjFileFormatVertexColorImporter : AssetPostprocessor
{
    static Dictionary<string, List<Color>> _rawColors;
    static List<int> _rawIndices;

    void OnPreprocessModel()
    {
        if (!assetPath.EndsWith(".obj")) return;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        string path = Application.dataPath + assetPath.Replace("Assets", "");
        var fs = new IO.FileStream(path, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite);
        var reader = new IO.StreamReader(fs);
        ReadColorData(reader, assetPath);
        reader.Dispose();
        fs.Dispose();


        double totalSeconds = new System.TimeSpan(stopwatch.ElapsedTicks).TotalSeconds;
        if (totalSeconds > 0.1) Debug.LogWarning($"{GetType().Name}::{nameof(OnPreprocessModel)}() took {totalSeconds:0.00} seconds, `{assetPath}` asset");
    }

    void ReadColorData(IO.StreamReader reader, string path)
    {
        List<Color> current_rawColor = new List<Color>();
        Dictionary<int, int> current_rawNormalHash = new Dictionary<int, int>();
        Dictionary<int, int> current_rawUvHash = new Dictionary<int, int>();
        HashSet<Vector3Int> current_geometrySharedVerticesDetector = new HashSet<Vector3Int>();
        _rawIndices = new List<int>();
        _rawColors = new Dictionary<string, List<Color>>() { { "default", current_rawColor } };
        Dictionary<string, Dictionary<int, int>> rawNormalHashes = new Dictionary<string, Dictionary<int, int>>() { { "default", current_rawNormalHash } };
        Dictionary<string, Dictionary<int, int>> rawUvHashes = new Dictionary<string, Dictionary<int, int>>() { { "default", current_rawUvHash } };
        Dictionary<string, HashSet<Vector3Int>> sharedVertexDetector = new Dictionary<string, HashSet<Vector3Int>>() { { "default", current_geometrySharedVerticesDetector } };
        int numVertexColorVertices = 0;
        int v_index = 1, vn_index = 1, vt_index = 1;
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            // string[] words = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            string[] words = line.Split(' ');
            byte wordsLen = (byte)words.Length;
            if (wordsLen != 0)
                switch (words[0])
                {
                    // parse vertex data (vertex color only)
                    case "v":
                        {
                            float r = float.Parse(words[4], NumberStyles.Number, CultureInfo.InvariantCulture);
                            float g = float.Parse(words[5], NumberStyles.Number, CultureInfo.InvariantCulture);
                            float b = float.Parse(words[6], NumberStyles.Number, CultureInfo.InvariantCulture);
                            current_rawColor.Add(new Color(r, g, b));
                            v_index++;
                            numVertexColorVertices++;

                            break;
                        }

                    // parse vertex normal data
                    case "vn":
                        {
                            int hash = 17;
                            unchecked
                            {
                                hash = hash * 23 + words[1].GetHashCode();
                                hash = hash * 23 + words[2].GetHashCode();
                                hash = hash * 23 + words[3].GetHashCode();
                            }
                            //Debug.Log($"vn #{vn_index} ( {words[1]} , {words[2]} , {words[3]} ) added as #{hash}");
                            current_rawNormalHash.Add(vn_index++, hash);

                            break;
                        }

                    // parse texture UV data
                    case "vt":
                        {
                            int hash = 17;
                            unchecked
                            {
                                hash = hash * 23 + words[1].GetHashCode();
                                hash = hash * 23 + words[2].GetHashCode();
                            }
                            //Debug.Log($"vt #{vn_index} ( {words[1]} , {words[2]} ) added as #{hash}");
                            current_rawUvHash.Add(vt_index++, hash);

                            break;
                        }

                    // parse face data
                    case "f":
                        {
                            if (words[1].Contains('/'))// format: pos/uv/normal indices
                            {
                                for (int i = 1; i < wordsLen; i++)
                                {
                                    string[] v_vt_vn = words[i].Split('/');
                                    if (v_vt_vn.Length == 3)
                                    {
                                        string v = v_vt_vn[0];// position index
                                        string vt = v_vt_vn[1];// uv index
                                        string vn = v_vt_vn[2];// normal index

                                        int vertexIndex = int.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);

                                        int normalHash;
                                        if (vn.Length != 0)
                                        {
                                            int normalIndex = int.Parse(vn, NumberStyles.Number, CultureInfo.InvariantCulture);

                                            if (!current_rawNormalHash.TryGetValue(normalIndex, out normalHash))
                                            {
                                                normalHash = -1;
                                                Debug.LogError($"Face definition points to normal data that is missing from this OBJ file. Line: {line}");
                                            }
                                        }
                                        else normalHash = -1;// vertex with no normal data

                                        int uvHash;
                                        if (vt.Length != 0)
                                        {
                                            int uvIndex = int.Parse(vt, NumberStyles.Number, CultureInfo.InvariantCulture);

                                            if (!current_rawUvHash.TryGetValue(uvIndex, out uvHash))
                                            {
                                                normalHash = -1;
                                                Debug.LogError($"Face definition points to uv data that is missing from this OBJ file. Line: {line}");
                                            }
                                        }
                                        else uvHash = -1;// vertex with no uv data

                                        if (current_geometrySharedVerticesDetector.Add(new Vector3Int(vertexIndex, uvHash, normalHash)))
                                        {
                                            // Debug.Log($"new vertex index added: ( {vertexIndex} #{uvHash} , #{normalHash} )");
                                            _rawIndices.Add(vertexIndex);
                                        }
                                        // else Debug.Log($"ignoring vertex as shared: ( {vertexIndex} , #{normalHash} )");
                                    }
                                }
                            }
                            else if (wordsLen >= 4) // face indices only
                            {
                                for (int i = 1; i < wordsLen; i++)
                                {
                                    string v = words[i];
                                    int vertexIndex = int.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);
                                    _rawIndices.Add(vertexIndex);
                                }
                            }

                            break;
                        }

                    // parse g
                    case "g":
                        {
                            string meshName = line.Substring(1).Trim(' ').Replace(' ', '_');

                            current_rawColor = new List<Color>();
                            current_rawNormalHash = new Dictionary<int, int>();
                            current_rawUvHash = new Dictionary<int, int>();
                            current_geometrySharedVerticesDetector = new HashSet<Vector3Int>();

                            _rawColors.Add(meshName, current_rawColor);
                            rawNormalHashes.Add(meshName, current_rawNormalHash);
                            rawUvHashes.Add(meshName, current_rawUvHash);
                            sharedVertexDetector.Add(meshName, current_geometrySharedVerticesDetector);

                            break;
                        }
                }
        }
        reader.Dispose();

        if (numVertexColorVertices != 0)// was any vertex color data found ?
        {
            // change importer settings
            var importer = (ModelImporter)assetImporter;
            importer.optimizeMeshVertices = false;
            importer.optimizeMeshPolygons = false;
            importer.weldVertices = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }

    void OnPostprocessModel(GameObject gameObject)
    {
        if (!assetPath.EndsWith(".obj"))
        {
            Debug.Log("TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT");
            return;

        }

        if (_rawColors != null)
        {
            Debug.Assert(_rawColors.Count != 0, $"{nameof(_rawColors)}.Count is zero");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                Mesh mesh = mf.sharedMesh;
                List<Color> rawColors = _rawColors[mesh.name];

                int meshVertexCount = mesh.vertexCount;
                Color[] finalColors = new Color[meshVertexCount];
                for (int i = 0; i < meshVertexCount; i++)
                {
                    int rawIndex = _rawIndices[i] - 1;// "-1" because raw OBJ face indices are in 1..N and not 0..N space
                    finalColors[i] = rawColors[rawIndex];
                }

                if (finalColors.Length != meshVertexCount) Debug.LogError($"Invalid color data length {finalColors.Length} - while mesh \"{mesh.name}\" expects {meshVertexCount} entries", gameObject);
                mesh.SetColors(finalColors);

                foreach (var vec in mesh.vertices)
                {
                    Debug.Log(vec);
                }
            }

            double totalSeconds = new System.TimeSpan(stopwatch.ElapsedTicks).TotalSeconds;
            if (totalSeconds > 0.1) Debug.LogWarning($"{GetType().Name}::{nameof(OnPostprocessModel)}() took {totalSeconds:0.00} seconds, `{assetPath}` asset", gameObject);

            // release static refs
            _rawColors = null;
            _rawIndices = null;
        }
    }

}
#endif