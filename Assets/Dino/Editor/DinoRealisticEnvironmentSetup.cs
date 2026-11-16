using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DinoVehicle.Editor
{
    /// <summary>
    /// Reemplazo definitivo del entorno poligonal por el ecosistema y terreno PBR ultra realista:
    /// - Elimina completamente 'Environment' y todos los modelos poligonales / facetados (SimpleNaturePack).
    /// - Genera un Unity Terrain con suaves colinas orgánicas y 4 capas PBR (Grass_A, Grass_Soil_A, Pebbles_B, Cliff_Mossy_E).
    /// - Integra SkyboxMountains en el horizonte y niebla atmosférica (sin cortes al vacío negro).
    /// - Puebla arboledas PBR (Trees Collection Asset PBR + Conifers BOTD + Pines) con colisiones para TreeImpactResponse.
    /// - Instancia rocas fotorrealistas PBR, helechos, arbustos y flores de cactus (CactusFlowers_2).
    /// - Mantiene la fauna viva (Estegosaurios, Paquicefalosaurios, Velociraptores, Anquilosaurio).
    /// - Posiciona la Pelota Dinosaurio gigante, rampas de salto y cajas destructibles sobre el nuevo suelo suave.
    /// </summary>
    public static class DinoRealisticEnvironmentSetup
    {
        static readonly string[] EscenasObjetivo =
        {
            "Assets/Dino/Scenes/Simulation.unity",
            "Assets/Dino/Scenes/DinoTestTrack.unity"
        };

        // La reconstrucción completa ya NO se lanza sola al abrir el editor: borraba y regeneraba
        // las dos escenas en cada sesión, pisando cualquier cambio hecho a mano. Queda en el menú.
        // Lo único automático es reparar terrenos sin material (se ven morados), que no destruye nada.
        [InitializeOnLoadMethod]
        static void AutoRepararTerrenos()
        {
            EditorSceneManager.sceneOpened += (escena, modo) => { RepararTerrenosSinMaterial(); AjustarTamanoTerreno(); };
            EditorApplication.delayCall += () => { RepararTerrenosSinMaterial(); AjustarTamanoTerreno(); };
        }

        /// <summary>
        /// Aplica TamTerreno al terreno ya existente sin reconstruir la escena: regenera alturas y
        /// texturas del TerrainData y recentra el objeto. No toca árboles, fauna ni obstáculos.
        /// </summary>
        [MenuItem("DINO WEST/Ajustar Tamaño del Terreno")]
        static void AjustarTamanoTerreno()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var datos = AssetDatabase.LoadAssetAtPath<TerrainData>(RutaDatosTerreno);
            if (datos == null) return;

            if (Mathf.Abs(datos.size.x - TamTerreno) > 0.5f || Mathf.Abs(datos.size.z - TamTerreno) > 0.5f)
            {
                PrepararDatosTerreno();
                Debug.Log($"<color=green>[DinoRealisticEnvironmentSetup]</color> Terreno regenerado a {TamTerreno:0} x {TamTerreno:0} m.");
            }

            var posicion = new Vector3(-TamTerreno * 0.5f, 0f, -TamTerreno * 0.5f);
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                if (t.terrainData != datos || t.transform.position == posicion) continue;

                Undo.RecordObject(t.transform, "Recentrar terreno");
                t.transform.position = posicion;
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
                Debug.Log($"<color=green>[DinoRealisticEnvironmentSetup]</color> Terreno '{t.name}' recentrado. Guarda la escena (Ctrl+S).");
            }
        }

        static Material MaterialTerrenoEstandar()
        {
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Terrain-Standard.mat");
        }

        [MenuItem("DINO WEST/Reparar Terreno Morado")]
        static void RepararTerrenosSinMaterial()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var mat = MaterialTerrenoEstandar();
            if (mat == null) return;

            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                if (t.materialTemplate != null && t.materialTemplate.shader != null && t.materialTemplate.shader.isSupported) continue;

                t.materialTemplate = mat;
                EditorUtility.SetDirty(t);
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
                Debug.Log($"<color=green>[DinoRealisticEnvironmentSetup]</color> Material estándar asignado al terreno '{t.name}'. Guarda la escena (Ctrl+S).");
            }
        }

        [MenuItem("DINO WEST/Construir Terreno y Ecosistema Realista (Cero Poligonos)")]
        public static void EjecutarReemplazoCompleto()
        {
            // Animators reales (Idle, Walk, Run, Roar, Eat, Drink, Attack, Sniff, Sleep...) antes de crear la fauna.
            DinoControllerBuilder.BuildControllers(false);

            string escenaActivaAlIniciar = EditorSceneManager.GetActiveScene().path;

            foreach (var rutaEscena in EscenasObjetivo)
            {
                if (!File.Exists(rutaEscena)) continue;

                var escena = EditorSceneManager.OpenScene(rutaEscena);
                Debug.Log($"<color=cyan>[DinoRealisticEnvironmentSetup]</color> Procesando escena: {rutaEscena}...");

                // 1. Eliminar todo el contenido poligonal antiguo
                EliminarEntornoPoligonal();

                // 2. Crear y configurar el terreno suave y realista
                var terrainGo = CrearTerrenoRealista();

                // 3. Montañas lejanas en el horizonte y niebla atmosférica
                ConfigurarHorizonteYAtmosfera();

                // 4. Crear contenedor de ecosistema realista
                var ecoRoot = new GameObject("Ecosistema_DinoWest");
                Undo.RegisterCreatedObjectUndo(ecoRoot, "Crear Ecosistema Realista");

                var arboledaRoot = new GameObject("01_Arboleda_PBR_Realista");
                arboledaRoot.transform.SetParent(ecoRoot.transform, false);

                var comidaRoot = new GameObject("02_Arboles_Comida_Interactivas");
                comidaRoot.transform.SetParent(ecoRoot.transform, false);

                var rocasRoot = new GameObject("03_Rocas_PBR_Fotorrealistas");
                rocasRoot.transform.SetParent(ecoRoot.transform, false);

                var floraRoot = new GameObject("04_Flora_Flores_Y_Arbustos");
                floraRoot.transform.SetParent(ecoRoot.transform, false);

                var faunaRoot = new GameObject("05_Fauna_Dinosaurios_Vivos");
                faunaRoot.transform.SetParent(ecoRoot.transform, false);

                var juguetesRoot = new GameObject("06_Pista_Obstaculos_Y_Pelota");
                juguetesRoot.transform.SetParent(ecoRoot.transform, false);

                // 5. Poblar con los assets descargados (misma distribución que el generador en Play)
                var terreno = TerrenoDeEscena(terrainGo);
                int arboles = DinoEcosystemLayout.PoblarArboles(arboledaRoot.transform, comidaRoot.transform, terreno, Cargar, InstanciarPrefab);
                int detalles = DinoEcosystemLayout.PoblarRocasYFlora(rocasRoot.transform, floraRoot.transform, terreno, Cargar, InstanciarPrefab);
                int dinos = DinoEcosystemLayout.PoblarFauna(faunaRoot.transform, terreno, Cargar, InstanciarPrefab, ControllerDe, null);
                PoblarObstaculosYPelota(juguetesRoot.transform);
                Debug.Log($"<color=cyan>[DinoRealisticEnvironmentSetup]</color> {arboles} árboles, {detalles} rocas/flores y {dinos} dinosaurios en {rutaEscena}");

                // 6. Asegurar posición del jugador y componentes de impacto
                AsegurarJugador();

                EditorSceneManager.MarkSceneDirty(escena);
                EditorSceneManager.SaveScene(escena);
                Debug.Log($"<color=green>[DinoRealisticEnvironmentSetup]</color> ¡Escena {rutaEscena} actualizada con éxito al terreno y vegetación realista!");
            }

            if (!string.IsNullOrEmpty(escenaActivaAlIniciar))
            {
                EditorSceneManager.OpenScene(escenaActivaAlIniciar);
            }
        }

        // -------------------------------------------------------------
        // 1. LIMPIEZA TOTAL DE ACTIVOS POLIGONALES
        // -------------------------------------------------------------
        static void EliminarEntornoPoligonal()
        {
            // Eliminar Environment viejo si existe
            var env = GameObject.Find("Environment");
            if (env != null)
            {
                Undo.DestroyObjectImmediate(env);
            }

            // Eliminar ecosistema previo si existía
            var ecoViejo = GameObject.Find("Ecosistema_DinoWest");
            if (ecoViejo != null)
            {
                Undo.DestroyObjectImmediate(ecoViejo);
            }

            // Eliminar terreno plano viejo
            var pisoViejo = GameObject.Find("Terreno_Pista");
            if (pisoViejo != null)
            {
                Undo.DestroyObjectImmediate(pisoViejo);
            }

            var caminoViejo = GameObject.Find("Pista_Camino");
            if (caminoViejo != null)
            {
                Undo.DestroyObjectImmediate(caminoViejo);
            }

            // Eliminar cualquier terreno anterior para recrear limpio
            var terrenoPrevio = GameObject.Find("Terreno_Realista_PBR");
            if (terrenoPrevio != null)
            {
                Undo.DestroyObjectImmediate(terrenoPrevio);
            }

            // Buscar y eliminar cualquier residuo de SimpleNaturePack o modelos facetados sueltos
            var todos = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            foreach (var go in todos)
            {
                if (go == null) continue;
                string n = go.name.ToLowerInvariant();
                if (n.Contains("simplenature") || n.StartsWith("ground_") || n.StartsWith("hill_") ||
                    n.StartsWith("tree_0") || n.StartsWith("bush_0") || n.StartsWith("stump_") ||
                    n.StartsWith("flowers_0") || n.StartsWith("mushroom_0"))
                {
                    Undo.DestroyObjectImmediate(go);
                }
            }
        }

        // -------------------------------------------------------------
        // 2. CREACIÓN DEL TERRENO REALISTA PBR CON UNITY TERRAIN
        // -------------------------------------------------------------
        /// <summary>Lado del terreno en metros (360 m). Pista plana x ±28, z de -60 a 165, más 5 praderas planas.</summary>
        const float TamTerreno = DinoEcosystemLayout.TamTerreno;
        const string RutaDatosTerreno = "Assets/Dino/Terrains/DinoWest_TerrainData.asset";
        const string CarpetaCapas = "Assets/Dino/Terrains/Layers";

        static GameObject CrearTerrenoRealista()
        {
            TerrainData tData = PrepararDatosTerreno();

            var terrainGo = Terrain.CreateTerrainGameObject(tData);
            terrainGo.name = "Terreno_Realista_PBR";
            terrainGo.transform.position = new Vector3(-TamTerreno * 0.5f, 0f, -TamTerreno * 0.5f);

            var terrainComp = terrainGo.GetComponent<Terrain>();
            // Sin material el terreno se pinta morado: hay que asignar el estándar del pipeline Built-in.
            terrainComp.materialTemplate = MaterialTerrenoEstandar();
            terrainComp.drawTreesAndFoliage = true;
            terrainComp.treeDistance = 1500f;
            terrainComp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            terrainComp.heightmapPixelError = 3;
            terrainComp.basemapDistance = 400f;

            return terrainGo;
        }

        /// <summary>
        /// Copia de una capa del paquete URP apta para Built-in: sin MaskMap (el shader estándar la ignora
        /// y el albedo con alfa la volvía brillante como agua), sin metal y sin brillo.
        /// </summary>
        static TerrainLayer CapaBIRP(string nombre, float normalScale = 0.8f)
        {
            string ruta = $"{CarpetaCapas}/BIRP_{nombre}.terrainlayer";
            var origen = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"Assets/TerrainDemoScene_URP/Terrain/Layers/{nombre}.terrainlayer");
            if (origen == null) return null;

            if (!AssetDatabase.IsValidFolder(CarpetaCapas)) AssetDatabase.CreateFolder("Assets/Dino/Terrains", "Layers");
            var capa = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ruta);
            bool nueva = capa == null;
            if (nueva) capa = new TerrainLayer();

            capa.diffuseTexture = origen.diffuseTexture;
            capa.normalMapTexture = origen.normalMapTexture;
            capa.maskMapTexture = null;
            capa.tileSize = origen.tileSize;
            capa.tileOffset = origen.tileOffset;
            capa.normalScale = normalScale;
            capa.metallic = 0f;
            capa.smoothness = 0f;
            capa.specular = Color.black;
            capa.diffuseRemapMin = Vector4.zero;
            capa.diffuseRemapMax = Vector4.one;

            if (nueva) AssetDatabase.CreateAsset(capa, ruta);

            // Suavidad constante (0): con "alfa del difuso" el pasto con canal alfa brillaba como agua mojada.
            var so = new SerializedObject(capa);
            var fuente = so.FindProperty("m_SmoothnessSource");
            if (fuente != null)
            {
                fuente.intValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(capa);
            return capa;
        }

        static TerrainData PrepararDatosTerreno()
        {
            string dirPath = "Assets/Dino/Terrains";
            if (!AssetDatabase.IsValidFolder(dirPath)) AssetDatabase.CreateFolder("Assets/Dino", "Terrains");

            TerrainData tData = AssetDatabase.LoadAssetAtPath<TerrainData>(RutaDatosTerreno);
            if (tData == null)
            {
                tData = new TerrainData();
                tData.heightmapResolution = 513;
                tData.size = new Vector3(TamTerreno, DinoEcosystemLayout.AlturaTerreno, TamTerreno);
                AssetDatabase.CreateAsset(tData, RutaDatosTerreno);
            }
            else
            {
                tData.heightmapResolution = 513;
                tData.size = new Vector3(TamTerreno, DinoEcosystemLayout.AlturaTerreno, TamTerreno);
            }

            // 0 pradera, 1 tierra de la pista, 2 gravilla, 3 roca, 4 pasto de colina
            var capas = new List<TerrainLayer>();
            foreach (var c in new[] { CapaBIRP("Grass_A"), CapaBIRP("Grass_Soil_A"), CapaBIRP("Pebbles_B"), CapaBIRP("Cliff_Mossy_E", 1f), CapaBIRP("Grass_B") })
            {
                if (c != null) capas.Add(c);
            }
            if (capas.Count > 0) tData.terrainLayers = capas.ToArray();
            int numCapas = capas.Count;

            // Alturas: pista y praderas planas, transición smoothstep a colinas suaves.
            int res = tData.heightmapResolution;
            float[,] heights = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                float worldZ = ((float)z / (res - 1) - 0.5f) * TamTerreno;
                for (int x = 0; x < res; x++)
                {
                    float worldX = ((float)x / (res - 1) - 0.5f) * TamTerreno;
                    heights[z, x] = DinoEcosystemLayout.AlturaNormalizada(worldX, worldZ);
                }
            }
            tData.SetHeights(0, 0, heights);

            // Texturizado: tierra en la pista, gravilla en sus bordes, pasto en praderas,
            // mezcla de pastos en colinas y roca solo donde la pendiente es fuerte.
            int alphaRes = tData.alphamapResolution;
            float[,,] alphamaps = new float[alphaRes, alphaRes, Mathf.Max(1, numCapas)];
            var w = new float[5];

            for (int z = 0; z < alphaRes; z++)
            {
                float nz = (float)z / (alphaRes - 1);
                float worldZ = (nz - 0.5f) * TamTerreno;
                for (int x = 0; x < alphaRes; x++)
                {
                    float nx = (float)x / (alphaRes - 1);
                    float worldX = (nx - 0.5f) * TamTerreno;

                    System.Array.Clear(w, 0, w.Length);
                    float plan = DinoEcosystemLayout.Planitud(worldX, worldZ);
                    float pendiente = tData.GetSteepness(nx, nz);
                    float ruido = Mathf.PerlinNoise(worldX * 0.02f + 11f, worldZ * 0.02f + 7f);

                    bool enPista = worldZ >= DinoEcosystemLayout.PistaZMin + 20f && worldZ <= DinoEcosystemLayout.PistaZMax - 20f;
                    float distEje = Mathf.Abs(worldX);

                    float tierra = enPista ? 1f - Mathf.Clamp01((distEje - 6f) / 4f) : 0f;
                    float gravilla = enPista ? Mathf.Clamp01(1f - Mathf.Abs(distEje - 11f) / 3f) * 0.7f : 0f;
                    float roca = Mathf.Clamp01((pendiente - 20f) / 16f);

                    w[1] = tierra * 0.9f;
                    w[2] = gravilla;
                    w[3] = roca;
                    float resto = Mathf.Max(0f, 1f - w[1] - w[2] - w[3]);
                    // En lo plano domina el pasto de pradera; en colinas se mezcla con el segundo pasto.
                    float colina = Mathf.Clamp01((1f - plan) * 0.8f + (ruido - 0.5f) * 0.6f);
                    w[0] = resto * (1f - colina);
                    w[4] = resto * colina;

                    if (numCapas < 5) { w[0] += w[4]; w[4] = 0f; }
                    float suma = 0f;
                    for (int i = 0; i < numCapas; i++) suma += w[i];
                    if (suma <= 0f) { w[0] = 1f; suma = 1f; }
                    for (int i = 0; i < numCapas; i++) alphamaps[z, x, i] = w[i] / suma;
                }
            }

            if (numCapas > 0) tData.SetAlphamaps(0, 0, alphamaps);
            EditorUtility.SetDirty(tData);
            AssetDatabase.SaveAssets();
            return tData;
        }

        // -------------------------------------------------------------
        // 3. HORIZONTE DE MONTAÑAS Y NIEBLA ATMOSFÉRICA
        // -------------------------------------------------------------
        static void ConfigurarHorizonteYAtmosfera()
        {
            string skyboxPrefabPath = "Assets/TerrainDemoScene_URP/Prefabs/Skybox/SkyboxMountains.prefab";
            var skyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(skyboxPrefabPath);

            var existingSky = GameObject.Find("SkyboxMountains_DinoWest");
            if (existingSky != null)
            {
                Undo.DestroyObjectImmediate(existingSky);
            }

            if (skyPrefab != null)
            {
                var skyGo = (GameObject)PrefabUtility.InstantiatePrefab(skyPrefab);
                skyGo.name = "SkyboxMountains_DinoWest";
                skyGo.transform.position = new Vector3(0f, -25f, 0f);
                skyGo.transform.localScale = Vector3.one * 1.15f;
                DinoMaterialesBIRP.Aplicar(skyGo);
            }

            // Niebla atmosférica suave que se funde con el horizonte (sin vacío negro)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0032f;
            RenderSettings.fogColor = new Color(0.72f, 0.79f, 0.86f);
        }

        // -------------------------------------------------------------
        // 4-7. BOSQUES, ROCAS, FLORA Y FAUNA (DinoEcosystemLayout)
        // -------------------------------------------------------------
        static GameObject Cargar(string ruta) => AssetDatabase.LoadAssetAtPath<GameObject>(ruta);

        static GameObject InstanciarPrefab(GameObject prefab, Transform padre)
        {
            var go = PrefabUtility.InstantiatePrefab(prefab, padre) as GameObject;
            return go != null ? go : Object.Instantiate(prefab, padre);
        }

        static Terrain TerrenoDeEscena(GameObject terrainGo)
        {
            var t = terrainGo != null ? terrainGo.GetComponent<Terrain>() : null;
            return t != null ? t : Terrain.activeTerrain;
        }

        static RuntimeAnimatorController ControllerDe(DinoEcosystemLayout.Especie e)
        {
            string ruta = e switch
            {
                DinoEcosystemLayout.Especie.Estegosaurio => DinoControllerBuilder.CtrlEstegosaurio,
                DinoEcosystemLayout.Especie.Paquicefalo => DinoControllerBuilder.CtrlPaquicefalo,
                DinoEcosystemLayout.Especie.Raptor => DinoControllerBuilder.CtrlRaptor,
                _ => null,
            };
            return ruta != null ? DinoMountManager.CargarControllerValido(ruta) : null;
        }

        // -------------------------------------------------------------
        // 8. PISTA: PELOTA DINOSAURIO, RAMPAS Y CAJAS DESTRUCTORAS
        // -------------------------------------------------------------
        static void PoblarObstaculosYPelota(Transform juguetesParent)
        {
            // Pelota Dinosaurio Prehistórica
            var pelotaExistente = GameObject.Find("Pelota_Dinosaurio_Prehistorica");
            if (pelotaExistente != null) Undo.DestroyObjectImmediate(pelotaExistente);

            var pelota = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pelota.name = "Pelota_Dinosaurio_Prehistorica";
            pelota.transform.SetParent(juguetesParent, false);
            pelota.transform.position = new Vector3(12f, 1.7f, 25f);
            pelota.transform.localScale = Vector3.one * 3.2f;

            var rb = pelota.AddComponent<Rigidbody>();
            rb.mass = 45f;
            rb.linearDamping = 0.15f;
            rb.angularDamping = 0.25f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            pelota.GetComponent<Renderer>().sharedMaterial = MaterialPlano("M_DW_Pelota", new Color(0.95f, 0.55f, 0.12f), 0.65f);
            if (pelota.GetComponent<DinoBallToy>() == null) pelota.AddComponent<DinoBallToy>();

            // Las rampas y cajas originales de la escena se conservan; solo se crean si no hay ninguna.
            bool hayRampas = false;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.name == "Rampa_Salto" && !t.IsChildOf(juguetesParent)) { hayRampas = true; break; }
            }
            if (hayRampas) return;

            CrearRampa(new Vector3(0f, 0f, 40f), new Vector3(10f, 2.2f, 12f), 18f, juguetesParent);
            CrearRampa(new Vector3(0f, 0f, 105f), new Vector3(12f, 3.6f, 15f), 24f, juguetesParent);
            CrearPiramideCajas(new Vector3(0f, 0.5f, 72f), 4, juguetesParent);
            CrearPiramideCajas(new Vector3(8f, 0.5f, 140f), 3, juguetesParent);
        }

        /// <summary>Material Standard guardado como asset (los creados en memoria se pierden al guardar la escena).</summary>
        static Material MaterialPlano(string nombre, Color color, float brillo)
        {
            string ruta = $"Assets/Dino/Materials/{nombre}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (m != null) return m;
            m = new Material(Shader.Find("Standard")) { color = color };
            m.SetFloat("_Glossiness", brillo);
            AssetDatabase.CreateAsset(m, ruta);
            return m;
        }

        static void CrearRampa(Vector3 pos, Vector3 tam, float angulo, Transform parent)
        {
            var rampa = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rampa.name = "Rampa_Salto";
            rampa.transform.SetParent(parent, false);
            rampa.transform.position = pos + new Vector3(0, tam.y * 0.45f, 0);
            rampa.transform.localScale = tam;
            rampa.transform.rotation = Quaternion.Euler(-angulo, 0f, 0f);
            rampa.GetComponent<MeshRenderer>().sharedMaterial = MaterialPlano("M_DW_Rampa", new Color(0.68f, 0.48f, 0.28f), 0.2f);
        }

        static void CrearPiramideCajas(Vector3 posBase, int filas, Transform parent)
        {
            float tamCaja = 1.0f;
            var mat = MaterialPlano("M_DW_Caja", new Color(0.78f, 0.58f, 0.36f), 0.15f);
            for (int f = 0; f < filas; f++)
            {
                int cajasEnFila = filas - f;
                float inicioX = -(cajasEnFila - 1) * tamCaja * 0.5f;
                float y = posBase.y + f * tamCaja;

                for (int c = 0; c < cajasEnFila; c++)
                {
                    var caja = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    caja.name = $"Caja_Destructible_{f}_{c}";
                    caja.transform.SetParent(parent, false);
                    caja.transform.position = new Vector3(posBase.x + inicioX + c * tamCaja, y, posBase.z);
                    caja.transform.localScale = Vector3.one * (tamCaja * 0.95f);
                    var rb = caja.AddComponent<Rigidbody>();
                    rb.mass = 25f;
                    caja.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
            }
        }

        // -------------------------------------------------------------
        // 9. CONFIGURAR JUGADOR Y MONTURA
        // -------------------------------------------------------------
        static void AsegurarJugador()
        {
            var vehicle = Object.FindAnyObjectByType<DinoVehicleController>();
            if (vehicle != null)
            {
                // Asegurar altura sobre el terreno suave (la salida está en la pista plana)
                vehicle.transform.position = new Vector3(0f, 1.0f, 0f);
                vehicle.transform.rotation = Quaternion.identity;

                if (vehicle.GetComponent<TreeImpactResponse>() == null)
                    vehicle.gameObject.AddComponent<TreeImpactResponse>();

                if (vehicle.GetComponent<DinoBirdProximity>() == null)
                    vehicle.gameObject.AddComponent<DinoBirdProximity>();
            }
        }
    }
}
