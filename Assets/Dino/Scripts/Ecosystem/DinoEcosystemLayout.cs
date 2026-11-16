using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DinoVehicle
{
    /// <summary>
    /// Distribución del mundo DinoWest compartida por el constructor del editor
    /// (DinoRealisticEnvironmentSetup) y el generador en Play (DinoEcosystemRuntime):
    /// - Pista central plana y praderas planas amplias con transición smoothstep a colinas.
    /// - Bosques naturales (~90-100 árboles) alrededor de praderas y bordes, sin invadir la pista ni las praderas.
    /// - Manadas de Estegosaurios, Paquicefalosaurios y Velociraptores con todas las variantes de color.
    /// Todo se apoya en el suelo real con Terrain.SampleHeight.
    /// </summary>
    public static class DinoEcosystemLayout
    {
        public const float TamTerreno = 360f;
        public const float AlturaTerreno = 65f;

        // Corredor central plano (la pista de tierra va por x = 0)
        public const float PistaMedioAncho = 28f;
        public const float PistaZMin = -60f;
        public const float PistaZMax = 165f;
        /// <summary>Metros de transición smoothstep entre lo plano y las colinas.</summary>
        public const float Transicion = 34f;
        /// <summary>Medio lado útil (deja margen al borde del terreno).</summary>
        public const float Borde = TamTerreno * 0.5f - 6f;

        public struct Zona
        {
            public string Nombre;
            public Vector2 Centro;
            public float Radio;
            public Zona(string n, float x, float z, float r) { Nombre = n; Centro = new Vector2(x, z); Radio = r; }
        }

        public static readonly Zona[] Praderas =
        {
            new Zona("Pradera_Estegosaurios", -95f, 40f, 32f),
            new Zona("Pradera_Paquicefalos", 95f, 70f, 30f),
            new Zona("Llanura_Raptores", 95f, -80f, 28f),
            new Zona("Pradera_Norte", -82f, 135f, 26f),
            new Zona("Pradera_Sur", -92f, -95f, 26f),
        };

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>1 = totalmente plano (pista o pradera), 0 = colina libre.</summary>
        public static float Planitud(float x, float z)
        {
            float dx = Mathf.Abs(x) - PistaMedioAncho;
            float dz = Mathf.Max(z - PistaZMax, PistaZMin - z);
            float dPista = Mathf.Max(dx, dz);
            float plan = 1f - Smooth(0f, Transicion, dPista);

            foreach (var p in Praderas)
            {
                float d = Vector2.Distance(new Vector2(x, z), p.Centro) - p.Radio;
                plan = Mathf.Max(plan, 1f - Smooth(0f, Transicion, d));
            }
            return plan;
        }

        /// <summary>Altura normalizada (0..1 de AlturaTerreno) en coordenadas de mundo.</summary>
        public static float AlturaNormalizada(float x, float z)
        {
            float plan = Planitud(x, z);
            if (plan >= 0.999f) return 0f;

            float c1 = Mathf.Sin(x * 0.031f) * Mathf.Cos(z * 0.025f) * 0.5f + 0.5f;
            float c2 = Mathf.Sin(x * 0.016f + z * 0.014f) * 0.5f + 0.5f;
            float c3 = Mathf.Cos(x * 0.008f - z * 0.01f) * 0.5f + 0.5f;
            float detalle = Mathf.Sin(x * 0.07f + 1.3f) * Mathf.Cos(z * 0.08f - 0.7f) * 0.5f + 0.5f;
            float colinas = (c1 * 0.45f + c2 * 0.35f + c3 * 0.20f) * 0.85f + detalle * 0.15f;

            // Suaves cerca del centro, más altas hacia el borde del mapa (hasta ~14 m).
            float r = new Vector2(x, z - 50f).magnitude;
            float amplitud = Mathf.Lerp(0.05f, 0.22f, Smooth(70f, 180f, r));

            return colinas * amplitud * (1f - plan);
        }

        public static float AlturaSuelo(Terrain t, float x, float z)
        {
            if (t == null) return 0f;
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        // ------------------------------------------------------------------
        // Bosques
        // ------------------------------------------------------------------
        public enum TipoArbol { Caducifolio, Conifera, Pino, Gigante }

        public struct PuntoArbol
        {
            public Vector2 Pos;
            public TipoArbol Tipo;
            public float Altura;
            public float Rot;
        }

        /// <summary>
        /// Bosque denso determinista (misma semilla = mismo bosque en editor y en Play):
        /// - Rejilla con jitter cada ~6.3 m en todo lo que no es pista ni pradera, con claros por ruido.
        /// - Más denso hacia el borde del mapa (muro de bosque que oculta el final del terreno).
        /// - Zonas de especie por ruido (coníferas, pinos, caducifolios) para que parezcan bosques naturales.
        /// - Árboles gigantes (36-60 m) en el borde de las praderas y repartidos dentro del bosque.
        /// - Alameda junto a la pista.
        /// </summary>
        public static List<PuntoArbol> GenerarArboles(int semilla = 1977)
        {
            var rnd = new System.Random(semilla);
            float R() => (float)rnd.NextDouble();
            var lista = new List<PuntoArbol>();
            // Rejilla espacial para comprobar la separación rápido.
            var celdas = new Dictionary<(int, int), List<int>>();
            const float tamCelda = 10f;

            bool Libre(Vector2 p, float sep)
            {
                foreach (var c in PosComida) if ((c - p).sqrMagnitude < 81f) return false;
                int cx = Mathf.FloorToInt(p.x / tamCelda), cz = Mathf.FloorToInt(p.y / tamCelda);
                int rango = Mathf.CeilToInt(Mathf.Max(sep, 13f) / tamCelda);
                for (int i = cx - rango; i <= cx + rango; i++)
                {
                    for (int j = cz - rango; j <= cz + rango; j++)
                    {
                        if (!celdas.TryGetValue((i, j), out var l)) continue;
                        foreach (int k in l)
                        {
                            // Los gigantes necesitan más espacio alrededor de su copa.
                            float s2 = lista[k].Tipo == TipoArbol.Gigante ? Mathf.Max(sep, 13f) : sep;
                            if ((lista[k].Pos - p).sqrMagnitude < s2 * s2) return false;
                        }
                    }
                }
                return true;
            }

            void Agregar(PuntoArbol a)
            {
                var key = (Mathf.FloorToInt(a.Pos.x / tamCelda), Mathf.FloorToInt(a.Pos.y / tamCelda));
                if (!celdas.TryGetValue(key, out var l)) celdas[key] = l = new List<int>();
                l.Add(lista.Count);
                lista.Add(a);
            }

            // 1. Gigantes primero: en el borde de cada pradera (como hitos) y repartidos por el bosque.
            foreach (var z in Praderas)
            {
                int n = 0;
                for (int intento = 0; intento < 80 && n < 3; intento++)
                {
                    float ang = R() * Mathf.PI * 2f;
                    var p = z.Centro + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (z.Radio + 9f + R() * 6f);
                    if (!DentroDelMapa(p) || Planitud(p.x, p.y) > 0.75f || EnPista(p, 6f) || !Libre(p, 22f)) continue;
                    Agregar(NuevoGigante(p, R));
                    n++;
                }
            }
            for (int intento = 0, n = 0; intento < 1500 && n < 34; intento++)
            {
                var p = new Vector2((R() * 2f - 1f) * Borde, (R() * 2f - 1f) * Borde);
                if (Planitud(p.x, p.y) > 0.2f || !Libre(p, 26f)) continue;
                Agregar(NuevoGigante(p, R));
                n++;
            }

            // 2. Bosque denso en rejilla con jitter.
            const float paso = 6.3f;
            for (float x = -Borde; x <= Borde; x += paso)
            {
                for (float zz = -Borde; zz <= Borde; zz += paso)
                {
                    var p = new Vector2(x + (R() - 0.5f) * paso * 0.8f, zz + (R() - 0.5f) * paso * 0.8f);
                    if (!DentroDelMapa(p)) continue;
                    float plan = Planitud(p.x, p.y);
                    if (plan > 0.25f) continue; // praderas y pista despejadas

                    // Claros naturales por ruido; cerca del borde casi no hay claros.
                    float borde = Smooth(Borde - 45f, Borde - 5f, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
                    float claro = Mathf.PerlinNoise(p.x * 0.035f + 40f, p.y * 0.035f + 13f);
                    float prob = Mathf.Lerp(0.82f, 1f, borde) * Mathf.Lerp(Smooth(0.14f, 0.34f, claro), 1f, borde);
                    // En la transición hacia lo plano el bosque se aclara.
                    prob *= 1f - plan * 2.2f;
                    if (R() > prob) continue;
                    if (!Libre(p, 4.8f)) continue;

                    float bioma = Mathf.PerlinNoise(p.x * 0.012f + 7f, p.y * 0.012f + 91f);
                    TipoArbol t = bioma < 0.4f ? TipoArbol.Conifera : (bioma < 0.62f ? TipoArbol.Pino : TipoArbol.Caducifolio);
                    if (R() < 0.15f) t = (TipoArbol)rnd.Next(0, 3); // mezcla en los límites
                    Agregar(Nuevo(p, t, R));
                }
            }

            // 3. Alameda junto a la pista (zona plana, fuera de la tierra, rampas y cajas).
            for (float z = PistaZMin + 10f; z < PistaZMax - 5f; z += 13f)
            {
                for (int lado = -1; lado <= 1; lado += 2)
                {
                    if (R() < 0.2f) continue;
                    var p = new Vector2(lado * (17f + R() * 8f), z + (R() - 0.5f) * 6f);
                    if (!Libre(p, 8f)) continue;
                    Agregar(Nuevo(p, R() < 0.6f ? TipoArbol.Caducifolio : TipoArbol.Pino, R));
                }
            }
            return lista;
        }

        static bool DentroDelMapa(Vector2 p) => Mathf.Abs(p.x) <= Borde && Mathf.Abs(p.y) <= Borde;

        static bool EnPista(Vector2 p, float margen)
            => Mathf.Abs(p.x) < PistaMedioAncho + margen && p.y > PistaZMin - margen && p.y < PistaZMax + margen;

        static PuntoArbol Nuevo(Vector2 p, TipoArbol t, Func<float> R)
        {
            float alt = t switch
            {
                TipoArbol.Conifera => Mathf.Lerp(15f, 26f, R()),
                TipoArbol.Pino => Mathf.Lerp(14f, 24f, R()),
                _ => Mathf.Lerp(10f, 20f, R()),
            };
            return new PuntoArbol { Pos = p, Tipo = t, Altura = alt, Rot = R() * 360f };
        }

        static PuntoArbol NuevoGigante(Vector2 p, Func<float> R)
            => new PuntoArbol { Pos = p, Tipo = TipoArbol.Gigante, Altura = Mathf.Lerp(36f, 60f, R()), Rot = R() * 360f };

        public static readonly Vector2[] PosComida =
        {
            new Vector2(-21f, 45f), new Vector2(-23f, 62f), new Vector2(21f, 50f), new Vector2(24f, 66f)
        };

        public static readonly string[] PrefabsCaducifolios =
        {
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_3.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_4.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_5.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_8.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_10.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_11.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_13.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_14.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_17.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_18.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_19.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_22.prefab",
        };

        public static readonly string[] PrefabsConiferas =
        {
            "Assets/Forst/Conifers [BOTD]/Render Pipeline Support/BIRP/Prefabs/PF Conifer Tall BOTD.prefab",
            "Assets/Forst/Conifers [BOTD]/Render Pipeline Support/BIRP/Prefabs/PF Conifer Medium BOTD.prefab",
            "Assets/Forst/Conifers [BOTD]/Render Pipeline Support/BIRP/Prefabs/PF Conifer Small BOTD.prefab",
        };

        // Los Pine_X.prefab tienen la malla desplazada cientos de metros del pivote; el .st está centrado.
        public static readonly string[] PrefabsPinos =
        {
            "Assets/TerrainDemoScene_URP/Prefabs/Trees/Pines/Pine_A/Pine_A.st",
            "Assets/TerrainDemoScene_URP/Prefabs/Trees/Pines/Pine_B/Pine_B.st",
            "Assets/TerrainDemoScene_URP/Prefabs/Trees/Pines/Pine_C/Pine_C.st",
            "Assets/TerrainDemoScene_URP/Prefabs/Trees/Pines/Pine_D/Pine_D.st",
        };

        /// <summary>Copas enormes y anchas: con 36-60 m de alto son los árboles "masivos".</summary>
        public static readonly string[] PrefabsGigantes =
        {
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_20.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_21.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_23.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_24.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_25.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_26.prefab",
            "Assets/Trees Collection Asset PBR/Prefabs/Tree_27.prefab",
        };

        public static string[] PoolDe(TipoArbol t) => t switch
        {
            TipoArbol.Gigante => PrefabsGigantes,
            TipoArbol.Conifera => PrefabsConiferas,
            TipoArbol.Pino => PrefabsPinos,
            _ => PrefabsCaducifolios,
        };

        // ------------------------------------------------------------------
        // Fauna
        // ------------------------------------------------------------------
        public enum Especie { Estegosaurio, Paquicefalo, Raptor, Anquilosaurio }

        public struct Manada
        {
            public string Nombre; public Especie Especie; public Vector2 Centro; public int Cantidad; public float Territorio;
            public Manada(string n, Especie e, float x, float z, int c, float t) { Nombre = n; Especie = e; Centro = new Vector2(x, z); Cantidad = c; Territorio = t; }
        }

        public static readonly Manada[] Manadas =
        {
            new Manada("Manada_Estegosaurios", Especie.Estegosaurio, -95f, 40f, 6, 20f),
            new Manada("Manada_Estegosaurios_Norte", Especie.Estegosaurio, -82f, 135f, 4, 14f),
            new Manada("Manada_Paquicefalos", Especie.Paquicefalo, 95f, 70f, 5, 18f),
            new Manada("Manada_Paquicefalos_Sur", Especie.Paquicefalo, -92f, -95f, 4, 14f),
            new Manada("Manada_Paquicefalos_Pista", Especie.Paquicefalo, -20f, 120f, 3, 8f),
            new Manada("Manada_Raptores", Especie.Raptor, 95f, -80f, 5, 16f),
            new Manada("Manada_Raptores_Pista", Especie.Raptor, 18f, 150f, 3, 10f),
            new Manada("Anquilosaurios_Guardianes", Especie.Anquilosaurio, -20f, -30f, 2, 0f),
        };

        public static readonly string[] VariantesEstegosaurio =
        {
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Green.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Blue.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Green2.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Blue2.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Grey.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Purple.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Prefabs/LODG/Stegasaurus_Yellow.prefab",
        };

        public static readonly string[] VariantesPaquicefalo =
        {
            "Assets/FerociousIndustries/PBRDinosaurs/PBRPachycephalasaurus/Prefabs/PBR/Pachycephalasaurus_Green.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRPachycephalasaurus/Prefabs/PBR/Pachycephalasaurus_SandMale.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRPachycephalasaurus/Prefabs/PBR/Pachycephalasaurus_SandFemale.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRPachycephalasaurus/Prefabs/PBR/Pachycephalasaurus_Blue.prefab",
            "Assets/FerociousIndustries/PBRDinosaurs/PBRPachycephalasaurus/Prefabs/PBR/Pachycephalasaurus_Red.prefab",
        };

        public static readonly string[] VariantesRaptor =
        {
            "Assets/PBRVelociraptor/Prefabs/PBR/15K/PBR_Velociraptor_Sand_Male.prefab",
            "Assets/PBRVelociraptor/Prefabs/PBR/15K/PBR_Velociraptor_Sand_Female.prefab",
            "Assets/PBRVelociraptor/Prefabs/PBR/15K/PBR_Velociraptor_Orange.prefab",
            "Assets/PBRVelociraptor/Prefabs/PBR/15K/PBR_Velociraptor_Green.prefab",
            "Assets/PBRVelociraptor/Prefabs/PBR/15K/PBR_Velociraptor_Blue.prefab",
        };

        public const string PrefabAnquilosaurio = "Assets/Dino/Models/Fauna/Ankylosaurus/source/Dinosaur.glb";

        /// <summary>
        /// Velocidad natural (m/s a escala 1) de los clips Walk/Run, medida en el editor con la velocidad
        /// de la pata apoyada en los clips in-place y el desplazamiento de las variantes _RM.
        /// La IA pone animator.speed = velocidad real / esta velocidad, así la pata apoyada no resbala.
        /// </summary>
        public static void VelocidadesPorDefecto(Especie e, out float caminar, out float correr)
        {
            switch (e)
            {
                case Especie.Estegosaurio: caminar = 2.2f; correr = 3.6f; break;
                case Especie.Paquicefalo: caminar = 0.92f; correr = 3.1f; break;
                case Especie.Raptor: caminar = 2.35f; correr = 3.4f; break;
                default: caminar = 1f; correr = 3f; break;
            }
        }

        /// <summary>
        /// Instancia todas las manadas.
        /// cargar: ruta -> prefab. instanciar: (prefab, padre) -> instancia.
        /// controller: especie -> AnimatorController real (null = no cambiar).
        /// velocidades: ajusta velocidadClipCaminar/Correr (medidas en el editor); puede ser null.
        /// </summary>
        public static int PoblarFauna(Transform padre, Terrain terreno,
            Func<string, GameObject> cargar,
            Func<GameObject, Transform, GameObject> instanciar,
            Func<Especie, RuntimeAnimatorController> controller,
            Action<DinoAIBase, Especie, string> velocidades,
            int semilla = 65)
        {
            var estadoPrevio = Random.state;
            Random.InitState(semilla);
            int total = 0;

            foreach (var m in Manadas)
            {
                var grupo = new GameObject(m.Nombre).transform;
                grupo.SetParent(padre, false);

                string[] variantes = m.Especie switch
                {
                    Especie.Estegosaurio => VariantesEstegosaurio,
                    Especie.Paquicefalo => VariantesPaquicefalo,
                    Especie.Raptor => VariantesRaptor,
                    _ => new[] { PrefabAnquilosaurio },
                };

                float separacion = m.Especie switch
                {
                    Especie.Estegosaurio => 7.5f,
                    Especie.Paquicefalo => 4.5f,
                    Especie.Raptor => 4f,
                    _ => 9f,
                };

                float yawLider = Random.Range(0f, 360f);
                Quaternion rumbo = Quaternion.Euler(0f, yawLider, 0f);
                DinoAIHerbivore liderH = null;
                DinoAIRaptor liderR = null;
                int offsetVariante = Random.Range(0, variantes.Length);

                for (int i = 0; i < m.Cantidad; i++)
                {
                    string ruta = variantes[(i + offsetVariante) % variantes.Length];
                    var prefab = cargar(ruta);
                    if (prefab == null) continue;

                    // Formación en espiral de Fermat: el líder delante y el resto detrás y a los lados.
                    Vector3 offset = Vector3.zero;
                    if (i > 0)
                    {
                        float a = i * 137.5f * Mathf.Deg2Rad;
                        float r = separacion * Mathf.Sqrt(i);
                        offset = new Vector3(Mathf.Sin(a) * r, 0f, -Mathf.Abs(Mathf.Cos(a)) * r - separacion * 0.5f);
                    }
                    Vector3 pos = new Vector3(m.Centro.x, 0f, m.Centro.y) + rumbo * offset;
                    pos.y = AlturaSuelo(terreno, pos.x, pos.z);

                    var go = instanciar(prefab, grupo);
                    go.name = $"{EspecieNombre(m.Especie)}_{(i == 0 ? "Lider" : i.ToString())}_{prefab.name}";
                    go.transform.position = pos;

                    float escala = m.Especie switch
                    {
                        Especie.Estegosaurio => i == 0 ? 1.15f : Random.Range(0.8f, 1.05f),
                        Especie.Paquicefalo => i == 0 ? 1.2f : Random.Range(0.95f, 1.15f),
                        Especie.Raptor => i == 0 ? 1.05f : Random.Range(0.88f, 1.0f),
                        _ => 5.5f + i * 0.4f,
                    };
                    go.transform.localScale = Vector3.one * escala;

                    bool raptor = m.Especie == Especie.Raptor;
                    // El Velociraptor PBR mira hacia -Z: se gira 180° para que su cabeza vaya hacia el rumbo.
                    go.transform.rotation = Quaternion.Euler(0f, yawLider + (raptor ? 180f : 0f) + Random.Range(-15f, 15f), 0f);

                    var anim = go.GetComponentInChildren<Animator>();
                    if (anim != null)
                    {
                        var ctrl = controller?.Invoke(m.Especie);
                        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
                        anim.applyRootMotion = false;
                    }

                    DinoMaterialesBIRP.Aplicar(go);
                    AsegurarCuerpoFisico(go, m.Especie);

                    VelocidadesPorDefecto(m.Especie, out float vC, out float vR);
                    if (raptor)
                    {
                        if (!go.TryGetComponent<DinoAIRaptor>(out var ai)) ai = go.AddComponent<DinoAIRaptor>();
                        ai.frenteInvertido = true;
                        ai.velocidadClipCaminar = vC;
                        ai.velocidadClipCorrer = vR;
                        ai.aceleracion = 7f;
                        ai.giroGradosSeg = 140f;
                        ai.radioPatrulla = m.Territorio;
                        ai.centroTerritorio = new Vector3(m.Centro.x, 0f, m.Centro.y);
                        ai.centroAsignado = true;
                        ai.indiceManada = i;
                        if (i == 0) liderR = ai;
                        else { ai.lider = liderR; ai.offsetManada = offset; }
                        velocidades?.Invoke(ai, m.Especie, ruta);
                    }
                    else
                    {
                        if (!go.TryGetComponent<DinoAIHerbivore>(out var ai)) ai = go.AddComponent<DinoAIHerbivore>();
                        ai.velocidadClipCaminar = vC;
                        ai.velocidadClipCorrer = vR;
                        ai.aceleracion = m.Especie == Especie.Estegosaurio ? 2.2f : 4f;
                        ai.giroGradosSeg = m.Especie == Especie.Estegosaurio ? 45f : 80f;
                        ai.territoryRadius = m.Territorio;
                        ai.centroTerritorio = new Vector3(m.Centro.x, 0f, m.Centro.y);
                        ai.centroAsignado = true;
                        ai.estatico = m.Especie == Especie.Anquilosaurio; // sin clips: vigila sin desplazarse
                        if (i == 0 || ai.estatico) liderH = ai.estatico ? null : ai;
                        else { ai.lider = liderH; ai.offsetManada = offset; }
                        velocidades?.Invoke(ai, m.Especie, ruta);
                    }
                    total++;
                }
            }

            Random.state = estadoPrevio;
            return total;
        }

        static string EspecieNombre(Especie e) => e switch
        {
            Especie.Estegosaurio => "Estegosaurio",
            Especie.Paquicefalo => "Paquicefalo",
            Especie.Raptor => "Velociraptor",
            _ => "Anquilosaurio",
        };

        /// <summary>Cápsula de colisión en el tronco del animal + Rigidbody cinemático (se mueve por transform).</summary>
        static void AsegurarCuerpoFisico(GameObject go, Especie e)
        {
            if (!go.TryGetComponent<Collider>(out _))
            {
                if (e == Especie.Anquilosaurio)
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.size = new Vector3(0.6f, 0.45f, 0.95f);
                    bc.center = new Vector3(0f, 0.25f, 0f);
                }
                else
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.direction = 2;
                    switch (e)
                    {
                        case Especie.Estegosaurio: cc.radius = 1.2f; cc.height = 7f; cc.center = new Vector3(0f, 1.9f, -1.2f); break;
                        case Especie.Paquicefalo: cc.radius = 0.6f; cc.height = 3.6f; cc.center = new Vector3(0f, 1.1f, -0.3f); break;
                        default: cc.radius = 0.45f; cc.height = 3.2f; cc.center = new Vector3(0f, 0.85f, 0.3f); break;
                    }
                }
            }

            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // ------------------------------------------------------------------
        // Árboles
        // ------------------------------------------------------------------
        public static int PoblarArboles(Transform padre, Transform padreComida, Terrain terreno,
            Func<string, GameObject> cargar,
            Func<GameObject, Transform, GameObject> instanciar)
        {
            var pools = new Dictionary<TipoArbol, List<GameObject>>();
            foreach (TipoArbol t in Enum.GetValues(typeof(TipoArbol)))
            {
                var l = new List<GameObject>();
                foreach (var ruta in PoolDe(t)) { var p = cargar(ruta); if (p != null) l.Add(p); }
                pools[t] = l;
            }
            List<GameObject> Pool(TipoArbol t)
            {
                if (pools[t].Count > 0) return pools[t];
                foreach (var kv in pools) if (kv.Value.Count > 0) return kv.Value;
                return null;
            }

            int n = 0;
            var puntos = GenerarArboles();
            for (int i = 0; i < puntos.Count; i++)
            {
                var pt = puntos[i];
                var pool = Pool(pt.Tipo);
                if (pool == null) break;
                var prefab = pool[(int)(pt.Rot * 13f) % pool.Count];
                var arbol = ColocarArbol(prefab, padre, terreno, pt.Pos, pt.Rot, pt.Altura, instanciar, pt.Tipo == TipoArbol.Gigante ? 1.4f : 1f);
                arbol.name = $"Arbol_Bosque_{i + 1:000}_{prefab.name}";
                n++;
            }

            // Árboles de comida (DinoFoodTree) en la zona plana junto a la pista.
            var comida = Pool(TipoArbol.Caducifolio);
            if (comida != null && padreComida != null)
            {
                for (int k = 0; k < PosComida.Length; k++)
                {
                    var prefab = comida[(k * 3) % comida.Count];
                    var arbol = ColocarArbol(prefab, padreComida, terreno, PosComida[k], k * 77f, 12f, instanciar);
                    arbol.name = $"Arbol_Comida_Oasis_{k + 1}";
                    if (arbol.GetComponent<DinoFoodTree>() == null) arbol.AddComponent<DinoFoodTree>();
                }
            }
            return n;
        }

        static GameObject ColocarArbol(GameObject prefab, Transform padre, Terrain terreno, Vector2 p, float rot, float altura,
            Func<GameObject, Transform, GameObject> instanciar, float ancho = 1f)
        {
            var arbol = instanciar(prefab, padre);
            arbol.transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.y), Quaternion.Euler(0f, rot, 0f));
            arbol.transform.localScale = Vector3.one;

            // Escala por altura real: los prefabs van de 5 m a 67 m.
            var b = LimitesRender(arbol);
            float alto = b.size.y;
            float escala = alto > 0.5f ? Mathf.Clamp(altura / alto, 0.15f, 3f) : 1f;
            // "ancho" > 1 engorda tronco y copa (árboles gigantes masivos).
            arbol.transform.localScale = new Vector3(escala * ancho, escala, escala * ancho);

            // Apoyado en el suelo real: se usa la altura mínima de las cuatro esquinas del tronco
            // para que en pendiente no quede ninguna raíz al aire.
            float y = Mathf.Min(
                AlturaSuelo(terreno, p.x + 0.6f, p.y), AlturaSuelo(terreno, p.x - 0.6f, p.y),
                AlturaSuelo(terreno, p.x, p.y + 0.6f), AlturaSuelo(terreno, p.x, p.y - 0.6f));
            arbol.transform.position = new Vector3(p.x, y - 0.15f, p.y);

            DinoMaterialesBIRP.Aplicar(arbol);
            AsegurarColliderTronco(arbol, escala);
            return arbol;
        }

        /// <summary>
        /// Tronco sólido para TreeImpactResponse (que en Start lo reajusta a la malla visible y añade TreeReaction).
        /// </summary>
        static void AsegurarColliderTronco(GameObject arbol, float escala)
        {
            foreach (var c in arbol.GetComponentsInChildren<Collider>(true))
            {
                if (!c.isTrigger) return;
            }
            var cc = arbol.AddComponent<CapsuleCollider>();
            float s = Mathf.Max(0.01f, escala);
            cc.radius = 0.6f / s;
            cc.height = 8f / s;
            cc.center = new Vector3(0f, 4f / s, 0f);
        }

        public static Bounds LimitesRender(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool init = false;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!init) { b = r.bounds; init = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ------------------------------------------------------------------
        // Rocas y flores
        // ------------------------------------------------------------------
        public static readonly string[] PrefabsRocas =
        {
            "Assets/TerrainDemoScene_URP/Prefabs/Rocks/Rock_A_01.prefab",
            "Assets/TerrainDemoScene_URP/Prefabs/Rocks/Rock_A_02.prefab",
            "Assets/TerrainDemoScene_URP/Prefabs/Rocks/Rock_B_01.prefab",
            "Assets/TerrainDemoScene_URP/Prefabs/Rocks/Rock_C_02.prefab",
            "Assets/TerrainDemoScene_URP/Prefabs/Rocks/Rock_Overgrown_B.prefab",
        };

        public const string PrefabFlor = "Assets/Dino/Models/Flora/CactusFlowers_2.fbx";
        public const string PrefabArbusto = "Assets/TerrainDemoScene_URP/Prefabs/Details/Bush_Red.prefab";

        public static int PoblarRocasYFlora(Transform padreRocas, Transform padreFlora, Terrain terreno,
            Func<string, GameObject> cargar,
            Func<GameObject, Transform, GameObject> instanciar,
            int semilla = 31)
        {
            var rnd = new System.Random(semilla);
            float R() => (float)rnd.NextDouble();
            int n = 0;

            var rocas = new List<GameObject>();
            foreach (var r in PrefabsRocas) { var p = cargar(r); if (p != null) rocas.Add(p); }

            // Rocas en las laderas que bordean praderas y pista.
            if (rocas.Count > 0)
            {
                int puestas = 0;
                for (int i = 0; i < 600 && puestas < 34; i++)
                {
                    var p = new Vector2((R() * 2f - 1f) * Borde, (R() * 2f - 1f) * Borde);
                    float plan = Planitud(p.x, p.y);
                    if (plan < 0.03f || plan > 0.55f) continue;
                    var prefab = rocas[puestas % rocas.Count];
                    var roca = instanciar(prefab, padreRocas);
                    roca.name = $"Roca_{puestas + 1:00}_{prefab.name}";
                    roca.transform.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
                    roca.transform.localScale = Vector3.one;
                    float tam = LimitesRender(roca).size.magnitude;
                    float objetivo = Mathf.Lerp(1.5f, 5f, R());
                    roca.transform.localScale = Vector3.one * (tam > 0.1f ? objetivo / tam : 1f);
                    roca.transform.position = new Vector3(p.x, AlturaSuelo(terreno, p.x, p.y) - 0.2f, p.y);
                    DinoMaterialesBIRP.Aplicar(roca);
                    if (roca.GetComponentInChildren<Collider>() == null)
                    {
                        var mf = roca.GetComponentInChildren<MeshFilter>();
                        if (mf != null) mf.gameObject.AddComponent<MeshCollider>();
                    }
                    puestas++;
                    n++;
                }
            }

            // Matas de flores y arbustos en los bordes de las praderas (no en el centro donde pastan).
            var flor = cargar(PrefabFlor);
            var arbusto = cargar(PrefabArbusto);
            foreach (var z in Praderas)
            {
                for (int k = 0; k < 7; k++)
                {
                    var prefab = (k % 3 == 0 && arbusto != null) ? arbusto : flor;
                    if (prefab == null) continue;
                    float a = R() * Mathf.PI * 2f;
                    float r = z.Radio * Mathf.Lerp(0.75f, 1.05f, R());
                    var p = z.Centro + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    var f = instanciar(prefab, padreFlora);
                    f.name = $"Flora_{z.Nombre}_{k + 1}";
                    f.transform.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
                    f.transform.localScale = Vector3.one * (prefab == arbusto ? Mathf.Lerp(0.6f, 1f, R()) : Mathf.Lerp(1.2f, 2f, R()));
                    f.transform.position = new Vector3(p.x, AlturaSuelo(terreno, p.x, p.y), p.y);
                    DinoMaterialesBIRP.Aplicar(f);
                    n++;
                }
            }
            return n;
        }
    }
}
