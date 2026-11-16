using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DinoVehicle
{
    /// <summary>
    /// Construye los AnimatorController de la fauna y de las monturas con los clips reales de cada FBX.
    /// Estados comunes: Idle, Idle2, Walk, Run, Roar, Eat, Drink, Attack (+ Sniff, Sleep, GoToSleep, WakeUp en Raptor/Paquicefalo).
    /// Sin parámetros: la IA y DinoAnimationDriver los activan con CrossFade por nombre.
    /// Los estados en bucle cuyo clip no tiene Loop Time reciben una transición a sí mismos.
    /// </summary>
    public static class DinoControllerBuilder
    {
        const string OutDir = "Assets/Dino/Animators";

        public const string CtrlRaptor = OutDir + "/Ctrl_Velociraptor.controller";
        public const string CtrlPaquicefalo = OutDir + "/Ctrl_Pachycephalosaurus.controller";
        public const string CtrlEstegosaurio = OutDir + "/Ctrl_Stegosaurus.controller";

        public const string FbxRaptor = "Assets/PBRVelociraptor/Models/15K/Raptor_Animated_FBX.fbx";
        public const string FbxPaquicefalo = "Assets/FerociousIndustries/PBRDinosaurs/PBRPachycephalasaurus/Models/LODG/Pachycephalasaurus.fbx";
        public const string FbxEstegosaurio = "Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/Models/LODG/Stegasaurus_LODG.fbx";

        /// <summary>Estados que deben repetirse mientras la IA los mantiene.</summary>
        static readonly HashSet<string> EstadosEnBucle = new() { "Idle", "Idle2", "Walk", "Run", "Eat", "Drink", "Sniff", "Sleep" };

        // Estado -> clip (nombre exacto en el FBX; los "_RM" con root motion no se usan).
        static readonly (string estado, string clip)[] MapaRaptor =
        {
            ("Idle", "RaptorArmature|Raptor_Idle1_Anim"),
            ("Idle2", "RaptorArmature|Raptor_Idle2_Anim"),
            ("Walk", "RaptorArmature|Raptor_Walk_Anim"),
            ("Run", "RaptorArmature|Raptor_Run1_Anim"),
            ("Roar", "RaptorArmature|Raptor_Roar1_Anim"),
            ("Call", "RaptorArmature|Raptor_Call1_Anim"),
            ("Eat", "RaptorArmature|Raptor_EatPrey_Anim"),
            ("Drink", "RaptorArmature|Raptor_Drink_Anim"),
            ("Attack", "RaptorArmature|Raptor_Bite1_Anim"),
            ("Sniff", "RaptorArmature|Raptor_SniffGround_Anim"),
            ("GoToSleep", "RaptorArmature|Raptor_GoToSleep_Anim"),
            ("Sleep", "RaptorArmature|Raptor_SleepLoop_Anim"),
            ("WakeUp", "RaptorArmature|Raptor_WakeFromSleep_Anim"),
        };

        static readonly (string estado, string clip)[] MapaPaquicefalo =
        {
            ("Idle", "Armature|Pachycephalasaurus_IdleA"),
            ("Idle2", "Armature|Pachycephalasaurus_IdleB"),
            ("Walk", "Armature|Pachycephalasaurus_Walk"),
            ("Run", "Armature|Pachycephalasaurus_Run"),
            ("Roar", "Armature|Pachycephalasaurus_Roar"),
            ("Call", "Armature|Pachycephalasaurus_Bark"),
            ("Eat", "Armature|Pachycephalasaurus_EatLow"),
            ("Drink", "Armature|Pachycephalasaurus_Drink"),
            ("Attack", "Armature|Pachycephalasaurus_Headbutt"),
            ("GoToSleep", "Armature|Pachycephalasaurus_GoToSleep"),
            ("Sleep", "Armature|Pachycephalasaurus_SleepLoop"),
            ("WakeUp", "Armature|Pachycephalasaurus_WakeFromSleep"),
        };

        static readonly (string estado, string clip)[] MapaEstegosaurio =
        {
            ("Idle", "Armature|Stegasaurus_IdleA"),
            ("Idle2", "Armature|Stegasaurus_IdleB"),
            ("Walk", "Armature|Stegasaurus_Walk"),
            ("Run", "Armature|Stegasaurus_Run"),
            ("Roar", "Armature|Stegasaurus_Roar"),
            ("Call", "Armature|Stegasaurus_Stomp"),
            ("Eat", "Armature|Stegasaurus_Eat"),
            ("Drink", "Armature|Stegasaurus_Drink"),
            ("Attack", "Armature|Stegasaurus_TailWhip"),
        };

        // Crear assets dentro de InitializeOnLoad deja el controller sin su StateMachine
        // (la AssetDatabase aún no está lista), así que se difiere al primer tick del editor.
        [InitializeOnLoadMethod]
        static void AutoBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.playModeStateChanged += AlSalirDePlay;
                    return;
                }
                BuildControllers(false);
            };
        }

        static void AlSalirDePlay(PlayModeStateChange estado)
        {
            if (estado != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= AlSalirDePlay;
            BuildControllers(false);
        }

        [MenuItem("DINO WEST/Construir Animators de Fauna y Monturas")]
        public static void BuildControllersMenu() => BuildControllers(true);

        public static void BuildControllers(bool forzar)
        {
            if (!AssetDatabase.IsValidFolder(OutDir))
            {
                AssetDatabase.CreateFolder("Assets/Dino", "Animators");
            }

            BuildController(CtrlRaptor, FbxRaptor, MapaRaptor, forzar);
            BuildController(CtrlPaquicefalo, FbxPaquicefalo, MapaPaquicefalo, forzar);
            BuildController(CtrlEstegosaurio, FbxEstegosaurio, MapaEstegosaurio, forzar);
        }

        /// <summary>Un controller sirve si su capa base tiene StateMachine con al menos un estado.</summary>
        public static bool EsValido(AnimatorController ctrl)
        {
            if (ctrl == null) return false;
            var layers = ctrl.layers;
            return layers.Length > 0
                && layers[0].stateMachine != null
                && layers[0].stateMachine.states.Length > 0;
        }

        static bool EstaCompleto(AnimatorController ctrl, (string estado, string clip)[] mapa)
        {
            if (!EsValido(ctrl)) return false;
            var nombres = new HashSet<string>();
            foreach (var s in ctrl.layers[0].stateMachine.states)
            {
                if (s.state.motion == null) return false;
                nombres.Add(s.state.name);
            }
            foreach (var (estado, _) in mapa)
            {
                if (!nombres.Contains(estado)) return false;
            }
            return true;
        }

        static Dictionary<string, AnimationClip> ClipsDe(string fbxPath)
        {
            var dic = new Dictionary<string, AnimationClip>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (a is AnimationClip c && !c.name.StartsWith("__preview__")) dic[c.name] = c;
            }
            return dic;
        }

        static void BuildController(string path, string fbxPath, (string estado, string clip)[] mapa, bool forzar)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!forzar && EstaCompleto(ctrl, mapa)) return;

            var clips = ClipsDe(fbxPath);
            if (clips.Count == 0)
            {
                Debug.LogWarning($"[DinoControllerBuilder] No se encontraron clips en {fbxPath}; no se crea {path}");
                return;
            }

            // Se reutiliza el asset (mismo GUID) para no romper referencias de escenas y prefabs.
            if (ctrl == null || ctrl.layers.Length == 0 || ctrl.layers[0].stateMachine == null)
            {
                if (ctrl != null) AssetDatabase.DeleteAsset(path);
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            var root = ctrl.layers[0].stateMachine;
            foreach (var s in root.states) root.RemoveState(s.state);
            foreach (var t in root.anyStateTransitions) root.RemoveAnyStateTransition(t);
            foreach (var p in ctrl.parameters) ctrl.RemoveParameter(p);

            AnimatorState idle = null;
            int fila = 0;
            foreach (var (estado, nombreClip) in mapa)
            {
                if (!clips.TryGetValue(nombreClip, out var clip))
                {
                    Debug.LogWarning($"[DinoControllerBuilder] Falta el clip '{nombreClip}' en {fbxPath}");
                    continue;
                }

                var st = root.AddState(estado, new Vector3(300f + (fila % 3) * 220f, 60f * (fila / 3), 0f));
                st.motion = clip;
                st.writeDefaultValues = true;
                fila++;

                if (EstadosEnBucle.Contains(estado) && !clip.isLooping)
                {
                    var t = st.AddTransition(st);
                    t.hasExitTime = true;
                    t.exitTime = 0.97f;
                    t.duration = 0.08f;
                    t.hasFixedDuration = false;
                }

                if (estado == "Idle") idle = st;
            }

            // GoToSleep encadena con Sleep y WakeUp vuelve a Idle sin que la IA tenga que vigilarlo.
            var porNombre = new Dictionary<string, AnimatorState>();
            foreach (var s in root.states) porNombre[s.state.name] = s.state;
            Encadenar(porNombre, "GoToSleep", "Sleep");
            Encadenar(porNombre, "WakeUp", "Idle");

            if (idle != null) root.defaultState = idle;

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=green>[DinoControllerBuilder]</color> Controller listo: {path} ({root.states.Length} estados)");
        }

        static void Encadenar(Dictionary<string, AnimatorState> estados, string desde, string hacia)
        {
            if (!estados.TryGetValue(desde, out var a) || !estados.TryGetValue(hacia, out var b)) return;
            var t = a.AddTransition(b);
            t.hasExitTime = true;
            t.exitTime = 0.95f;
            t.duration = 0.15f;
        }
    }
}
