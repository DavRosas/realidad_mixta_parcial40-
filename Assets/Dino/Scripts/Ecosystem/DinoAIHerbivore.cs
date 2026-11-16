using UnityEngine;

namespace DinoVehicle
{
    /// <summary>
    /// IA de herbívoros en manada (Estegosaurios, Paquicefalosaurios; Anquilosaurio en modo estático).
    /// - Vigilar (Idle/Idle2), Pastar (Eat), Beber (Drink) y pasear por su pradera.
    /// - Los miembros siguen al líder manteniendo su hueco en la formación.
    /// - Si el jugador se acerca despacio, un adulto lo encara y le advierte con un rugido (Roar).
    /// - Si se acerca rápido, ruge cerca o el líder huye, toda la manada sale en estampida (Run).
    /// El desplazamiento usa DinoAIBase: sin patinaje y pegado al terreno.
    /// </summary>
    public class DinoAIHerbivore : DinoAIBase
    {
        public enum AIState { Vigilar, Pastar, Beber, Pasear, Seguir, Advertir, Huir }

        [Header("Comportamiento")]
        [SerializeField] AIState currentState = AIState.Vigilar;
        [Tooltip("Radio de la pradera alrededor de su centro")]
        public float territoryRadius = 35f;
        [Tooltip("Distancia a la que encara al jugador y ruge de advertencia")]
        public float distanciaAdvertencia = 30f;
        [Tooltip("Distancia a la que huye aunque el jugador vaya despacio")]
        public float fearDistance = 13f;
        [Tooltip("Un rugido del jugador dentro de esta distancia provoca estampida")]
        public float roarFearDistance = 45f;
        [Tooltip("Multiplicador de Run en la estampida (animator.speed sube igual: no patina)")]
        public float multiplicadorHuida = 1.35f;

        [Header("Manada")]
        public DinoAIHerbivore lider;
        [Tooltip("Hueco en la formación, relativo al líder (en metros)")]
        public Vector3 offsetManada;
        public Vector3 centroTerritorio;
        public bool centroAsignado;

        float timer;
        float duracion;
        Vector3 destino;
        float cooldownAdvertencia;
        Vector3 dirHuida;

        public bool Huyendo => currentState == AIState.Huir;
        public Vector3 DireccionHuida => dirHuida;
        public AIState Estado => currentState;

