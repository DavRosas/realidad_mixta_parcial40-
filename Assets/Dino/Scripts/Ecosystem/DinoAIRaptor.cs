using UnityEngine;

namespace DinoVehicle
{
    /// <summary>
    /// IA de Velociraptores en manada.
    /// - Patrullan su llanura siguiendo al líder; olfatean el suelo (Sniff), descansan (Idle/Idle2) o duermen (Sleep).
    /// - Al detectar al jugador lo rodean a distancia (Circling) y de vez en cuando uno lo amenaza (Roar/Call/Attack).
    /// - Si el jugador ruge, embiste rápido o se acerca demasiado, se dispersan corriendo y luego se reagrupan.
    /// El modelo PBR del Velociraptor mira hacia -Z: frenteInvertido = true.
    /// </summary>
    public class DinoAIRaptor : DinoAIBase
    {
        public enum RaptorState { Patrullar, Olfatear, Descansar, Dormir, Despertar, Rodear, Amenazar, Dispersarse, Reagrupar }

        [Header("Comportamiento Raptor")]
        [SerializeField] RaptorState currentState = RaptorState.Patrullar;
        public float radioPatrulla = 30f;
        [Tooltip("Distancia a la que detectan al jugador y empiezan a rodearlo")]
        public float distanciaDeteccion = 45f;
        [Tooltip("Radio del círculo alrededor del jugador")]
        public float radioRodeo = 16f;
        [Tooltip("Si el jugador viene embalado dentro de esta distancia, se dispersan")]
        public float alertDistance = 20f;
        [Tooltip("Distancia mínima: más cerca se dispersan aunque el jugador vaya despacio")]
        public float distanciaPanico = 7f;

        [Header("Manada")]
        public DinoAIRaptor lider;
        public Vector3 offsetManada;
        public Vector3 centroTerritorio;
        public bool centroAsignado;
        public int indiceManada;

        float timer;
        float duracion;
        Vector3 destino;
        Vector3 dirHuida;
        float anguloRodeo;
        float sentidoRodeo = 1f;
        float limiteRodeo;
        float cooldownAmenaza;
        float cooldownRodeo;
        bool accionLanzada;

        public RaptorState Estado => currentState;

        void Reset()
        {
            frenteInvertido = true;
            velocidadClipCaminar = 1.6f;
            velocidadClipCorrer = 7.5f;
            aceleracion = 7f;
            giroGradosSeg = 140f;
        }

