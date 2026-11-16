using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DinoVehicle
{
    /// <summary>
    /// Garantiza materiales compatibles con el Built-in Render Pipeline (shader Standard) en flora, rocas y fauna.
    /// - Shaders de URP/HDRP/Shader Graph o no soportados -> copia Standard con su albedo y normal (se veían rosados).
    /// - Standard con la "MaskMap" de URP en _MetallicGlossMap -> copia sin ese mapa (se veían plateados/metálicos).
    /// En el editor (fuera de Play) las copias se guardan en Assets/Dino/Materials/BIRP para que la escena las conserve.
    /// No toca los materiales originales de los paquetes.
    /// </summary>
    public static class DinoMaterialesBIRP
    {
        const string Carpeta = "Assets/Dino/Materials/BIRP";
        static readonly Dictionary<Material, Material> cache = new();

        static readonly string[] PalabrasHoja = { "leaf", "leaves", "hoja", "branch", "needle", "frond", "billboard", "grass", "fern", "bush", "foliage", "heather", "shrub", "pine" };
        static readonly string[] PalabrasTronco = { "bark", "trunk", "wood", "tronco", "rock", "stone" };

        static bool EsHoja(string nombreMaterial)
        {
            string n = nombreMaterial.ToLowerInvariant();
            foreach (var t in PalabrasTronco) if (n.Contains(t)) return false;
            foreach (var p in PalabrasHoja) if (n.Contains(p)) return true;
            return false;
        }

        /// <summary>Hojas/billboards en Standard Opaco: se ven como tarjetas o rectángulos sólidos.</summary>
        static bool HojaOpaca(Material m)
        {
            return m.shader != null && m.shader.name == "Standard" && m.HasProperty("_Mode")
                && m.GetFloat("_Mode") < 0.5f && m.mainTexture != null && EsHoja(m.name);
        }

        public static void Aplicar(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool cambio = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var c = Convertir(mats[i]);
                    if (c != mats[i]) { mats[i] = c; cambio = true; }
                }
                if (cambio) r.sharedMaterials = mats;
            }
        }

        public static bool EsIncompatible(Material m)
        {
            if (m == null) return false;
            var sh = m.shader;
            if (sh == null || !sh.isSupported) return true;
            string n = sh.name;
            return n == "Hidden/InternalErrorShader"
                || n.Contains("Universal Render Pipeline")
                || n.Contains("HDRP")
                || n.StartsWith("Shader Graphs/")
                || n.StartsWith("URP");
        }

        static bool TieneMascaraURP(Material m)
        {
            if (m == null || m.shader == null || !m.shader.name.StartsWith("Standard")) return false;
            if (!m.HasProperty("_MetallicGlossMap")) return false;
            var t = m.GetTexture("_MetallicGlossMap");
            return t != null && t.name.IndexOf("mask", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Material Convertir(Material m)
        {
            if (m == null) return null;
            if (m.name.StartsWith("BIRP_")) return m;
            if (cache.TryGetValue(m, out var c) && c != null) return c;

            Material res = m;
            bool incompatible = EsIncompatible(m);
            if (incompatible || TieneMascaraURP(m) || HojaOpaca(m)) res = CrearCopia(m, incompatible);
            cache[m] = res;
            return res;
        }

        static Material CrearCopia(Material m, bool desdeOtroShader)
        {
            string nombre = "BIRP_" + Limpiar(m.name);
#if UNITY_EDITOR
            string ruta = Carpeta + "/" + nombre + ".mat";
            if (!Application.isPlaying)
            {
                var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
                if (existente != null) return existente;
            }
#endif
            var standard = Shader.Find("Standard");
            Material n;
            if (desdeOtroShader)
            {
                n = new Material(standard) { name = nombre };
                var albedo = Primera(m, "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_AlbedoMap", "_Diffuse");
                if (albedo != null) n.mainTexture = albedo;
                Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                col.a = 1f;
                n.color = col;
                var normal = Primera(m, "_BumpMap", "_NormalMap");
                if (normal != null)
                {
                    n.SetTexture("_BumpMap", normal);
                    n.EnableKeyword("_NORMALMAP");
                }
            }
            else
            {
                n = new Material(m) { name = nombre };
                n.SetTexture("_MetallicGlossMap", null);
                n.DisableKeyword("_METALLICGLOSSMAP");
            }

            n.SetFloat("_Metallic", 0f);
            n.SetFloat("_Glossiness", 0.12f);

            if (EsHoja(m.name))
            {
                n.SetFloat("_Mode", 1f);
                n.SetOverrideTag("RenderType", "TransparentCutout");
                n.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                n.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                n.SetInt("_ZWrite", 1);
                n.EnableKeyword("_ALPHATEST_ON");
                n.DisableKeyword("_ALPHABLEND_ON");
                n.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                n.SetFloat("_Cutoff", 0.4f);
                n.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Dino/Materials", "BIRP");
                AssetDatabase.CreateAsset(n, ruta);
            }
#endif
            return n;
        }

        static Texture Primera(Material m, params string[] props)
        {
            foreach (var p in props)
            {
                if (m.HasProperty(p))
                {
                    var t = m.GetTexture(p);
                    if (t != null) return t;
                }
            }
            return null;
        }

        static string Limpiar(string s)
        {
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Replace(' ', '_');
        }
    }
}