        protected override void Start()
        {
            base.Start();
            if (!centroAsignado)
            {
                centroTerritorio = transform.position;
                centroAsignado = true;
            }
            // Desfase para que la manada no haga todo al unísono.
            cooldownAdvertencia = Random.Range(2f, 6f);
            ElegirActividadQuieta();
            timer = Random.Range(0f, 3f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            timer += dt;
            cooldownAdvertencia -= dt;

            if (estatico)
            {
                ComportamientoEstatico();
                PegarAlSuelo();
                ActualizarAnimacion();
                return;
            }

            EvaluarAmenaza();

            switch (currentState)
            {
                case AIState.Vigilar:
                case AIState.Pastar:
                case AIState.Beber:
                    Frenar();
                    if (lider != null && LejosDeSuHueco(6f))
                    {
                        Cambiar(AIState.Seguir);
                    }
                    else if (timer >= duracion)
                    {
                        if (lider == null && Random.value < 0.45f) EmpezarPaseo();
                        else ElegirActividadQuieta();
                    }
                    break;

                case AIState.Pasear:
                    Locomover(destino, VelCaminar, 2f);
                    if (Plano(destino - transform.position).magnitude < 2.5f || timer > 40f)
                    {
                        ElegirActividadQuieta();
                    }
                    break;

                case AIState.Seguir:
                {
                    Vector3 hueco = HuecoEnManada();
                    float dist = Plano(hueco - transform.position).magnitude;
                    // Si se ha quedado muy atrás trota (Run) para alcanzar al grupo.
                    float vel = dist > 18f ? VelCorrer * 0.8f : VelCaminar * Mathf.Lerp(0.8f, 1.15f, Mathf.InverseLerp(3f, 12f, dist));
                    Locomover(hueco, vel, 1.5f);
                    if (dist < 2f && velActual < 0.2f) ElegirActividadQuieta();
                    break;
                }

                case AIState.Advertir:
                    if (AccionEnCurso)
                    {
                        Frenar();
                    }
                    else if (duracion < 0f && AlineacionCon(PosJugador) < 0.85f && timer < 3f)
                    {
                        // Primero encara al jugador andando despacio (sin pivotar en seco).
                        Locomover(PosJugador, VelCaminar * 0.5f, 0f);
                    }
                    else if (duracion < 0f)
                    {
                        Frenar();
                        duracion = 0f;
                        IniciarAccion("Roar", 4f);
                    }
                    else
                    {
                        Frenar();
                        ElegirActividadQuieta(AIState.Vigilar);
                    }
                    break;

                case AIState.Huir:
                {
                    if (lider != null && lider.Huyendo)
                    {
                        dirHuida = Vector3.Slerp(dirHuida, lider.DireccionHuida, dt * 2f).normalized;
                    }
                    else if (jugador != null && timer % 2f < dt)
                    {
                        dirHuida = Vector3.Slerp(dirHuida, Plano(transform.position - PosJugador).normalized, 0.6f).normalized;
                    }

                    Vector3 objetivo = transform.position + dirHuida * 25f;
                    // En estampida los de atrás conservan su hueco lateral.
                    if (lider != null && lider.Huyendo) objetivo = Vector3.Lerp(objetivo, HuecoEnManada() + dirHuida * 20f, 0.5f);
                    Locomover(objetivo, VelCorrer * multiplicadorHuida, 0.5f);

                    bool seguro = DistanciaJugador() > fearDistance * 3f && timer > 5f;
                    if ((lider == null && seguro) || (lider != null && !lider.Huyendo && timer > 3f))
                    {
                        if (lider == null) centroTerritorio = Vector3.Lerp(centroTerritorio, transform.position, 0.5f);
                        ElegirActividadQuieta(AIState.Vigilar);
                    }
                    break;
                }
            }

            PegarAlSuelo();
            ActualizarAnimacion();
        }

        void EvaluarAmenaza()
        {
            if (jugador == null || currentState == AIState.Huir) return;

            float dist = DistanciaJugador();
            bool rapido = jugador.CurrentSpeedKmh > 18f;
            bool rugidoCerca = jugador.IsRoaring && dist < roarFearDistance;
            bool liderHuye = lider != null && lider.Huyendo;

            if (rugidoCerca || liderHuye || dist < (rapido ? fearDistance * 1.6f : fearDistance))
            {
                CancelarAccion();
                dirHuida = liderHuye ? lider.DireccionHuida : Plano(transform.position - PosJugador).normalized;
                if (dirHuida.sqrMagnitude < 0.01f) dirHuida = -Frente;
                Cambiar(AIState.Huir);
                return;
            }

            // Advertencia: solo si está tranquilo y no ha rugido hace poco.
            if (currentState != AIState.Advertir && dist < distanciaAdvertencia && cooldownAdvertencia <= 0f)
            {
                cooldownAdvertencia = Random.Range(10f, 16f);
                Cambiar(AIState.Advertir);
                duracion = -1f; // marca "aún no ha rugido"
            }
        }

        void ComportamientoEstatico()
        {
            // Vigilante: alterna Idle / Idle2 y ruge si el jugador se acerca. Nunca se desplaza.
            Frenar();
            if (jugador != null && DistanciaJugador() < distanciaAdvertencia && cooldownAdvertencia <= 0f && !AccionEnCurso)
            {
                cooldownAdvertencia = 12f;
                IniciarAccion("Roar", 4f);
            }
            if (timer >= duracion)
            {
                timer = 0f;
                duracion = Random.Range(5f, 10f);
                animQuieta = Random.value < 0.5f ? "Idle" : "Idle2";
            }
        }

        void ElegirActividadQuieta(AIState? forzado = null)
        {
            AIState s;
            if (forzado.HasValue) s = forzado.Value;
            else
            {
                float r = Random.value;
                s = r < 0.45f ? AIState.Pastar : (r < 0.62f ? AIState.Beber : AIState.Vigilar);
            }

            Cambiar(s);
            switch (s)
            {
                case AIState.Pastar:
                    animQuieta = "Eat";
                    duracion = Random.Range(6f, 13f);
                    break;
                case AIState.Beber:
                    animQuieta = "Drink";
                    duracion = Random.Range(5f, 9f);
                    break;
                default:
                    animQuieta = Random.value < 0.6f ? "Idle" : "Idle2";
                    duracion = Random.Range(4f, 9f);
                    break;
            }
        }

        void EmpezarPaseo()
        {
            Vector2 c = Random.insideUnitCircle * territoryRadius;
            destino = centroTerritorio + new Vector3(c.x, 0f, c.y);
            animQuieta = "Idle";
            Cambiar(AIState.Pasear);
        }

        Vector3 HuecoEnManada()
        {
            if (lider == null) return transform.position;
            Quaternion rumbo = Quaternion.Euler(0f, lider.yaw, 0f);
            return lider.transform.position + rumbo * offsetManada;
        }

        bool LejosDeSuHueco(float margen)
        {
            return Plano(HuecoEnManada() - transform.position).magnitude > margen;
        }

        void Cambiar(AIState s)
        {
            currentState = s;
            timer = 0f;
        }

        static Vector3 Plano(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