        protected override void Start()
        {
            base.Start();
            if (!centroAsignado)
            {
                centroTerritorio = transform.position;
                centroAsignado = true;
            }
            sentidoRodeo = Random.value < 0.5f ? 1f : -1f;
            cooldownAmenaza = Random.Range(4f, 8f) + indiceManada * 1.5f;
            EmpezarDescanso();
            timer = Random.Range(0f, 2f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            timer += dt;
            cooldownAmenaza -= dt;
            cooldownRodeo -= dt;

            EvaluarJugador();

            switch (currentState)
            {
                case RaptorState.Patrullar:
                    if (lider == null)
                    {
                        Locomover(destino, VelCaminar, 1.5f);
                        if (Plano(destino - transform.position).magnitude < 2f || timer > 35f) DecidirParada();
                    }
                    else
                    {
                        SeguirHueco();
                        if (!LejosDeSuHueco(2.5f) && lider.velActual < 0.2f) ImitarAlLider();
                    }
                    break;

                case RaptorState.Olfatear:
                case RaptorState.Descansar:
                    Frenar();
                    if (lider != null)
                    {
                        if (LejosDeSuHueco(5f)) Cambiar(RaptorState.Patrullar);
                    }
                    else if (timer >= duracion)
                    {
                        NuevoPuntoPatrulla();
                    }
                    break;

                case RaptorState.Dormir:
                    Frenar();
                    if (timer >= duracion || (lider != null && lider.currentState != RaptorState.Dormir && timer > 2f))
                    {
                        IniciarAccion("WakeUp", 3f);
                        animQuieta = "Idle";
                        Cambiar(RaptorState.Despertar);
                    }
                    break;

                case RaptorState.Despertar:
                    Frenar();
                    if (!AccionEnCurso) NuevoPuntoPatrulla();
                    break;

                case RaptorState.Rodear:
                    Rodear(dt);
                    break;

                case RaptorState.Amenazar:
                    if (AccionEnCurso)
                    {
                        Frenar();
                    }
                    else if (!accionLanzada && AlineacionCon(PosJugador) < 0.8f && timer < 2.5f)
                    {
                        Locomover(PosJugador, VelCaminar * 0.6f, 0f);
                    }
                    else if (!accionLanzada)
                    {
                        Frenar();
                        accionLanzada = true;
                        float r = Random.value;
                        IniciarAccion(r < 0.45f ? "Roar" : (r < 0.75f ? "Call" : "Attack"), 3.5f);
                    }
                    else
                    {
                        Cambiar(RaptorState.Rodear);
                    }
                    break;

                case RaptorState.Dispersarse:
                    Locomover(transform.position + dirHuida * 30f, VelCorrer * 1.5f, 0.5f);
                    if (timer > duracion && DistanciaJugador() > alertDistance * 1.5f)
                    {
                        cooldownRodeo = 10f;
                        Cambiar(RaptorState.Reagrupar);
                    }
                    break;

                case RaptorState.Reagrupar:
                {
                    Vector3 objetivo = lider != null ? HuecoEnManada() : centroTerritorio;
                    float dist = Plano(objetivo - transform.position).magnitude;
                    Locomover(objetivo, dist > 20f ? VelCorrer * 0.75f : VelCaminar, 2f);
                    if (dist < 3f) EmpezarDescanso();
                    break;
                }
            }

            PegarAlSuelo();
            ActualizarAnimacion();
        }

        void EvaluarJugador()
        {
            if (jugador == null || currentState == RaptorState.Dispersarse) return;

            float dist = DistanciaJugador();
            bool panico = (jugador.IsRoaring && dist < 35f)
                       || (jugador.CurrentSpeedKmh > 22f && dist < alertDistance)
                       || dist < distanciaPanico
                       || (lider != null && lider.currentState == RaptorState.Dispersarse);

            if (panico)
            {
                Dispersar();
                return;
            }

            bool tranquilo = currentState == RaptorState.Patrullar || currentState == RaptorState.Olfatear
                          || currentState == RaptorState.Descansar || currentState == RaptorState.Reagrupar;

            if (currentState == RaptorState.Dormir && dist < distanciaDeteccion * 0.6f)
            {
                IniciarAccion("WakeUp", 3f);
                animQuieta = "Idle";
                Cambiar(RaptorState.Despertar);
                return;
            }

            bool liderRodea = lider != null && (lider.currentState == RaptorState.Rodear || lider.currentState == RaptorState.Amenazar);
            if (tranquilo && cooldownRodeo <= 0f && (dist < distanciaDeteccion || liderRodea))
            {
                EmpezarRodeo();
            }
        }

        void Rodear(float dt)
        {
            if (jugador == null) { EmpezarDescanso(); return; }

            float vRodeo = Mathf.Lerp(VelCaminar, VelCorrer, 0.25f);
            anguloRodeo += sentidoRodeo * (vRodeo / Mathf.Max(4f, radioRodeo)) * dt;
            Vector3 p = PosJugador;
            Vector3 objetivo = p + new Vector3(Mathf.Sin(anguloRodeo), 0f, Mathf.Cos(anguloRodeo)) * radioRodeo;

            float dist = Plano(objetivo - transform.position).magnitude;
            float vel = dist > 8f ? VelCorrer * 0.9f : vRodeo;
            Locomover(objetivo, vel, 0.5f);

            float distJugador = DistanciaJugador();
            if (cooldownAmenaza <= 0f && distJugador < radioRodeo * 1.4f)
            {
                cooldownAmenaza = Random.Range(6f, 11f);
                accionLanzada = false;
                Cambiar(RaptorState.Amenazar);
                return;
            }

            if (timer > limiteRodeo || distJugador > distanciaDeteccion * 1.6f)
            {
                cooldownRodeo = 12f;
                Cambiar(RaptorState.Reagrupar);
            }
        }

        void EmpezarRodeo()
        {
            Vector3 d = Plano(transform.position - PosJugador);
            anguloRodeo = Mathf.Atan2(d.x, d.z);
            if (lider != null) sentidoRodeo = lider.sentidoRodeo;
            limiteRodeo = Random.Range(18f, 28f);
            animQuieta = "Idle";
            CancelarAccion();
            Cambiar(RaptorState.Rodear);
        }

        void Dispersar()
        {
            Vector3 lejos = Plano(transform.position - PosJugador).normalized;
            if (lejos.sqrMagnitude < 0.01f) lejos = -Frente;
            dirHuida = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * lejos;
            duracion = Random.Range(4f, 6.5f);
            animQuieta = "Idle";
            CancelarAccion();
            Cambiar(RaptorState.Dispersarse);
        }

        void DecidirParada()
        {
            float r = Random.value;
            if (r < 0.4f) { animQuieta = "Sniff"; duracion = Random.Range(4f, 7f); Cambiar(RaptorState.Olfatear); }
            else if (r < 0.62f) EmpezarDescanso();
            else if (r < 0.75f) EmpezarSueno();
            else NuevoPuntoPatrulla();
        }

        void ImitarAlLider()
        {
            switch (lider.currentState)
            {
                case RaptorState.Dormir:
                    EmpezarSueno();
                    break;
                case RaptorState.Olfatear:
                    animQuieta = Random.value < 0.7f ? "Sniff" : "Idle2";
                    duracion = Random.Range(4f, 7f);
                    Cambiar(RaptorState.Olfatear);
                    break;
                default:
                    EmpezarDescanso();
                    break;
            }
        }

        void EmpezarDescanso()
        {
            animQuieta = Random.value < 0.6f ? "Idle" : "Idle2";
            duracion = Random.Range(4f, 8f);
            Cambiar(RaptorState.Descansar);
        }

        void EmpezarSueno()
        {
            IniciarAccion("GoToSleep", 3f);
            animQuieta = "Sleep";
            duracion = Random.Range(18f, 35f);
            Cambiar(RaptorState.Dormir);
        }

        void NuevoPuntoPatrulla()
        {
            Vector2 c = Random.insideUnitCircle * radioPatrulla;
            destino = centroTerritorio + new Vector3(c.x, 0f, c.y);
            animQuieta = "Idle";
            Cambiar(RaptorState.Patrullar);
        }

        void SeguirHueco()
        {
            Vector3 hueco = HuecoEnManada();
            float dist = Plano(hueco - transform.position).magnitude;
            float vel = dist > 15f ? VelCorrer * 0.7f : VelCaminar * Mathf.Lerp(0.8f, 1.2f, Mathf.InverseLerp(2f, 10f, dist));
            Locomover(hueco, vel, 1.2f);
        }

        Vector3 HuecoEnManada()
        {
            if (lider == null) return transform.position;
            return lider.transform.position + Quaternion.Euler(0f, lider.yaw, 0f) * offsetManada;
        }

        bool LejosDeSuHueco(float margen) => Plano(HuecoEnManada() - transform.position).magnitude > margen;

        void Cambiar(RaptorState s)
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
